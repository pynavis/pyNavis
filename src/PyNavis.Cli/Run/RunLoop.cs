using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PyNavis.Shared;

namespace PyNavis.Cli
{
    /// <summary>
    /// The heart of <c>pynavis run</c>: starts one Navisworks, then for each model opens
    /// it, hands the runner add-in a request file, waits for its response, reports, and
    /// optionally saves. Every file it trades with the add-in, and the results.json
    /// summary, lands in a fresh run folder under the runs root.
    /// </summary>
    public sealed class RunLoop
    {
        /// <summary>The runner add-in's plugin id (the loader's RunCommand).</summary>
        public const string PluginId = "PyNavis.Run.PYNV";

        /// <summary>Statuses in results.json, beyond <c>ok</c>, <c>failed</c>, <c>open-failed</c>,
        /// <c>save-failed</c> and <c>timed-out</c>.</summary>
        public const string SaveSkipped = "save-skipped", NotRun = "not-run", Cancelled = "cancelled";

        private const string TimeoutHint =
            "A dialog the script did not guard with __runner__, or a first-run dialog on a fresh install, blocks a hidden Navisworks. Run again with --show to see it.";

        private const string DidNotClose = "Navisworks did not close. Close it from Task Manager (Roamer.exe).";

        // How long a Ctrl+C waits for a save in flight when --timeout is not set.
        private static readonly TimeSpan CancelledSaveWait = TimeSpan.FromSeconds(60);

        private readonly Func<IAutomationSession> _openSession;
        private readonly string _runsRoot;
        private readonly TextWriter _console;
        private readonly Func<DateTime> _now;
        private readonly TimeSpan _disposeWait;
        private readonly Func<int[]> _roamerPids;
        /// <summary>True while the Navisworks window is shown (<c>--show</c> only).</summary>
        private bool _shown;
        private readonly Func<int, bool> _killPid;
        private readonly TimeSpan _exitGrace;
        private readonly ManualResetEventSlim _cancel = new ManualResetEventSlim();
        private IAutomationSession _session;
        // The Roamer processes that appeared while the session started: the run's own, as
        // far as a before/after diff can tell.
        private int[] _ownPids = new int[0];
        // The Roamer processes running before the session started: never the run's own.
        private int[] _before = new int[0];
        // The Navisworks that answered a request names itself: exact, so it wins over the diff.
        private int _hostPid;

        // Skipped: the cancel was set before the call started, so it never ran.
        // Cancelled: the cancel landed while the call was running.
        private enum Wait { Done, TimedOut, Skipped, Cancelled, Abandoned }

