using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using PyNavis.Cli;
using PyNavis.Shared;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The orchestration of <c>pynavis run</c>, driven through a fake automation session
    /// that answers each request the way the runner add-in would.
    /// </summary>
    public class RunLoopTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 4, 9, 30, 0);

        private readonly string _dir;
        private readonly string _runsRoot;
        private readonly string _script;
        private readonly string _modelA;
        private readonly string _modelB;
        private readonly StringWriter _console = new StringWriter();
        private readonly FakeSession _session = new FakeSession();
        /// <summary>The Roamer process ids the fake process list reports as running.</summary>
        private readonly List<int> _livePids = new List<int>();
        private readonly List<int> _killed = new List<int>();

        public RunLoopTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            _runsRoot = Path.Combine(_dir, "runs");
            Directory.CreateDirectory(_dir);
            _script = Path.Combine(_dir, "audit.py");
            File.WriteAllText(_script, "print('hello')");
            _modelA = Path.Combine(_dir, "a.nwd");
            _modelB = Path.Combine(_dir, "b.nwd");
            _kill = EndProcess;
        }

        public void Dispose()
        {
            _session.Release();
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string RunDir => Path.Combine(_runsRoot, "20261004-093000");

        private int[] LivePids()
        {
            lock (_livePids) return _livePids.ToArray();
        }

        /// <summary>What the fake kill does after recording the pid; ends the process by default.</summary>
        private Func<int, bool> _kill;

        private bool Kill(int pid)
        {
            lock (_livePids) _killed.Add(pid);
            return _kill(pid);
        }

        // Ends the fake process and reports that the kill ended it.
        private bool EndProcess(int pid)
        {
            lock (_livePids) _livePids.Remove(pid);
            return true;
        }

        // A generous dispose wait: only the tests that need Dispose to hang shorten it, so
        // a slow machine never turns a dispose that returns into one that "did not close".
        private RunLoop NewLoop(Func<IAutomationSession> factory = null, string runsRoot = null,
            TextWriter console = null, TimeSpan? disposeWait = null) =>
            new RunLoop(factory ?? (() => _session), runsRoot ?? _runsRoot, console ?? _console, () => Now,
                disposeWait ?? TimeSpan.FromSeconds(10), LivePids, Kill, TimeSpan.FromMilliseconds(200));

        private int Run(params string[] argv)
        {
            var args = RunArgs.Parse(argv, out var error);
            Assert.Null(error);
            return NewLoop().Run(args, "2026");
        }

        private Dictionary<string, object>[] Outcomes(string runDir = null)
        {
            var text = File.ReadAllText(Path.Combine(runDir ?? RunDir, "results.json"));
            var json = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text);
            return ((object[])json["models"]).Cast<Dictionary<string, object>>().ToArray();
        }

        private Dictionary<string, object> Summary(string runDir = null) =>
            (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(
                File.ReadAllText(Path.Combine(runDir ?? RunDir, "results.json")));

        [Fact]
        public void NoModels_RunsOnce_WithANullModel()
        {
            var exit = Run(_script);

            Assert.Equal(0, exit);
            Assert.Empty(_session.Opened);
            var request = Assert.Single(_session.Requests);
            Assert.Null(request.Model);
            Assert.Empty(request.Models);
            Assert.Equal(_script, request.Script);
            Assert.Equal(RunDir, request.RunDir);
            Assert.True(File.Exists(Path.Combine(RunDir, "request-0.json")));
            Assert.Equal(Path.Combine(RunDir, "response-0.json"), request.ResponsePath);
            Assert.Equal(Path.Combine(RunDir, "transcript-0.txt"), request.TranscriptPath);
            var outcome = Assert.Single(Outcomes());
            Assert.Equal("ok", outcome["status"]);
            Assert.Null(outcome["model"]);
            Assert.True(_session.Disposed);
            Assert.Contains("Summary: " + Path.Combine(RunDir, "results.json"), _console.ToString());
            Assert.Equal(false, Summary()["cancelled"]);
        }

        [Fact]
        public void TwoModels_OpenInOrder_AndEachRequestNamesBoth()
        {
            var exit = Run(_script, _modelA, _modelB, "--", "--fix", "7");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { _modelA, _modelB }, _session.Opened);
            Assert.Equal(2, _session.Requests.Count);
            Assert.Equal(_modelA, _session.Requests[0].Model);
            Assert.Equal(_modelB, _session.Requests[1].Model);
            foreach (var request in _session.Requests)
            {
                Assert.Equal(new[] { _modelA, _modelB }, request.Models);
                Assert.Equal(new[] { "--fix", "7" }, request.Args);
            }
            Assert.True(File.Exists(Path.Combine(RunDir, "request-1.json")));
            Assert.True(File.Exists(Path.Combine(RunDir, "request-2.json")));
            Assert.Equal(new[] { "PyNavis.Run.PYNV", "PyNavis.Run.PYNV" }, _session.PluginIds);

            var text = _console.ToString();
            Assert.Contains("Opening a.nwd...", text);
            Assert.Contains("a.nwd ... ok (1.2 s)", text);
            Assert.Contains("b.nwd ... ok (1.2 s)", text);
            Assert.Contains("  hello from a.nwd" + Environment.NewLine + "  second line", text);
            Assert.Contains("  hello from b.nwd", text);
            Assert.All(Outcomes(), o => Assert.Equal("ok", o["status"]));
        }

        [Fact]
        public void AFailedScript_KeepsOnlyTheTracebacksLastLine_AndExitsOne()
        {
            _session.Respond = r => r.Model == _modelB
                ? new RunResponse { Succeeded = false, Error = "Traceback (most recent call last):\r\n  File \"audit.py\", line 1, in <module>\r\nZeroDivisionError: division by zero\r\n\r\n", DurationMs = 10 }
                : new RunResponse { Succeeded = true, DurationMs = 1200 };

            var exit = Run(_script, _modelA, _modelB);

            Assert.Equal(1, exit);
            var outcomes = Outcomes();
            Assert.Equal("ok", outcomes[0]["status"]);
            Assert.Equal("failed", outcomes[1]["status"]);
            Assert.Equal("ZeroDivisionError: division by zero", outcomes[1]["error"]);
            Assert.Contains("b.nwd ... FAILED: ZeroDivisionError: division by zero", _console.ToString());
        }

        [Fact]
        public void AOneLineError_IsKeptWhole()
        {
            _session.Respond = r => new RunResponse { Succeeded = false, Error = "  Model has no clash tests  " };

            Run(_script, _modelA);

            Assert.Equal("Model has no clash tests", Assert.Single(Outcomes())["error"]);
            Assert.Contains("a.nwd ... FAILED: Model has no clash tests", _console.ToString());
        }

        [Fact]
        public void AMissingResponse_IsAFailure_ThatNamesTheAttachedYear()
        {
            _session.Respond = r => null;
            _session.NoResponseCode = 0;

            var exit = Run(_script, _modelA);

            Assert.Equal(1, exit);
            var outcome = Assert.Single(Outcomes());
            Assert.Equal("failed", outcome["status"]);
            Assert.Equal("No response from the add-in. Is pyNavis attached to Navisworks 2026?", outcome["error"]);
        }

        [Fact]
        public void AMissingResponse_WithAReturnCode_NamesTheCode_AndTheLogs()
        {
            _session.Respond = r => null;
            _session.NoResponseCode = 2;

            var exit = Run(_script, _modelA);

            Assert.Equal(1, exit);
            Assert.Equal(
                @"No response from the add-in (return code 2). Is pyNavis attached to Navisworks 2026? See %APPDATA%\pyNavis\logs.",
                Assert.Single(Outcomes())["error"]);
        }

        [Fact]
        public void AModelThatWillNotOpen_IsRecorded_AndTheNextStillRuns()
        {
            _session.OpenThrows = path => path == _modelA ? new IOException("Not a Navisworks file") : null;

            var exit = Run(_script, _modelA, _modelB);

            Assert.Equal(1, exit);
            var outcomes = Outcomes();
            Assert.Equal("open-failed", outcomes[0]["status"]);
            Assert.Equal("Not a Navisworks file", outcomes[0]["error"]);
            Assert.Equal("ok", outcomes[1]["status"]);
            var request = Assert.Single(_session.Requests);
            Assert.Equal(_modelB, request.Model);
        }

        [Fact]
        public void Save_SavesAfterASuccess_NotAfterAFailure()
        {
            _session.Respond = r => new RunResponse { Succeeded = r.Model == _modelA, Error = r.Model == _modelA ? null : "Nope" };

            var exit = Run(_script, _modelA, _modelB, "--save");

            Assert.Equal(1, exit);
            Assert.Equal(new[] { _modelA }, _session.Saved);
        }

        [Fact]
        public void Save_ThatThrows_IsSaveFailed()
        {
            _session.SaveThrows = path => new UnauthorizedAccessException("The file is read-only");

            var exit = Run(_script, _modelA, "--save");

            Assert.Equal(1, exit);
            var outcome = Assert.Single(Outcomes());
            Assert.Equal("save-failed", outcome["status"]);
            Assert.Equal("The file is read-only", outcome["error"]);
        }

        private const string Hint = "Timed out after 1 s. A dialog the script did not guard with __runner__, or a first-run dialog on a fresh install, blocks a hidden Navisworks. Run again with --show to see it.";

        [Fact]
        public void Timeout_PrintsTheHint_ThenDisposesTheSession_AndStopsTheLoop()
        {
            _session.BlockExecute = r => true;
            string consoleAtDispose = null;
            var resultsAtDispose = true;
            _session.OnDispose = () =>
            {
                consoleAtDispose = _console.ToString();
                resultsAtDispose = File.Exists(Path.Combine(RunDir, "results.json"));
            };

            var exit = Run(_script, _modelA, _modelB, "--timeout", "1");

            Assert.Equal(1, exit);
            Assert.Equal(1, _session.DisposeCalls);
            Assert.Contains(Hint, consoleAtDispose);          // the hint went out before the dispose
            Assert.DoesNotContain("Summary:", consoleAtDispose);
            Assert.False(resultsAtDispose);                    // and the dispose came before the summary
            Assert.Equal(new[] { _modelA }, _session.Opened);
            var outcomes = Outcomes();
            Assert.Equal(2, outcomes.Length);
            Assert.Equal("timed-out", outcomes[0]["status"]);
            Assert.Equal("not-run", outcomes[1]["status"]);
        }

        private const string DidNotClose = "Navisworks did not close. Close it from Task Manager (Roamer.exe).";

        [Fact]
        public void Timeout_WithADisposeThatHangs_StillFinishesTheRun()
        {
            _session.BlockExecute = r => true;
            _session.DisposeHangs = true;
            var args = RunArgs.Parse(new[] { _script, _modelA, "--timeout", "1" }, out _);

            var exit = NewLoop(disposeWait: TimeSpan.FromMilliseconds(300)).Run(args, "2026");

            Assert.Equal(1, exit);
            var text = _console.ToString();
            Assert.Contains(Hint, text);
            Assert.Contains(DidNotClose, text);
            Assert.Contains("Summary: ", text);
            Assert.Equal("timed-out", Assert.Single(Outcomes())["status"]);
            // The dispose runs on a thread of its own: wait for it to start, never assume it has.
            Assert.True(_session.DisposeEntered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, _session.DisposeCalls);
        }

        [Fact]
        public void ADisposeThatThrows_IsNotClosed_AndSaysWhy()
        {
            _session.DisposeThrows = new InvalidOperationException("The RPC server is unavailable.");

            var exit = Run(_script);

            Assert.Equal(0, exit);
            var text = _console.ToString();
            Assert.Contains("Closing Navisworks failed: The RPC server is unavailable.", text);
            Assert.Contains(DidNotClose, text);
        }

        [Fact]
        public void ARoamerStartedByTheSession_ThatOutlivesTheDispose_IsKilled()
        {
            _livePids.Add(100);                       // the user's own Navisworks, running before
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242); // the session's hidden one
                return _session;
            });

            var exit = loop.Run(args, "2026");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { 4242 }, _killed);
            Assert.Contains("Closed Navisworks (process 4242) that did not exit on its own.", _console.ToString());
            Assert.DoesNotContain(DidNotClose, _console.ToString());
        }

        [Fact]
        public void ARoamerThatExitsWithTheDispose_IsNotKilled_NorOneRunningBefore()
        {
            _livePids.Add(100);
            _session.OnDispose = () => { lock (_livePids) _livePids.Remove(4242); };
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242);
                return _session;
            });

            loop.Run(args, "2026");

            Assert.Empty(_killed);
            Assert.DoesNotContain("Closed Navisworks", _console.ToString());
        }

        [Fact]
        public void AHungDispose_KillsTheSessionsRoamer_AndNotThePreExistingOne()
        {
            _livePids.Add(100);
            _session.BlockExecute = r => true;
            _session.DisposeHangs = true;
            var args = RunArgs.Parse(new[] { _script, _modelA, "--timeout", "1" }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242);
                return _session;
            }, disposeWait: TimeSpan.FromMilliseconds(300));

            var exit = loop.Run(args, "2026");

            Assert.Equal(1, exit);
            Assert.Equal(new[] { 4242 }, _killed);
            Assert.Contains("Closed Navisworks (process 4242) that did not exit on its own.", _console.ToString());
            Assert.DoesNotContain(DidNotClose, _console.ToString());
        }

        [Fact]
        public void Timeout_AppliesToOpeningAModel()
        {
            _session.BlockOpen = true;

            var exit = Run(_script, _modelA, _modelB, "--timeout", "1");

            Assert.Equal(1, exit);
            Assert.Empty(_session.Requests);
            Assert.Equal("timed-out", Outcomes()[0]["status"]);
            Assert.Contains(Hint, _console.ToString());
        }

        [Fact]
        public void ATimeout_ListsEveryUnreachedModel_AsNotRun()
        {
            var modelC = Path.Combine(_dir, "c.nwd");
            _session.BlockExecute = r => r.Model == _modelB;

            var exit = Run(_script, _modelA, _modelB, modelC, "--timeout", "1");

            Assert.Equal(1, exit);
            var outcomes = Outcomes();
            Assert.Equal(new object[] { _modelA, _modelB, modelC }, outcomes.Select(o => o["model"]).ToArray());
            Assert.Equal(new object[] { "ok", "timed-out", "not-run" }, outcomes.Select(o => o["status"]).ToArray());
            Assert.Equal("Not run: the run ended after an earlier model.", outcomes[2]["error"]);
            Assert.Equal(new[] { _modelA, _modelB }, _session.Opened);
            Assert.Equal(false, Summary()["cancelled"]);
        }

        [Fact]
        public void Cancel_MidModel_EndsTheRun_WithThePartialSummary()
        {
            var modelC = Path.Combine(_dir, "c.nwd");
            _session.BlockExecute = r => r.Model == _modelB;
            var loop = NewLoop();
            var args = RunArgs.Parse(new[] { _script, _modelA, _modelB, modelC }, out _);
            var canceller = new Thread(() =>
            {
                if (_session.Blocked.Wait(TimeSpan.FromSeconds(10))) loop.Cancel();
            });
            canceller.Start();

            var exit = loop.Run(args, "2026");
            canceller.Join();

            Assert.Equal(1, exit);
            Assert.True(_session.Disposed);
            var text = _console.ToString();
            Assert.Contains("Cancelled.", text);
            Assert.Contains("Summary: ", text);
            // Every model is listed: the one in flight as cancelled, the one never reached as not run.
            var outcomes = Outcomes();
            Assert.Equal(new object[] { _modelA, _modelB, modelC }, outcomes.Select(o => o["model"]).ToArray());
            Assert.Equal(new object[] { "ok", "cancelled", "not-run" }, outcomes.Select(o => o["status"]).ToArray());
            Assert.Equal("Cancelled while the script was running.", outcomes[1]["error"]);
            Assert.Equal("Not run: the run ended after an earlier model.", outcomes[2]["error"]);
            Assert.Equal(true, Summary()["cancelled"]);
        }

        [Fact]
        public void Cancel_BeforeAnyModelStarted_SaysSoForEveryModel()
        {
            RunLoop loop = null;
            // Ctrl+C lands while Navisworks is still starting, before the first model.
            loop = NewLoop(() =>
            {
                loop.Cancel();
                return _session;
            });
            var args = RunArgs.Parse(new[] { _script, _modelA, _modelB }, out _);

            var exit = loop.Run(args, "2026");

            Assert.Equal(1, exit);
            Assert.Empty(_session.Calls);
            Assert.True(_session.Disposed);
            Assert.Contains("Cancelled.", _console.ToString());
            var outcomes = Outcomes();
            Assert.Equal(new object[] { _modelA, _modelB }, outcomes.Select(o => o["model"]).ToArray());
            Assert.All(outcomes, o => Assert.Equal("not-run", o["status"]));
            Assert.All(outcomes, o => Assert.Equal("Not run: the run was cancelled before this model started.", o["error"]));
            Assert.Equal(true, Summary()["cancelled"]);
        }

        [Fact]
        public void TheHostPidAResponseNamed_IsTheOnlyOneEnded_NeverADiffPid()
        {
            _livePids.Add(100);
            _session.Respond = r => new RunResponse { Succeeded = true, DurationMs = 5, HostPid = 777 };
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                // Two new Roamers during the start: the diff alone could not tell them apart.
                lock (_livePids) { _livePids.Add(4242); _livePids.Add(777); }
                return _session;
            });

            var exit = loop.Run(args, "2026");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { 777 }, _killed);
            Assert.Contains("Closed Navisworks (process 777) that did not exit on its own.", _console.ToString());
            Assert.DoesNotContain("4242", _console.ToString());
        }

        [Fact]
        public void TheHostPidAResponseNamed_ThatExited_LeavesTheDiffPidsAlone()
        {
            _session.Respond = r => new RunResponse { Succeeded = true, DurationMs = 5, HostPid = 777 };
            _session.OnDispose = () => { lock (_livePids) _livePids.Remove(777); };
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) { _livePids.Add(4242); _livePids.Add(777); }
                return _session;
            });

            loop.Run(args, "2026");

            Assert.Empty(_killed);
            Assert.DoesNotContain("Closed Navisworks", _console.ToString());
        }

        [Fact]
        public void WithNoResponseEver_TheDiffDecides()
        {
            _livePids.Add(100);
            _session.OpenThrows = path => new IOException("Not a Navisworks file");
            var args = RunArgs.Parse(new[] { _script, _modelA, _modelB }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242);
                return _session;
            });

            loop.Run(args, "2026");

            Assert.Empty(_session.Requests);
            Assert.Equal(new[] { 4242 }, _killed);
        }

        [Fact]
        public void Cancel_BeforeTheSave_NeverStartsIt_AndKeepsTheOutcome()
        {
            RunLoop loop = null;
            // Ctrl+C lands right after the script reported ok, before the save step.
            var console = new CancellingWriter(line => line.Contains("a.nwd ... ok"), () => loop.Cancel());
            loop = NewLoop(console: console);
            var args = RunArgs.Parse(new[] { _script, _modelA, "--save" }, out _);

            var exit = loop.Run(args, "2026");

            Assert.Equal(1, exit);
            Assert.Empty(_session.Saved);
            Assert.Equal(0, _session.SaveCalls);
            Assert.Contains("Cancelled.", console.ToString());
            var outcome = Assert.Single(Outcomes());
            Assert.Equal(_modelA, outcome["model"]);
            Assert.Equal("ok", outcome["status"]);
            Assert.Equal("Cancelled before the save; the model was not saved.", outcome["error"]);
            Assert.Contains("a.nwd ... not saved: cancelled", console.ToString());
        }

        [Fact]
        public void Cancel_BeforeTheModelOpened_SaysSo_AndNeverOpensIt()
        {
            RunLoop loop = null;
            // Ctrl+C lands after the Opening line, before the open starts.
            var console = new CancellingWriter(line => line == "Opening a.nwd...", () => loop.Cancel());
            loop = NewLoop(console: console);
            var args = RunArgs.Parse(new[] { _script, _modelA, _modelB }, out _);

            var exit = loop.Run(args, "2026");

            Assert.Equal(1, exit);
            Assert.DoesNotContain("open", _session.Calls);
            var outcomes = Outcomes();
            Assert.Equal(new object[] { "cancelled", "not-run" }, outcomes.Select(o => o["status"]).ToArray());
            Assert.Equal("Cancelled before the model opened.", outcomes[0]["error"]);
        }

        [Fact]
        public void Cancel_BeforeTheScriptRan_SaysSo_AndNeverRunsIt()
        {
            RunLoop loop = null;
            // Ctrl+C lands while --show brings the window up, after the open and before the script.
            _session.OnSetVisible = visible => loop.Cancel();
            loop = NewLoop();
            var args = RunArgs.Parse(new[] { _script, _modelA, "--show" }, out _);

            var exit = loop.Run(args, "2026");

            Assert.Equal(1, exit);
            Assert.Equal(new[] { _modelA }, _session.Opened);
            Assert.Empty(_session.Requests);
            var outcome = Assert.Single(Outcomes());
            Assert.Equal("cancelled", outcome["status"]);
            Assert.Equal("Cancelled before the script ran.", outcome["error"]);
        }

        [Fact]
        public void AKillThatFindsTheProcessAlreadyGone_SaysNothingAboutClosing()
        {
            _kill = pid => { EndProcess(pid); return false; };
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242);
                return _session;
            });

            var exit = loop.Run(args, "2026");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { 4242 }, _killed);
            var text = _console.ToString();
            Assert.DoesNotContain("Closed Navisworks", text);
            Assert.DoesNotContain("Could not close", text);
            Assert.DoesNotContain(DidNotClose, text);
        }

        [Fact]
        public void AKillThatThrows_ForAProcessThatExitedMeanwhile_SaysNothingAboutClosing()
        {
            _kill = pid => { EndProcess(pid); throw new InvalidOperationException("No process is associated with this object."); };
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242);
                return _session;
            });

            loop.Run(args, "2026");

            var text = _console.ToString();
            Assert.DoesNotContain("Could not close", text);
            Assert.DoesNotContain(DidNotClose, text);
        }

        [Fact]
        public void AKillThatThrows_ForAProcessStillRunning_SaysItDidNotClose()
        {
            _kill = pid => throw new System.ComponentModel.Win32Exception(5, "Access is denied");
            var args = RunArgs.Parse(new[] { _script }, out _);
            var loop = NewLoop(() =>
            {
                lock (_livePids) _livePids.Add(4242);
                return _session;
            });

            loop.Run(args, "2026");

            var text = _console.ToString();
            Assert.Contains("Could not close Navisworks (process 4242): Access is denied", text);
            Assert.Contains(DidNotClose, text);
        }

        [Fact]
        public void AHostPidThatWasRunningBeforeTheSession_IsNeverEnded()
        {
            _livePids.Add(100);                       // the user's own Navisworks
            _session.Respond = r => new RunResponse { Succeeded = true, DurationMs = 5, HostPid = 100 };
            var args = RunArgs.Parse(new[] { _script }, out _);

            var exit = NewLoop().Run(args, "2026");

            Assert.Equal(0, exit);
            Assert.Empty(_killed);
            Assert.DoesNotContain("Closed Navisworks", _console.ToString());
        }

        [Fact]
        public void Cancel_DuringASaveThatFinishes_WaitsForIt_AndKeepsTheOutcome()
        {
            var gate = new ManualResetEventSlim();
            _session.SaveGate = gate;
            RunLoop loop = null;
            // The save finishes only once the run has seen the cancel and is waiting on it.
            var console = new CancellingWriter(line => line.StartsWith("Waiting for the save", StringComparison.Ordinal), () => gate.Set());
            loop = NewLoop(console: console);
            var args = RunArgs.Parse(new[] { _script, _modelA, _modelB, "--save" }, out _);
            var canceller = new Thread(() =>
            {
                if (_session.Blocked.Wait(TimeSpan.FromSeconds(10))) loop.Cancel();
            });
            canceller.Start();

            var exit = loop.Run(args, "2026");
            canceller.Join();

            Assert.Equal(1, exit);
            Assert.Equal(new[] { _modelA }, _session.Saved);
            Assert.Contains("Waiting for the save of a.nwd to finish before closing Navisworks.", console.ToString());
            var outcomes = Outcomes();
            Assert.Equal("ok", outcomes[0]["status"]);
            Assert.Equal("not-run", outcomes[1]["status"]);
            Assert.True(_session.SaveFinishedBeforeDispose);
        }

        [Fact]
        public void Cancel_DuringASaveThatNeverFinishes_IsSaveFailed()
        {
            _session.BlockSave = true;
            var loop = NewLoop();
            var args = RunArgs.Parse(new[] { _script, _modelA, "--save", "--timeout", "2" }, out _);
            var canceller = new Thread(() =>
            {
                if (_session.Blocked.Wait(TimeSpan.FromSeconds(10))) loop.Cancel();
            });
            canceller.Start();

            var exit = loop.Run(args, "2026");
            canceller.Join();

            Assert.Equal(1, exit);
            Assert.Equal(1, _session.SaveCalls);
            var outcome = Assert.Single(Outcomes());
            Assert.Equal("save-failed", outcome["status"]);
            Assert.Equal("Cancelled during the save; the file may be incomplete.", outcome["error"]);
            Assert.Contains("a.nwd ... could not save: Cancelled during the save; the file may be incomplete.", _console.ToString());
        }

        [Fact]
        public void Save_OfAFileThatIsNotNwdOrNwf_IsSkipped_AndDoesNotFailTheRun()
        {
            var ifc = Path.Combine(_dir, "c.ifc");
            var nwf = Path.Combine(_dir, "d.NWF");

            var exit = Run(_script, ifc, nwf, "--save");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { nwf }, _session.Saved);
            var outcomes = Outcomes();
            Assert.Equal("save-skipped", outcomes[0]["status"]);
            Assert.Equal("Only .nwd and .nwf files are saved back.", outcomes[0]["error"]);
            Assert.Equal("ok", outcomes[1]["status"]);
            Assert.Contains("c.ifc ... save skipped: Only .nwd and .nwf files are saved back.", _console.ToString());
        }

        [Fact]
        public void ARunFolderThatCannotBeMade_NeverStartsNavisworks()
        {
            var blocked = Path.Combine(_dir, "runs-is-a-file");
            File.WriteAllText(blocked, "");
            var started = false;
            var args = RunArgs.Parse(new[] { _script, _modelA }, out _);

            var exit = NewLoop(() => { started = true; return _session; }, blocked).Run(args, "2026");

            Assert.Equal(1, exit);
            Assert.False(started);
            Assert.Contains("Could not create the run folder under " + blocked, _console.ToString());
        }

        [Fact]
        public void ASessionThatWillNotStart_ExitsThree()
        {
            var args = RunArgs.Parse(new[] { _script, _modelA }, out _);

            var exit = NewLoop(() => throw new InvalidOperationException("Roamer.exe would not start")).Run(args, "2026");

            Assert.Equal(3, exit);
            Assert.Contains("Could not start Navisworks 2026: Roamer.exe would not start", _console.ToString());
        }

        [Fact]
        public void AMissingModelList_IsAUsageError()
        {
            var exit = Run(_script, "--models", Path.Combine(_dir, "nope.txt"));

            Assert.Equal(2, exit);
            Assert.Empty(_session.Requests);
        }

        [Fact]
        public void TheModelList_AddsTrimmedLines_AfterThePositionalModels()
        {
            var list = Path.Combine(_dir, "models.txt");
            File.WriteAllLines(list, new[] { "# the B side", "", "  b.nwd  ", "   " });

            var exit = Run(_script, _modelA, "--models", list);

            Assert.Equal(0, exit);
            Assert.Equal(new[] { _modelA, _modelB }, _session.Opened);
        }

        [Fact]
        public void ARunFolderAnotherRunOwns_GetsASuffix()
        {
            Directory.CreateDirectory(RunDir);
            File.WriteAllText(Path.Combine(RunDir, ".owner"), "");

            Run(_script);

            Assert.Equal(RunDir + "-2", Assert.Single(_session.Requests).RunDir);
            Assert.Single(Outcomes(RunDir + "-2"));
            Assert.True(File.Exists(Path.Combine(RunDir + "-2", ".owner")));
            Assert.False(File.Exists(Path.Combine(RunDir, "results.json")));
        }

        [Fact]
        public void ASessionThatWillNotStart_LeavesNoRunFolder()
        {
            var args = RunArgs.Parse(new[] { _script }, out _);

            NewLoop(() => throw new InvalidOperationException("No licence")).Run(args, "2026");

            Assert.False(Directory.Exists(RunDir));
        }

        [Fact]
        public void Results_CopiesTheSummary()
        {
            var copy = Path.Combine(_dir, "out", "summary.json");

            Run(_script, "--results", copy);

            Assert.Equal(File.ReadAllText(Path.Combine(RunDir, "results.json")), File.ReadAllText(copy));
        }

        /// <summary>A console that runs an action when a matching line is written.</summary>
        private sealed class CancellingWriter : StringWriter
        {
            private readonly Func<string, bool> _when;
            private readonly Action _then;

            public CancellingWriter(Func<string, bool> when, Action then)
            {
                _when = when;
                _then = then;
            }

            public override void WriteLine(string value)
            {
                base.WriteLine(value);
                if (_when(value ?? "")) _then();
            }
        }

        /// <summary>
        /// Records every call and plays the runner add-in: reads the request it is handed
        /// and writes the response and transcript there.
        /// </summary>
        // Field-measured 2026-10-04: OpenFile into a visible Navisworks 2026 window ends the
        // process, so --show opens every model hidden and shows the window afterwards.
        [Fact]
        public void Show_OpensEachModelHidden_ThenShowsTheWindow()
        {
            var exit = Run(_script, _modelA, _modelB, "--show");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { "open", "show", "execute", "hide", "open", "show", "execute" }, _session.Calls);
        }

        [Fact]
        public void Show_WithNoModel_ShowsTheWindowBeforeTheScript()
        {
            var exit = Run(_script, "--show");

            Assert.Equal(0, exit);
            Assert.Equal(new[] { "show", "execute" }, _session.Calls);
        }

        [Fact]
        public void WithoutShow_TheWindowIsNeverTouched()
        {
            Run(_script, _modelA, _modelB);

            Assert.DoesNotContain("show", _session.Calls);
            Assert.DoesNotContain("hide", _session.Calls);
        }

        private sealed class FakeSession : IAutomationSession
        {
            private readonly ManualResetEventSlim _disposed = new ManualResetEventSlim();

            public readonly List<string> Opened = new List<string>();
            /// <summary>Every call in order: open, show, hide, execute, save.</summary>
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Saved = new List<string>();
            public readonly List<string> PluginIds = new List<string>();
            public readonly List<RunRequest> Requests = new List<RunRequest>();
            public Func<RunRequest, RunResponse> Respond = r => new RunResponse { Succeeded = true, DurationMs = 1200 };
            /// <summary>What ExecuteAddInPlugin returns when Respond gives no response.</summary>
            public int NoResponseCode = 2;
            /// <summary>When set, SaveFile waits for it (and sets Blocked first).</summary>
            public ManualResetEventSlim SaveGate;
            public volatile bool SaveFinishedBeforeDispose;
            /// <summary>Thrown by Dispose after it has done its work.</summary>
            public Exception DisposeThrows;
            /// <summary>Set the moment Dispose is entered.</summary>
            public readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            public Func<string, Exception> OpenThrows = path => null;
            public Func<string, Exception> SaveThrows = path => null;
            /// <summary>Which requests hang until the session is disposed, like a modal dialog.</summary>
            public Func<RunRequest, bool> BlockExecute = r => false;
            /// <summary>OpenFile hangs until the session is disposed.</summary>
            public bool BlockOpen;
            /// <summary>SaveFile hangs until the session is disposed.</summary>
            public bool BlockSave;
            public int SaveCalls;
            /// <summary>Dispose never returns, like a Navisworks stuck in a dialog.</summary>
            public bool DisposeHangs;
            public Action OnDispose = () => { };
            /// <summary>Set when a call starts hanging.</summary>
            public readonly ManualResetEventSlim Blocked = new ManualResetEventSlim();
            public int DisposeCalls;
            public bool Disposed => _disposed.IsSet;

            private void HangUntilDisposed()
            {
                Blocked.Set();
                _disposed.Wait();
                throw new ObjectDisposedException("NavisworksApplication");
            }

            /// <summary>Runs inside SetVisible, after the call is recorded.</summary>
            public Action<bool> OnSetVisible = visible => { };

            public void SetVisible(bool visible)
            {
                Calls.Add(visible ? "show" : "hide");
                OnSetVisible(visible);
            }

            public void OpenFile(string path)
            {
                Calls.Add("open");
                if (BlockOpen) HangUntilDisposed();
                var ex = OpenThrows(path);
                if (ex != null) throw ex;
                Opened.Add(path);
            }

            public int ExecuteAddInPlugin(string pluginId, params string[] args)
            {
                Calls.Add("execute");
                PluginIds.Add(pluginId);
                var request = RunRequest.Load(Assert.Single(args));
                Requests.Add(request);
                if (BlockExecute(request)) HangUntilDisposed();
                var name = request.Model == null ? "no model" : Path.GetFileName(request.Model);
                File.WriteAllText(request.TranscriptPath, "hello from " + name + Environment.NewLine + "second line" + Environment.NewLine);
                var response = Respond(request);
                if (response == null) return NoResponseCode;
                response.Save(request.ResponsePath);
                return response.Succeeded ? 0 : 1;
            }

            public void SaveFile(string path)
            {
                Calls.Add("save");
                Interlocked.Increment(ref SaveCalls);
                if (BlockSave) HangUntilDisposed();
                if (SaveGate != null)
                {
                    Blocked.Set();
                    SaveGate.Wait();
                }
                var ex = SaveThrows(path);
                if (ex != null) throw ex;
                Saved.Add(path);
                if (!_disposed.IsSet) SaveFinishedBeforeDispose = true;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCalls);
                DisposeEntered.Set();
                OnDispose();
                _disposed.Set();
                if (DisposeHangs) _release.Wait(); // a thread the loop abandons
                if (DisposeThrows != null) throw DisposeThrows;
            }

            private readonly ManualResetEventSlim _release = new ManualResetEventSlim();

            /// <summary>Lets a hanging Dispose return, at the end of the test.</summary>
            public void Release() => _release.Set();
        }
    }
}
