using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Install;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>
    /// The one telemetry service the runtime records into. Boot and Reload build it from
    /// the settings in force (Configure), which keeps the running one when they have not
    /// changed, the Settings window rebuilds it when its telemetry changes, and
    /// app-closing stops it, which flushes what is left.
    /// <see cref="Current"/> is never null: with nothing configured it is a service that
    /// records nothing, so a caller never checks.
    ///
    /// Also the one place a run becomes a record (ForButton, ForHook, ForRun, then
    /// Finish), so a click, a hook and a pynavis run fill the fields the same way. None of
    /// it ever throws into the run it describes.
    /// </summary>
    public static class TelemetryHost
    {
        private static TelemetryService _current = Off();

        /// <summary>Services Install replaced whose final flush may still be under way, for
        /// app-closing to wait on.</summary>
        private static readonly List<TelemetryService> Retiring = new List<TelemetryService>();

        /// <summary>The service in force; one that records nothing until configured.</summary>
        public static TelemetryService Current => Volatile.Read(ref _current);

        /// <summary>%APPDATA%\pyNavis\telemetry: one spool file per destination.</summary>
        public static string SpoolDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "telemetry");

        /// <summary>%PROGRAMDATA%\pyNavis\telemetry.json, the file a firm deploys.</summary>
        public static string FirmPath => TelemetryConfig.DefaultFirmPath;

        /// <summary>The extensions behind the ribbon, so a pynavis run of a bundle's
        /// script.py is recorded under that button. RuntimeHost points it at its own list.</summary>
        internal static Func<IEnumerable<ExtensionModel>> Extensions { get; set; } =
            () => Enumerable.Empty<ExtensionModel>();

        private static TelemetryService Off() => TelemetryService.Create(null, null, null);

        // ---- lifetime ----------------------------------------------------------------

        /// <summary>
        /// Builds the service for <paramref name="user"/> (config.json's "telemetry"
        /// section, overridden by a trusted firm file) and installs it in place of the one
        /// in force. Settings that match the running service's leave it running: Settings'
        /// Save configures and then asks for a Reload that configures again. Called on the
        /// UI thread, so it never waits on a destination. Never throws.
        /// </summary>
        public static void Configure(TelemetryConfig user) =>
            Configure(user, FirmPath, SpoolDir, MachineFolderTrust.IsAdminOnlyFile);

        internal static void Configure(TelemetryConfig user, string firmPath, string spoolDir, Func<FileStream, bool> adminOnly)
        {
            try
            {
                var settings = InForce(user, firmPath, adminOnly, Log.Error);
                var current = Current;
                var inForce = current.Config;
                if (!current.IsStopped && SameSettings(settings, inForce) && settings.Managed == inForce.Managed)
                {
                    Log.Info(Describe(current, firmPath));
                    return;
                }
                var next = TelemetryService.Create(settings, spoolDir, Log.Error);
                Install(next);
                Log.Info(Describe(next, firmPath));
            }
            catch (Exception ex)
            {
                Log.Error("Telemetry could not start", ex);
            }
        }

        /// <summary>
        /// Makes <paramref name="next"/> (null for none) the service in force and returns
        /// the old one. The old one is stopped without waiting: its worker flushes what it
        /// holds and lets go of its sinks on a thread of its own, so a slow or dead
        /// destination never holds up Save or Reload. The new worker's first flush waits
        /// for that to finish, because both send the same spool files.
        /// </summary>
        internal static TelemetryService Install(TelemetryService next)
        {
            var service = next ?? Off();
            var old = Interlocked.Exchange(ref _current, service);
            WaitHandle predecessor = null;
            if (old != null && !ReferenceEquals(old, service))
            {
                try
                {
                    old.Stop(TimeSpan.Zero);
                    predecessor = old.Finished;
                    lock (Retiring)
                    {
                        Retiring.RemoveAll(s => s.Finished.WaitOne(0));
                        Retiring.Add(old);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Telemetry: the old service did not stop cleanly", ex);
                }
            }
            try { service.Start(predecessor); }
            catch (Exception ex) { Log.Error("Telemetry: the service did not start", ex); }
            return old;
        }

        /// <summary>App-closing: stops the worker, which flushes once more, waiting at most
        /// TelemetryService.StopWait. A service Install replaced moments before may still be
        /// on its own final flush; it gets what is left of the same wait. Never throws.</summary>
        public static void Stop()
        {
            var clock = Stopwatch.StartNew();
            try { Current.Stop(); }
            catch (Exception ex) { Log.Error("Telemetry did not stop cleanly", ex); }
            try
            {
                TelemetryService[] retiring;
                lock (Retiring) retiring = Retiring.ToArray();
                foreach (var old in retiring)
                {
                    var left = TelemetryService.StopWait - clock.Elapsed;
                    if (left <= TimeSpan.Zero) break;
                    old.Finished.WaitOne(left);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Telemetry: an earlier service did not stop cleanly", ex);
            }
        }

        /// <summary>
        /// Flushes now on a thread of its own and waits at most <paramref name="wait"/>.
        /// pynavis run closes its Navisworks straight after the run, before the worker's
        /// next flush and without the app-closing event a ribbon session stops on, so the
        /// flush tries every destination, including one waiting out a retry delay: parked
        /// for later, the run's record would wait for some other session. True when the
        /// flush finished in time (or there was nothing to do).
        /// </summary>
        public static bool FlushWithin(TimeSpan wait)
        {
            var service = Current;
            if (!service.IsActive) return true;
            try
            {
                var thread = new Thread(() => service.FlushNow()) { IsBackground = true, Name = "pyNavis telemetry flush" };
                thread.Start();
                return thread.Join(wait);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Describe(TelemetryService service, string firmPath)
        {
            var config = service.Config;
            var from = config.Managed ? " (managed by " + firmPath + ")" : "";
            if (!service.IsActive) return "Telemetry is off" + from + ".";
            var targets = string.Join(" and ", service.Sinks.Select(s => s.Target));
            return "Telemetry is on" + from + ": records go to " + targets
                   + (config.IncludeHooks ? ", hooks included." : ".");
        }

        // ---- the firm file -----------------------------------------------------------

        /// <summary>The settings in force for <paramref name="user"/>: the firm file's when
        /// only administrators can change it and it parses, otherwise the user's own. The
        /// file is checked and read through one handle (TelemetryConfig.Resolve).</summary>
        public static TelemetryConfig InForce(TelemetryConfig user) =>
            InForce(user, FirmPath, MachineFolderTrust.IsAdminOnlyFile, Log.Error);

        /// <param name="adminOnly">Whether the open firm file may be trusted; tests pass
        /// their own, production MachineFolderTrust.IsAdminOnlyFile.</param>
        internal static TelemetryConfig InForce(TelemetryConfig user, string firmPath,
            Func<FileStream, bool> adminOnly, Action<string> logError) =>
            TelemetryConfig.Resolve(user, firmPath, adminOnly, logError);

        // ---- runs into records ------------------------------------------------------

        /// <summary>Whether the service in force keeps a record of this kind: on, and for
        /// a hook, with includeHooks on too. Never throws.</summary>
        public static bool Wants(string kind)
        {
            try
            {
                var service = Current;
                if (!service.IsActive) return false;
                return !string.Equals(kind, "hook", StringComparison.OrdinalIgnoreCase)
                       || service.Config.IncludeHooks;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The record for a ribbon button's run, kind "click" (script.py) or
        /// "config" (config.py, Shift+Click); null when telemetry does not want it.
        /// Never throws.</summary>
        public static TelemetryRecord ForButton(PushButtonModel button, string kind)
        {
            try
            {
                if (button == null || !Wants(kind)) return null;
                return NewRecord(kind, button.BundleKey, button.Title,
                    ExtensionOf(button.ExtensionName), button.EngineId, ActiveDocumentFileName());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The record for a hook's run: its id ("Extension:event") as the key and
        /// its event as the title; null unless includeHooks is on. Never throws.</summary>
        public static TelemetryRecord ForHook(HookModel hook)
        {
            try
            {
                if (hook == null || !Wants("hook")) return null;
                return NewRecord("hook", hook.Id, hook.EventName, hook.ExtensionName, hook.EngineId,
                    ActiveDocumentFileName());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The record for a pynavis run of <paramref name="scriptPath"/>: a
        /// bundle's script.py is recorded under that button, any other script under its
        /// file name. The document is the open one, or the model the run was given.
        /// Null when telemetry is off. Never throws.</summary>
        public static TelemetryRecord ForRun(string scriptPath, string engineId, string model)
        {
            try
            {
                if (!Wants("run")) return null;
                var (key, title, extension) = RunIdentity(scriptPath, Extensions());
                var doc = ActiveDocumentFileName();
                return NewRecord("run", key, title, extension, engineId, doc.Length > 0 ? doc : model);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Completes <paramref name="record"/> (null does nothing) and queues it:
        /// how long the run took, whether it worked, and the error's summary line. Never
        /// throws, so recording can never break the run it describes.</summary>
        public static void Finish(TelemetryRecord record, long ms, bool succeeded, string errorText)
        {
            if (record == null) return;
            try
            {
                record.Ms = Math.Max(0, ms);
                record.Result = succeeded ? "ok" : "error";
                record.Error = succeeded ? "" : TelemetryRecord.ErrorSummary(errorText);
                Current.Record(record);
            }
            catch (Exception)
            {
                // Telemetry never breaks the tool it watches.
            }
        }

        internal static TelemetryRecord NewRecord(string kind, string key, string title, string extension,
            string engine, string doc) => new TelemetryRecord
        {
            Time = DateTimeOffset.Now,
            Session = TelemetryRecord.CurrentSession,
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Navis = NavisYear(),
            PyNavis = PyNavisVersion.Product,
            Doc = doc ?? "",
            Kind = kind ?? "",
            Key = key ?? "",
            Title = OneLine(title),
            Extension = extension ?? "",
            Engine = engine ?? "",
        };

        /// <summary>The host year, "2026"; "" when it cannot be told.</summary>
        private static string NavisYear()
        {
            var year = PyNavisVersion.HostYear;
            return year > 0 ? year.ToString(CultureInfo.InvariantCulture) : "";
        }

        /// <summary><paramref name="text"/> with every run of line breaks (and the spaces
        /// around it) replaced by one space, trimmed; "" for null.</summary>
        internal static string OneLine(string text) =>
            text == null ? "" : Regex.Replace(text, @"\s*[\r\n]+\s*", " ").Trim();

        /// <summary>The extension's name, trimmed; "" when the model does not carry one. The
        /// bundle key is no stand-in: it is extension-relative, so its first segment is the
        /// tab folder, not the extension.</summary>
        internal static string ExtensionOf(string extensionName) =>
            string.IsNullOrWhiteSpace(extensionName) ? "" : extensionName.Trim();

        /// <summary>Key, title and extension for a pynavis run: those of the button whose
        /// script.py it is, else the script's file name, its name without ".py" (the
        /// __title__ the runner gives it) and no extension.</summary>
        internal static (string Key, string Title, string Extension) RunIdentity(string scriptPath,
            IEnumerable<ExtensionModel> extensions)
        {
            var script = scriptPath ?? "";
            var full = FullPathOrSame(script);
            foreach (var extension in extensions ?? Enumerable.Empty<ExtensionModel>())
            foreach (var button in extension.Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons))
            {
                if (string.IsNullOrEmpty(button.ScriptPath)) continue;
                if (string.Equals(FullPathOrSame(button.ScriptPath), full, StringComparison.OrdinalIgnoreCase))
                    return (button.BundleKey ?? "", OneLine(button.Title),
                        ExtensionOf(string.IsNullOrWhiteSpace(button.ExtensionName) ? extension.Name : button.ExtensionName));
            }
            return (TelemetryRecord.FileNameOnly(script), Path.GetFileNameWithoutExtension(TelemetryRecord.FileNameOnly(script)), "");
        }

        private static string FullPathOrSame(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception) { return path; }
        }

        /// <summary>The active document's file name, "" when there is none or the API
        /// cannot be reached. Never throws.</summary>
        internal static string ActiveDocumentFileName()
        {
            try { return ReadActiveDocumentFileName() ?? ""; }
            catch (Exception) { return ""; }
        }

        // Its own method, so a missing API assembly (the tests) fails inside the try above.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadActiveDocumentFileName()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            return doc == null ? "" : doc.FileName;
        }

        // ---- Settings' Send test record ---------------------------------------------

        /// <summary>
        /// Sends one record of kind "test" now and returns the line for the status bar:
        /// "Test record sent to &lt;targets&gt;." or "Test record could not be sent to
        /// &lt;target&gt;: &lt;reason&gt;" for each destination that did not take it.
        /// When <paramref name="entered"/> (what the window shows) matches the service in
        /// force, that service sends it (SendNow, past its queue, so the worker's own flush
        /// cannot take it), trying every destination at once even while one waits out a
        /// retry delay. Settings typed in but not saved yet get a one-off service
        /// of their own, with a spool that is thrown away, so the test answers for what is on
        /// the screen and leaves nothing to retry. Writes to the share or the network on the
        /// calling thread: call it off the UI thread. Never throws.
        /// </summary>
        public static string SendTestRecord(TelemetryConfig entered) =>
            SendTestRecord(entered, Current, Path.GetTempPath());

        internal static string SendTestRecord(TelemetryConfig entered, TelemetryService inForce, string scratchRoot)
        {
            try
            {
                var record = NewRecord("test", "test", "Test record", "", "", "");
                var settings = (entered ?? inForce.Config).Clone();
                var unsaved = !SameSettings(settings, inForce.Config);
                if (inForce.IsActive && !unsaved)
                {
                    var sent = inForce.SendNow(record);
                    if (sent.Failures.Count > 0 || sent.Sent == 0) return TestFailure(sent, inForce.Sinks);
                    return "Test record sent to " + string.Join(" and ", inForce.Sinks.Select(s => s.Target)) + ".";
                }

                if (!settings.HasTarget) return "Name a folder or a URL first.";
                settings.Enabled = true;                    // a test is sent on request, on or off
                var spool = Path.Combine(scratchRoot, "pyNavis-telemetry-test-" + Guid.NewGuid().ToString("N"));
                var probe = TelemetryService.Create(settings, spool, _ => { });
                try
                {
                    if (!string.IsNullOrWhiteSpace(settings.Url) && probe.Sinks.All(s => s.Name != "http"))
                        return "The URL must start with http:// or https://.";
                    var result = probe.SendNow(record);
                    if (result.Failures.Count > 0 || result.Sent == 0) return TestFailure(result, probe.Sinks);
                    if (!(entered?.Enabled ?? true))
                        return inForce.Config.Managed && !unsaved
                            ? "Test record sent. Your firm has telemetry turned off."
                            : "Test record sent. Telemetry stays off until you turn it on and save.";
                    return unsaved ? "Test record sent. Save to keep these settings." : "Test record sent.";
                }
                finally
                {
                    foreach (var sink in probe.Sinks) (sink as IDisposable)?.Dispose();
                    try { if (Directory.Exists(spool)) Directory.Delete(spool, true); }
                    catch (Exception) { }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Telemetry: the test record failed", ex);
                return "The test failed: " + ex.Message;
            }
        }

        /// <summary>The answer for a test record a flush did not get everywhere: one
        /// sentence per destination that did not take it, then the ones that did.</summary>
        private static string TestFailure(FlushResult result, IReadOnlyList<ITelemetrySink> sinks)
        {
            if (result.Failures.Count == 0) return "The test record was not sent.";
            var failed = new HashSet<string>(result.Failures.Select(f => f.Target), StringComparer.Ordinal);
            var text = string.Join(" ", result.Failures.Select(f => $"Test record could not be sent to {f.Target}: {f.Reason}"));
            var reached = sinks.Select(s => s.Target).Where(t => !failed.Contains(t)).ToList();
            return reached.Count == 0 ? text : text + " It reached " + string.Join(" and ", reached) + ".";
        }

        /// <summary>The four settings a person can change, compared the way they are
        /// saved: trimmed, a folder without regard to case.</summary>
        internal static bool SameSettings(TelemetryConfig left, TelemetryConfig right)
        {
            if (left == null || right == null) return left == right;
            return left.Enabled == right.Enabled
                   && left.IncludeHooks == right.IncludeHooks
                   && string.Equals((left.Folder ?? "").Trim(), (right.Folder ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                   && string.Equals((left.Url ?? "").Trim(), (right.Url ?? "").Trim(), StringComparison.Ordinal);
        }
    }
}
