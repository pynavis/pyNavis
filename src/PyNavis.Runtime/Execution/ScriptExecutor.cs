using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Output;
using PyNavis.Runtime.Telemetry;

namespace PyNavis.Runtime.Execution
{
    /// <summary>Routes a ribbon button click to the right engine with pyNavis globals.</summary>
    public static class ScriptExecutor
    {
        public static void Run(PushButtonModel button) => RunScript(button, button.ScriptPath, "click");

        /// <summary>Shift+Click secondary action: the bundle's config.py, same engine
        /// and globals contract with __file__ pointing at the config script.</summary>
        public static void RunConfig(PushButtonModel button) =>
            RunScript(button, button.ConfigScriptPath, "config");

        /// <param name="kind">What telemetry records the run as: "click" or "config".</param>
        private static void RunScript(PushButtonModel button, string scriptPath, string kind)
        {
            var block = HostGate.BlockMessage(button, RuntimeHost.HostYear);
            if (block != null)
            {
                Forms.Toast.Show("warning", block, button.Title);
                return;
            }

            // A long script pumps the message loop so it can repaint, and a pump
            // dispatches queued input - including another click on a ribbon
            // button. Refuse that rather than run two scripts over one document.
            if (!RunGate.TryEnter(button.Title))
            {
                Log.Info($"Refused '{button.Title}': '{RunGate.CurrentTitle}' is still running.");
                Forms.Toast.Show("info", RunGate.CurrentTitle + " is still running",
                                 "Wait for it to finish, then try again.");
                return;
            }

            Log.Info($"Running '{button.Title}' ({scriptPath}) on engine '{button.EngineId}'");
            Events.NavisEvents.Raise(Events.NavisEvent.BeforeCommand, button.BundleKey);
            var writer = new OutputWindowWriter("pyNavis - " + button.Title);

            // Every run that got past the gates is one record, written in the finally
            // whatever happened; null (nothing recorded) while telemetry is off.
            var record = TelemetryHost.ForButton(button, kind);
            var succeeded = false;
            string error = null;
            var watch = Stopwatch.StartNew();
            try
            {
                var request = new ScriptRequest
                {
                    ScriptPath = scriptPath,
                    Output = writer,
                    ErrorOutput = writer,
                };
                foreach (var path in button.SearchPaths)
                    request.SearchPaths.Add(path);

                request.Globals["__file__"] = scriptPath;
                request.Globals["__commandpath__"] = button.Directory;
                request.Globals["__title__"] = button.Title;
                request.Globals["__selfinit__"] = false;
                request.Globals["__pynavis__"] = PyNavisHost.Instance;
                request.Globals["__runner__"] = false;   // only pynavis run sets it True
                PyNavisHost.Instance.SetCommandContext(scriptPath, button.Directory, button.Title, button.BundleKey);
                PyNavisHost.Instance.SetCurrentOutput(writer);

                ExecResult result;
                using (PyNavisHost.Instance.TelemetryScope(record))
                    result = EngineManager.GetEngine(button.EngineId).Execute(request);
                watch.Stop();
                succeeded = result.Succeeded;
                if (!result.Succeeded)
                {
                    error = result.ErrorText;
                    Log.Error($"Script '{button.Title}' failed:\n{result.ErrorText}");
                    writer.WriteError(result.ErrorText + Environment.NewLine);
                    Ai.FailureLog.Record(button.Directory, result.ErrorText);   // "Send last error" in Ask AI
                }
            }
            catch (Exception ex)
            {
                // e.g. unknown engine id, engine creation failure
                error = ex.Message;
                Log.Error($"Could not run '{button.Title}'", ex);
                writer.WriteError(ex.Message + Environment.NewLine);
                Ai.FailureLog.Record(button.Directory, ex.ToString());
            }
            finally
            {
                watch.Stop();
                TelemetryHost.Finish(record, watch.ElapsedMilliseconds, succeeded, error);
                // A leaked ProgressScope leaves Navisworks unclickable, so sweep
                // it however the script ended - the script's own try/finally is
                // the first line of defence, this is the backstop.
                Forms.ProgressScope.CloseAll();
                // after-command runs INSIDE the gate: its hook is Python too, and
                // releasing first would leave a window where a queued double-click
                // starts a second command on top of it. after-command hooks are
                // exempt from HookRunner's while-running skip, so they still fire.
                Events.NavisEvents.Raise(Events.NavisEvent.AfterCommand, button.BundleKey);
                RunGate.Exit();
                // Reload raises app-init while this run still holds the gate; now that
                // it is free, the held event reaches its hooks.
                Events.NavisEvents.FlushPending();
            }
        }
    }

