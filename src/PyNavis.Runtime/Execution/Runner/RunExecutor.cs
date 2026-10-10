using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Output;
using PyNavis.Runtime.Telemetry;
using PyNavis.Shared;

namespace PyNavis.Runtime.Execution.Runner
{
    /// <summary>
    /// Runs one <c>pynavis run</c> request: the script once, against whatever model the CLI
    /// has open, with its output in the transcript file and its __result__ handed back in
    /// the response. No ribbon session is involved, so no command events and no hooks.
    /// </summary>
    public static class RunExecutor
    {
        private const string ResultGlobal = "__result__";

        /// <summary>
        /// Runs <paramref name="request"/>'s script on <paramref name="engine"/> inside a
        /// runner context writing to the request's transcript. Whatever goes wrong once the
        /// transcript is open (a script that raises, an engine that throws, a run already in
        /// progress) comes back as a failed response; only a transcript that cannot be
        /// created throws.
        /// </summary>
        public static RunResponse Execute(RunRequest request, IEngine engine, PyNavisHost host)
        {
            var response = ExecuteCore(request, engine, host);
            response.HostPid = HostPid;
            return response;
        }

        // This Navisworks: the CLI ends exactly this process if it will not close.
        private static int HostPid
        {
            get
            {
                using (var self = Process.GetCurrentProcess()) return self.Id;
            }
        }

        private static RunResponse ExecuteCore(RunRequest request, IEngine engine, PyNavisHost host)
        {
            using (var transcript = new StreamWriter(request.TranscriptPath, false, new UTF8Encoding(false)) { AutoFlush = true })
            {
                IDisposable context;
                try
                {
                    context = RunnerContext.Enter(transcript);
                }
                catch (InvalidOperationException ex)
                {
                    // Another run owns the context: leave it exactly as it is.
                    Log.Error($"Refused pynavis run of '{request.Script}': {ex.Message}");
                    transcript.WriteLine("ERROR: " + ex.Message);
                    return new RunResponse { Succeeded = false, Error = ex.Message };
                }

                // The context ends before its transcript closes.
                using (context)
                {
                    if (!RunGate.TryEnter("pynavis run"))
                    {
                        Log.Info($"Refused pynavis run of '{request.Script}': '{RunGate.CurrentTitle}' is still running.");
                        return new RunResponse { Succeeded = false, Error = "Another pyNavis script is still running." };
                    }
                    try
                    {
                        return Run(request, engine, host);
                    }
                    finally
                    {
                        try
                        {
                            // Same backstop as a ribbon click: a leaked ProgressScope would
                            // leave the next run's Navisworks unclickable.
                            Forms.ProgressScope.CloseAll();
                        }
                        finally
                        {
                            RunGate.Exit();
                        }
                    }
                }
            }
        }