        /// <param name="openSession">Starts the Navisworks the run drives.</param>
        /// <param name="runsRoot">Where run folders go, normally %APPDATA%\pyNavis\runs.</param>
        /// <param name="console">Where progress lines go.</param>
        /// <param name="now">The clock, for the run folder name and the summary times.</param>
        /// <param name="disposeWait">How long closing Navisworks may take before the run
        /// gives up on it and says so; 15 seconds when null.</param>
        /// <param name="roamerPids">The ids of the running Roamer.exe processes; none when null.</param>
        /// <param name="killPid">Ends a process by id: true when it ended one, false when there
        /// was nothing to end (it had exited, or the id is no longer a Roamer). Never called
        /// when null.</param>
        /// <param name="exitGrace">How long a Roamer the run started may take to exit after
        /// a dispose that returned, before it is ended; 10 seconds when null.</param>
        public RunLoop(Func<IAutomationSession> openSession, string runsRoot, TextWriter console, Func<DateTime> now,
            TimeSpan? disposeWait = null, Func<int[]> roamerPids = null, Func<int, bool> killPid = null,
            TimeSpan? exitGrace = null)
        {
            _openSession = openSession;
            _runsRoot = runsRoot;
            _console = console;
            _now = now;
            _disposeWait = disposeWait ?? TimeSpan.FromSeconds(15);
            _roamerPids = roamerPids ?? (() => new int[0]);
            _killPid = killPid ?? (pid => false);
            _exitGrace = exitGrace ?? TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Stops the run (Ctrl+C). Safe from any thread and returns at once: the call in
        /// flight is abandoned (a save is waited for, within a bound), and the run closes
        /// its Navisworks and writes the summary.
        /// </summary>
        public void Cancel() => _cancel.Set();

        /// <summary>
        /// Runs <paramref name="args"/> in Navisworks <paramref name="year"/>. Returns 0 when
        /// every model was ok, 1 when any failed or timed out (or the run folder could not be
        /// made), 2 for a usage error, 3 when Navisworks would not start.
        /// </summary>
        public int Run(RunArgs args, string year)
        {
            var started = _now();
            var script = Path.GetFullPath(args.Script);
            if (!File.Exists(script))
            {
                _console.WriteLine("Script not found: " + script);
                return 2;
            }
            var models = ResolveModels(args);
            if (models == null) return 2;

            // Before Navisworks starts, so a folder that cannot be made never strands one.
            string runDir;
            try
            {
                runDir = NewRunDir(started);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Could not create the run folder under {_runsRoot}: {ex.Message}");
                return 1;
            }
            var summary = new RunSummary { Script = script, Year = year, StartedAt = Iso(started) };
            var entries = models.Count == 0 ? new string[] { null } : models.ToArray();

            var before = _before = RoamerPids();
            try
            {
                _session = _openSession();
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Could not start Navisworks {year}: {ex.Message}");
                try
                {
                    File.Delete(Path.Combine(runDir, OwnerMarker));
                    Directory.Delete(runDir);
                }
                catch { /* an empty folder is no harm */ }
                return 3;
            }
            // Any Roamer that was not running before is this session's: the only ones the
            // run may end if they outlive the dispose. The user's own are never touched.
            _ownPids = RoamerPids().Except(before).ToArray();

            var reached = 0;
            try
            {
                while (reached < entries.Length && !_cancel.IsSet)
                {
                    var i = reached++;
                    var outcome = RunOne(args, year, script, models, entries[i], models.Count == 0 ? 0 : i + 1, runDir);
                    if (outcome == null) break;
                    summary.Models.Add(outcome);
                    if (outcome.Status == "timed-out" || outcome.Status == Cancelled) break;
                }
            }
            finally
            {
                DisposeSession();
            }
            // Every model the run never reached is still listed, so results.json names them all.
            // None reached means Ctrl+C landed before the first model: no earlier model ran.
            var notRun = reached == 0 && _cancel.IsSet
                ? "Not run: the run was cancelled before this model started."
                : "Not run: the run ended after an earlier model.";
            for (var j = reached; j < entries.Length; j++)
                summary.Models.Add(new ModelOutcome { Model = entries[j], Status = NotRun, Error = notRun });
            summary.Cancelled = _cancel.IsSet;
            if (summary.Cancelled) _console.WriteLine("Cancelled.");

            summary.FinishedAt = Iso(_now());
            var resultsPath = Path.Combine(runDir, "results.json");
            summary.Save(resultsPath);
            if (args.ResultsPath != null)
            {
                var copy = Path.GetFullPath(args.ResultsPath);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(resultsPath, copy, true);
            }
            _console.WriteLine("Summary: " + resultsPath);

            var allOk = !_cancel.IsSet && summary.Models.Count == entries.Length
                && summary.Models.All(m => m.Status == "ok" || m.Status == SaveSkipped);
            return allOk ? 0 : 1;
        }

        // One model (or the one no-model pass). Status cancelled when Ctrl+C landed before or
        // while it opened or its script ran; a cancel during the save waits for it, within a
        // bound, and one before the save keeps the script's ok and says the model was not saved.
        // Null only when the session is already gone.
        private ModelOutcome RunOne(RunArgs args, string year, string script, List<string> models,
            string model, int n, string runDir)
        {
            var session = _session;
            if (session == null) return null;
            var name = Path.GetFileName(model ?? script);
            var outcome = new ModelOutcome { Model = model };
            var watch = Stopwatch.StartNew();
            Exception thrown;

            if (model != null)
            {
                // Opening into a visible window ends Navisworks (measured on 2026), so each
                // model opens hidden; --show brings the window back once it is in.
                if (_shown) SetVisible(session, false, args);
                _console.WriteLine($"Opening {name}...");
                switch (Bounded(() => session.OpenFile(model), args.TimeoutSeconds, null, out thrown))
                {
                    case Wait.Skipped: return CancelledOutcome(outcome, watch, "Cancelled before the model opened.");
                    case Wait.Cancelled: return CancelledOutcome(outcome, watch, "Cancelled while the model was opening.");
                    case Wait.TimedOut: return TimedOut(outcome, args.TimeoutSeconds, watch);
                }
                if (thrown != null) return Report(outcome, "open-failed", thrown.Message, $"{name} ... could not open: ");
            }
            if (args.Show && !_shown) SetVisible(session, true, args);

            var request = new RunRequest
            {
                Script = script,
                Model = model,
                Models = models.ToArray(),
                Args = args.ScriptArgs.ToArray(),
                RunDir = runDir,
                ResponsePath = Path.Combine(runDir, $"response-{n}.json"),
                TranscriptPath = Path.Combine(runDir, $"transcript-{n}.txt"),
            };
            var requestPath = Path.Combine(runDir, $"request-{n}.json");
            request.Save(requestPath);

            var returnCode = 0;
            switch (Bounded(() => returnCode = session.ExecuteAddInPlugin(PluginId, requestPath), args.TimeoutSeconds, null, out thrown))
            {
                case Wait.Skipped: return CancelledOutcome(outcome, watch, "Cancelled before the script ran.");
                case Wait.Cancelled: return CancelledOutcome(outcome, watch, "Cancelled while the script was running.");
                case Wait.TimedOut: return TimedOut(outcome, args.TimeoutSeconds, watch);
            }

            RunResponse response = null;
            string error;
            if (thrown != null)
                error = thrown.Message;
            else if (!File.Exists(request.ResponsePath))
                error = returnCode == 0
                    ? $"No response from the add-in. Is pyNavis attached to Navisworks {year}?"
                    : $@"No response from the add-in (return code {returnCode}). Is pyNavis attached to Navisworks {year}? See %APPDATA%\pyNavis\logs.";
            else
            {
                try
                {
                    response = RunResponse.Load(request.ResponsePath);
                    // A pid that was running before the session started is never this run's.
                    if (response.HostPid > 0 && !_before.Contains(response.HostPid)) _hostPid = response.HostPid;
                    error = response.Succeeded ? null : response.Error ?? "The script failed.";
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            outcome.DurationMs = response?.DurationMs ?? watch.ElapsedMilliseconds;
            outcome.ResultJson = response?.ResultJson;
            if (error == null)
            {
                outcome.Status = "ok";
                _console.WriteLine($"{name} ... ok ({(outcome.DurationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} s)");
            }
            else Report(outcome, "failed", error, $"{name} ... FAILED: ");
            PrintTranscript(request.TranscriptPath);

            if (args.Save && model != null && outcome.Status == "ok")
            {
                if (!IsNavisworksFile(model))
                    return Report(outcome, SaveSkipped, "Only .nwd and .nwf files are saved back.", $"{name} ... save skipped: ");

                // A save cut off by the dispose can leave a broken file, so a cancel waits for it.
                var onCancel = $"Waiting for the save of {name} to finish before closing Navisworks.";
                switch (Bounded(() => session.SaveFile(model), args.TimeoutSeconds, onCancel, out thrown))
                {
                    case Wait.Skipped:
                        // Never started: the script's ok stands, and results.json says the save did not run.
                        outcome.Error = "Cancelled before the save; the model was not saved.";
                        _console.WriteLine($"{name} ... not saved: cancelled");
                        return outcome;
                    case Wait.Abandoned:
                        return Report(outcome, "save-failed", "Cancelled during the save; the file may be incomplete.", $"{name} ... could not save: ");
                    case Wait.TimedOut: return TimedOut(outcome, args.TimeoutSeconds, watch);
                }
                if (thrown != null) Report(outcome, "save-failed", thrown.Message, $"{name} ... could not save: ");
            }
            return outcome;
        }

        private static bool IsNavisworksFile(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase);
        }

        // Runs one session call on a thread of its own, so a Navisworks stuck in a dialog
        // cannot hang the CLI: waits for the call, the timeout (0: none) or a cancel,
        // whichever comes first. thrown is what a finished call raised, or null. Skipped
        // when the cancel was already set, so the call never started.
        // finishOnCancel non-null: a cancel prints it and waits on for the call (to the
        // step's own timeout, or CancelledSaveWait without one), Done when it finishes
        // in time and Abandoned when it does not.
        private Wait Bounded(Action call, int timeoutSeconds, string finishOnCancel, out Exception thrown)
        {
            thrown = null;
            // Once cancelled, start nothing: a save begun now would be cut off by the dispose.
            if (_cancel.IsSet) return Wait.Skipped;
            var watch = Stopwatch.StartNew();
            var task = Start(call);
            var limit = timeoutSeconds == 0 ? Timeout.Infinite : timeoutSeconds * 1000;
            var woke = WaitHandle.WaitAny(new[] { ((IAsyncResult)task).AsyncWaitHandle, _cancel.WaitHandle }, limit);
            if (woke == WaitHandle.WaitTimeout) return Wait.TimedOut;
            if (woke == 1 || (_cancel.IsSet && !task.IsCompleted))
            {
                if (finishOnCancel == null) return Wait.Cancelled;
                _console.WriteLine(finishOnCancel);
                var rest = timeoutSeconds == 0
                    ? CancelledSaveWait
                    : TimeSpan.FromMilliseconds(Math.Max(0, limit - watch.ElapsedMilliseconds));
                if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(rest)) return Wait.Abandoned;
            }
            thrown = task.Exception?.GetBaseException();
            return Wait.Done;
        }

        // A thread of its own, not the pool's: a COM call into Navisworks may block for hours.
        private static Task Start(Action call) =>
            Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // The hint goes out first: closing a Navisworks stuck in a dialog can itself hang.
        private ModelOutcome TimedOut(ModelOutcome outcome, int seconds, Stopwatch watch)
        {
            outcome.Status = "timed-out";
            outcome.DurationMs = watch.ElapsedMilliseconds;
            outcome.Error = $"Timed out after {seconds} s.";
            _console.WriteLine(outcome.Error + " " + TimeoutHint);
            DisposeSession();
            return outcome;
        }

        private static ModelOutcome CancelledOutcome(ModelOutcome outcome, Stopwatch watch, string error)
        {
            outcome.Status = Cancelled;
            outcome.DurationMs = watch.ElapsedMilliseconds;
            outcome.Error = error;
            return outcome;
        }

        private ModelOutcome Report(ModelOutcome outcome, string status, string error, string prefix)
        {
            outcome.Status = status;
            outcome.Error = LastLine(error);
            _console.WriteLine(prefix + outcome.Error);
            return outcome;
        }

        private void PrintTranscript(string path)
        {
            if (!File.Exists(path)) return;
            var text = File.ReadAllText(path).TrimEnd('\r', '\n');
            if (text.Length == 0) return;
            foreach (var line in text.Split('\n'))
                _console.WriteLine("  " + line.TrimEnd('\r'));
        }

        // Positional models, then the --models list's lines (relative to the list's folder).
        private List<string> ResolveModels(RunArgs args)
        {
            var models = args.Models.Select(Path.GetFullPath).ToList();
            if (args.ModelsFile == null) return models;

            var list = Path.GetFullPath(args.ModelsFile);
            if (!File.Exists(list))
            {
                _console.WriteLine("Model list not found: " + list);
                return null;
            }
            foreach (var raw in File.ReadAllLines(list))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                models.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(list), line)));
            }
            return models;
        }

        private const string OwnerMarker = ".owner";

        // Two runs started in the same second must not share a folder. CreateDirectory
        // succeeds for both, so the claim is the marker file: CreateNew lets only one
        // process make it, and the other moves on to the next suffix.
        private string NewRunDir(DateTime started)
        {
            var stem = Path.Combine(_runsRoot, started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            for (var k = 1; ; k++)
            {
                var dir = k == 1 ? stem : stem + "-" + k;
                Directory.CreateDirectory(dir);
                var marker = Path.Combine(dir, OwnerMarker);
                try
                {
                    using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write)) { }
                    return dir;
                }
                catch (IOException) when (File.Exists(marker))
                {
                    // Another run owns this one.
                }
            }
        }

        // Once only (the timeout path or the loop's finally, whichever is first), and
        // bounded: a Navisworks that will not close is reported, not waited out. A Roamer
        // the session started that is still alive afterwards is ended.
        /// <summary>Shows or hides the window within the usual bound. A failure here is
        /// reported and the run goes on: the window is a convenience, the script is the job.</summary>
        private void SetVisible(IAutomationSession session, bool visible, RunArgs args)
        {
            Exception thrown;
            var wait = Bounded(() => session.SetVisible(visible), args.TimeoutSeconds, null, out thrown);
            if (wait == Wait.Done && thrown == null) { _shown = visible; return; }
            if (wait == Wait.Skipped || wait == Wait.Cancelled) return; // the run is ending: no failure to report
            var what = visible ? "show" : "hide";
            _console.WriteLine(thrown != null
                ? $"Could not {what} the Navisworks window: {thrown.Message}"
                : $"Could not {what} the Navisworks window in time.");
        }

        private void DisposeSession()
        {
            var session = Interlocked.Exchange(ref _session, null);
            if (session == null) return;
            var closing = Start(session.Dispose);
            bool closed;
            try
            {
                closed = closing.Wait(_disposeWait);
            }
            catch (AggregateException ex)
            {
                // It returned, but by throwing: Navisworks may well still be running.
                closed = false;
                _console.WriteLine("Closing Navisworks failed: " + LastLine(ex.GetBaseException().Message));
            }

            // The pid a response named is exact; the diff is the fallback for a run in which
            // the add-in never answered. With a named pid, no diff pid is ever ended.
            var candidates = _hostPid > 0 ? new[] { _hostPid } : _ownPids;
            var unresolved = !closed && candidates.Length == 0; // nothing known to end
            foreach (var pid in Survivors(candidates, closed ? _exitGrace : TimeSpan.Zero))
            {
                try
                {
                    // False: it was gone by the time of the kill, which is what the run wanted.
                    if (_killPid(pid))
                        _console.WriteLine($"Closed Navisworks (process {pid}) that did not exit on its own.");
                }
                catch (Exception ex)
                {
                    // A kill can fail because the process exited meanwhile: only a survivor is a problem.
                    if (!RoamerPids().Contains(pid)) continue;
                    unresolved = true;
                    _console.WriteLine($"Could not close Navisworks (process {pid}): {ex.Message}");
                }
            }
            if (unresolved) _console.WriteLine(DidNotClose);
        }

        // The candidates still alive as Roamer processes after up to grace of waiting for
        // them to exit. Checked against the live Roamer list, so a reused pid is never ended.
        private int[] Survivors(int[] candidates, TimeSpan grace)
        {
            if (candidates.Length == 0) return candidates;
            var watch = Stopwatch.StartNew();
            while (true)
            {
                var alive = candidates.Intersect(RoamerPids()).ToArray();
                if (alive.Length == 0 || watch.Elapsed >= grace) return alive;
                Thread.Sleep(100);
            }
        }

        private int[] RoamerPids()
        {
            try
            {
                return _roamerPids() ?? new int[0];
            }
            catch
            {
                return new int[0]; // no list means nothing is the run's, so nothing is ended
            }
        }

        // A traceback ends with its point ("ZeroDivisionError: division by zero"), so the
        // last non-empty line is the one worth a summary; a one-line text is kept whole.
        private static string LastLine(string text)
        {
            var line = (text ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
            return line ?? "The script failed.";
        }

        private static string Iso(DateTime time) =>
            time.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