    /// <summary>
    /// The <c>__pynavis__</c> global handed to every script: the scripts' door back
    /// into the runtime.
    /// </summary>
    public class PyNavisHost
    {
        public static PyNavisHost Instance { get; } = new PyNavisHost();

        private PyNavisHost() { }

        /// <summary>The product version, "1.0.0", the same one the installer carries.</summary>
        public string Version => PyNavisVersion.Product;

        /// <summary>True when the host UI renders dark; scripts theme their own dialogs with this.</summary>
        public bool IsDarkTheme => Output.PyNavisTheme.IsDark;

        /// <summary>True while the script runs under <c>pynavis run</c>: no ribbon, no
        /// window, and dialogs that need an answer throw.</summary>
        public bool IsRunner => Runner.RunnerContext.IsActive;

        /// <summary>script.py of the command currently (or most recently) executing.</summary>
        public string ScriptPath { get; private set; }

        /// <summary>Bundle folder of the command currently (or most recently) executing.</summary>
        public string CommandPath { get; private set; }

        /// <summary>Resolved title of the command currently (or most recently) executing.</summary>
        public string CommandTitle { get; private set; }

        /// <summary>
        /// Bundle key of the command currently (or most recently) executing. Set for every
        /// command, not just *.toggle bundles; null only until the first command runs, and
        /// stale (the last command's key) inside hooks, startup.py and smartbutton selfinit,
        /// none of which set a command context.
        /// </summary>
        public string CommandBundleKey { get; private set; }

        /// <summary>
        /// What to label this script's log lines with, for runs that are not a command and
        /// so have no CommandTitle of their own: hooks and startup.py. Null during ordinary
        /// command runs, where the command's title is the right label.
        /// </summary>
        /// <remarks>
        /// Without this, pynavis.script.get_logger fell back to CommandTitle, which those
        /// runs never set - so a hook's lines were attributed to whichever button the user
        /// happened to click last. Field-measured: viewpoint-recalled logged as
        /// "[Reload]".
        ///
        /// Deliberately separate from SetCommandContext rather than having hooks call that:
        /// CommandBundleKey also drives toggle state and pynavis.panes' default target, so
        /// giving a hook a command context would change behaviour well beyond a log label.
        /// </remarks>
        public string LogSource { get; set; }

        /// <summary>
        /// Called by the runtime before each script run so pynavis.script can answer
        /// "what command am I?" without inspecting scope globals.
        /// </summary>
        public void SetCommandContext(string scriptPath, string commandPath, string title, string bundleKey = null)
        {
            ScriptPath = scriptPath;
            CommandPath = commandPath;
            CommandTitle = title;
            CommandBundleKey = bundleKey;
            // A command owns its own label, and clearing here stops a hook's scope leaking
            // onto the next button clicked - the same misattribution, mirrored.
            LogSource = null;
        }

        /// <summary>Toggle state of the current toggle bundle (pynavis.script.get_toggle_state).</summary>
        public bool GetToggleState() => Ribbon.ToggleStateStore.Get(CommandBundleKey);

        /// <summary>Flips the current toggle bundle's ribbon icon (pynavis.script.set_toggle_state).</summary>
        public void SetToggleState(bool on) => Ribbon.ToggleStateStore.Set(CommandBundleKey, on);

        private OutputWindowWriter _currentOutput;

        /// <summary>Rich-output channel of the current (or most recent) run; null in consoles.</summary>
        public void SetCurrentOutput(OutputWindowWriter writer) => _currentOutput = writer;

        /// <summary>Appends an HTML fragment to the current output window (pynavis.output).</summary>
        public void WriteHtml(string html) => _currentOutput?.WriteHtml(html);

        /// <summary>True when an output window is already on screen for this run.
        /// WriteHtml OPENS one if none has, so anything that merely decorates
        /// output (the logger echoing warnings) must gate on this or it will pop
        /// an otherwise empty window for a window-less tool.</summary>
        public bool HasOpenOutput => _currentOutput != null && _currentOutput.HasOpenWindow;

        /// <summary>Writes the current output window's transcript as a standalone HTML file (pynavis.output.save).</summary>
        public void SaveOutput(string path) => _currentOutput?.SaveTranscript(path);

        /// <summary>Retitles the current output window (pynavis.output.set_title).</summary>
        public void SetOutputTitle(string title) => _currentOutput?.SetTitle(title);