        private static RunResponse Run(RunRequest request, IEngine engine, PyNavisHost host)
        {
            var clock = Stopwatch.StartNew();
            var script = request.Script;
            OutputWindowWriter writer = null;
            // One record per run while telemetry is on, written in the finally.
            var record = TelemetryHost.ForRun(script, engine?.Id, request.Model);
            var succeeded = false;
            string error = null;
            try
            {
                var directory = Path.GetDirectoryName(script);
                var title = Path.GetFileNameWithoutExtension(script);
                Log.Info($"pynavis run: '{script}' on model '{request.Model ?? "(none)"}'");

                writer = new OutputWindowWriter(title);
                var scriptRequest = new ScriptRequest
                {
                    ScriptPath = script,
                    Output = writer,
                    ErrorOutput = writer,
                };
                scriptRequest.SearchPaths.Add(directory);
                scriptRequest.Globals["__runner__"] = true;
                scriptRequest.Globals["__model__"] = request.Model;
                scriptRequest.Globals["__models__"] = request.Models ?? new string[0];
                scriptRequest.Globals["__args__"] = request.Args ?? new string[0];
                scriptRequest.Globals["__run_dir__"] = request.RunDir;
                scriptRequest.Globals["__file__"] = script;
                scriptRequest.Globals["__commandpath__"] = directory;
                scriptRequest.Globals["__title__"] = title;
                scriptRequest.Globals["__pynavis__"] = host;
                scriptRequest.Globals["__selfinit__"] = false;
                scriptRequest.CaptureGlobals.Add(ResultGlobal);
                host.SetCommandContext(script, directory, title, null);
                host.SetCurrentOutput(writer);

                ExecResult result;
                using (host.TelemetryScope(record))
                    result = engine.Execute(scriptRequest);
                clock.Stop();
                succeeded = result.Succeeded;
                error = result.ErrorText;

                if (!result.Succeeded)
                {
                    Log.Error($"pynavis run of '{script}' failed:\n{result.ErrorText}");
                    writer.WriteError(result.ErrorText + Environment.NewLine);
                }
                return new RunResponse
                {
                    Succeeded = result.Succeeded,
                    Error = result.Succeeded ? null : result.ErrorText,
                    DurationMs = clock.ElapsedMilliseconds,
                    ResultJson = result.Captured.TryGetValue(ResultGlobal, out var json) ? json : null,
                };
            }
            catch (Exception ex)
            {
                // e.g. an engine that cannot start: as much a failed run as a script that raised.
                clock.Stop();
                error = ex.Message;
                Log.Error($"pynavis run of '{script}' could not run", ex);
                (writer ?? new OutputWindowWriter("pynavis run"))
                    .WriteError("The script could not run: " + ex.Message + Environment.NewLine);
                return new RunResponse { Succeeded = false, Error = ex.ToString(), DurationMs = clock.ElapsedMilliseconds };
            }
            finally
            {
                // The writer goes with its transcript when Execute returns: nothing written
                // later (a hook, the next ribbon click before its own) may reach it.
                host.SetCurrentOutput(null);
                // ms is the response's DurationMs: the clock has stopped on every path.
                TelemetryHost.Finish(record, clock.ElapsedMilliseconds, succeeded, error);
            }
        }

        /// <summary>
        /// The loader add-in's entry: reads the request file the CLI wrote, makes sure the
        /// runtime is booted and startup.py has run, executes on the default engine and writes
        /// the response. Returns 0 when the script succeeded, 1 when it failed (or the run
        /// could not start), 2 when the request could not be read.
        /// </summary>
        public static int RunRequestFile(string requestPath) =>
            RunRequestFile(requestPath, () => EngineManager.GetEngine());

        /// <summary>
        /// <see cref="RunRequestFile(string)"/> with the engine supplied: the tests pass their
        /// own, since EngineManager is a process-wide singleton one test class owns.
        /// </summary>
        /// <param name="boot">Defaults to <see cref="BootAndRunStartupScripts"/>; the tests pass a
        /// no-op, since outside Navisworks Boot only fails.</param>
        internal static int RunRequestFile(string requestPath, Func<IEngine> engine, Action boot = null)
        {
            RunRequest request;
            try
            {
                request = RunRequest.Load(requestPath);
            }
            catch (Exception ex)
            {
                Log.Error($"pynavis run request '{requestPath}' could not be read", ex);
                return 2;
            }

            try
            {
                (boot ?? BootAndRunStartupScripts)();
            }
            catch (Exception ex)
            {
                // The engine does not need a booted runtime, so the run goes on regardless.
                Log.Error("pynavis run could not boot the runtime or run startup scripts", ex);
            }

            try
            {
                var response = Execute(request, engine(), PyNavisHost.Instance);
                response.Save(request.ResponsePath);
                return response.Succeeded ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Error($"pynavis run of '{request.Script}' failed", ex);
                try
                {
                    new RunResponse { Succeeded = false, Error = ex.ToString(), HostPid = HostPid }.Save(request.ResponsePath);
                }
                catch (Exception saveEx)
                {
                    Log.Error($"pynavis run response '{request.ResponsePath}' could not be written", saveEx);
                }
                return 1;
            }
            finally
            {
                // The CLI closes this Navisworks once the response is written, before the
                // telemetry worker's next flush and without the app-closing event a ribbon
                // session stops it on, so the run's record goes out now (briefly bounded).
                TelemetryHost.FlushWithin(TelemetryService.StopWait);
            }
        }

        /// <summary>
        /// Boots the runtime (a no-op after the first time) and runs startup.py once per
        /// process. startup.py runs inside a runner context of its own, so a dialog in it
        /// raises instead of blocking the hidden host, and its toasts land in the log.
        /// </summary>
        private static void BootAndRunStartupScripts()
        {
            RuntimeHost.Boot();
            using (var log = new LogWriter("startup"))
            using (RunnerContext.Enter(log))
                RuntimeHost.EnsureStartupScriptsRan();
        }
    }
}