        /// <summary>Shows/updates the output progress bar; fraction 0..1.</summary>
        public void Progress(double fraction, string label) =>
            _currentOutput?.Progress(fraction, label);

        /// <summary>HTML for a link that selects the given ModelItem; plain label when unavailable.</summary>
        public string ElementLink(object item, string label) =>
            _currentOutput != null ? _currentOutput.ElementLink(item, label) : label;

        /// <summary>Rescans extension folders and rebuilds the pyNavis ribbon.</summary>
        public void Reload() => RuntimeHost.Reload();

        private readonly System.Collections.Generic.Dictionary<string, object> _envVars =
            new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Session-scoped shared variable (pynavis.script.get_envvar); null when unset.</summary>
        public object GetEnvVar(string name) =>
            name != null && _envVars.TryGetValue(name, out var value) ? value : null;

        /// <summary>Sets a session variable shared across scripts; null removes it.</summary>
        public void SetEnvVar(string name, object value)
        {
            if (name == null) return;
            if (value == null) _envVars.Remove(name);
            else _envVars[name] = value;
        }

        /// <summary>Log sink for pynavis.script.get_logger; level is debug/info/success/warning/error.</summary>
        public void LogMessage(string level, string source, string message)
        {
            var line = $"[{source}] {message}";
            if (level == "error") Log.Error(line);
            else Log.Info($"[{level}] {line}");
        }

        // ---- telemetry: pynavis.telemetry.note ---------------------------------------

        private readonly object _telemetryGate = new object();
        private readonly List<TelemetryRecord> _telemetryRuns = new List<TelemetryRecord>();

        /// <summary>
        /// Called by the runtime just before a script runs: <paramref name="record"/> is
        /// the record pynavis.telemetry.note adds to until the matching EndTelemetry, or
        /// null for a run that is not recorded, so a note there goes nowhere instead of
        /// into the run around it (startup.py during a Reload click, a smartbutton
        /// refreshing at ribbon build). Calls nest; pair every one with EndTelemetry.
        /// </summary>
        public void BeginTelemetry(TelemetryRecord record)
        {
            lock (_telemetryGate) _telemetryRuns.Add(record);
        }

        /// <summary>Ends the innermost BeginTelemetry and returns its record (null for an
        /// unrecorded run, or when nothing was begun).</summary>
        public TelemetryRecord EndTelemetry()
        {
            lock (_telemetryGate)
            {
                if (_telemetryRuns.Count == 0) return null;
                var last = _telemetryRuns[_telemetryRuns.Count - 1];
                _telemetryRuns.RemoveAt(_telemetryRuns.Count - 1);
                return last;
            }
        }

        /// <summary>BeginTelemetry now, EndTelemetry on Dispose: wraps one Execute.</summary>
        internal IDisposable TelemetryScope(TelemetryRecord record)
        {
            BeginTelemetry(record);
            return new TelemetryEnd(this);
        }

        private sealed class TelemetryEnd : IDisposable
        {
            private PyNavisHost _host;
            public TelemetryEnd(PyNavisHost host) { _host = host; }

            public void Dispose()
            {
                _host?.EndTelemetry();
                _host = null;
            }
        }

        /// <summary>
        /// Merges <paramref name="values"/> into the current run's record ("results"); a
        /// name already there is replaced. The values are copied as plain JSON values now,
        /// so changing them afterwards changes nothing. Returns false, and changes nothing,
        /// outside a recorded run (telemetry off, or no run at all). Never throws.
        /// </summary>
        public bool TelemetryNote(IDictionary values)
        {
            try
            {
                if (values == null) return false;
                lock (_telemetryGate)
                {
                    var record = _telemetryRuns.Count > 0 ? _telemetryRuns[_telemetryRuns.Count - 1] : null;
                    if (record == null) return false;
                    if (record.Results == null) record.Results = new Dictionary<string, object>();
                    foreach (DictionaryEntry entry in values)
                    {
                        var name = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                        if (string.IsNullOrEmpty(name)) continue;
                        record.Results[name] = TelemetryRecord.Snapshot(entry.Value);
                    }
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// TelemetryNote for a JSON object, which is how pynavis.telemetry.note hands its
        /// values over: text crosses from IronPython and CPython alike, where a Python dict
        /// reaches .NET as an IDictionary only under IronPython. False for anything but a
        /// JSON object. Never throws.
        /// </summary>
        public bool TelemetryNoteJson(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return false;
                return new JavaScriptSerializer().DeserializeObject(json) is Dictionary<string, object> values
                       && TelemetryNote(values);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
