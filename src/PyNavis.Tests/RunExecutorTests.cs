using System;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Execution;
using PyNavis.Runtime.Execution.Runner;
using PyNavis.Shared;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// One <c>pynavis run</c> request, executed for real by IronPython: the globals the
    /// script sees, its __result__ coming back as JSON, and everything it says landing in
    /// the transcript file. Every Execute enters the process-wide RunnerContext, so these
    /// share RunnerContextTests' non-parallel collection.
    /// </summary>
    [Collection(RunnerContextCollection.Name)]
    public class RunExecutorTests : IDisposable
    {
        private const string DialogsUnavailable =
            "Dialogs are not available under pynavis run. Check __runner__ and skip the prompt.";

        private readonly string _dir;
        private readonly IronPythonEngine _engine;

        public RunExecutorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private RunRequest Request(string code, string model = null, string[] models = null, string[] args = null)
        {
            var script = Path.Combine(_dir, "count_items.py");
            File.WriteAllText(script, code);
            return new RunRequest
            {
                Script = script,
                Model = model,
                Models = models ?? new string[0],
                Args = args ?? new string[0],
                RunDir = _dir,
                ResponsePath = Path.Combine(_dir, "response-1.json"),
                TranscriptPath = Path.Combine(_dir, "transcript-1.txt"),
            };
        }

        private RunResponse Execute(RunRequest request) =>
            RunExecutor.Execute(request, _engine, PyNavisHost.Instance);

        private static string Transcript(RunRequest request) => File.ReadAllText(request.TranscriptPath);

        [Fact]
        public void Execute_ReturnsResultAsJson_AndWritesPrintsToTheTranscript()
        {
            var request = Request("print('hello')\n__result__ = 7");

            var response = Execute(request);

            Assert.True(response.Succeeded, response.Error);
            Assert.Null(response.Error);
            Assert.Equal("7", response.ResultJson);
            Assert.True(response.DurationMs >= 0);
            Assert.Contains("hello", Transcript(request));
            Assert.False(RunnerContext.IsActive);
            Assert.False(RunGate.IsHeld);
        }

        [Fact]
        public void Execute_ClearsTheHostsOutput_SoALaterWriteNeverHitsTheClosedTranscript()
        {
            var request = Request("__pynavis__.WriteHtml('<b>counted</b>')");

            var response = Execute(request);

            Assert.True(response.Succeeded, response.Error);
            Assert.Contains("counted", Transcript(request));
            Assert.False(PyNavisHost.Instance.HasOpenOutput);
            PyNavisHost.Instance.WriteHtml("late");   // must not reach the disposed writer
        }

        [Fact]
        public void Execute_LeavesResultJsonNull_WhenTheScriptSetsNoResult()
        {
            var response = Execute(Request("x = 1"));

            Assert.True(response.Succeeded, response.Error);
            Assert.Null(response.ResultJson);
        }

        [Fact]
        public void Execute_HandsTheScriptTheRunnerGlobals()
        {
            var request = Request(
                "print(repr((__runner__, __model__, list(__models__), list(__args__), __run_dir__ == r'" + _dir + "')))\n" +
                "print('title=%s selfinit=%s' % (__title__, __selfinit__))\n" +
                "print('file=' + __file__)\n" +
                "print('dir=' + __commandpath__)\n" +
                "print('host=%s' % __pynavis__.IsRunner)",
                model: null, models: new[] { "a.nwd", "b.nwd" }, args: new[] { "--limit", "5" });

            var response = Execute(request);

            Assert.True(response.Succeeded, response.Error);
            var text = Transcript(request);
            Assert.Contains("(True, None, ['a.nwd', 'b.nwd'], ['--limit', '5'], True)", text);
            Assert.Contains("title=count_items selfinit=False", text);
            Assert.Contains("file=" + request.Script, text);
            Assert.Contains("dir=" + _dir, text);
            Assert.Contains("host=True", text);
        }

        /// <summary>
        /// Scripts guard prompts with <c>if not __runner__:</c>, which raises NameError
        /// wherever the global is missing. Every runtime path that hands a script
        /// __pynavis__ (click, Shift+Click, smartbutton init, dock pane, hook, startup.py,
        /// pynavis run) must hand it __runner__ too. Most of those paths need Navisworks or
        /// the process-wide EngineManager, so the rule is checked on the sources.
        /// </summary>
        [Fact]
        public void EveryScriptPathThatSetsPyNavis_AlsoSetsRunner()
        {
            string runtimeSrc = null;
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PyNavis.Runtime");
                if (Directory.Exists(candidate)) { runtimeSrc = candidate; break; }
            }
            Assert.NotNull(runtimeSrc);

            var setters = Directory.GetFiles(runtimeSrc, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(p => File.ReadAllText(p).Contains("Globals[\"__pynavis__\"]"))
                .ToArray();

            Assert.True(setters.Length >= 6, "Expected the six script paths, found " + setters.Length);
            Assert.Empty(setters
                .Where(p => !File.ReadAllText(p).Contains("Globals[\"__runner__\"]"))
                .Select(Path.GetFileName));
        }

        [Fact]
        public void Execute_PassesTheModelPath_WhenThereIsOne()
        {
            var model = Path.Combine(_dir, "Site.nwd");
            var request = Request("print('model=' + __model__)", model: model, models: new[] { model });

            var response = Execute(request);

            Assert.True(response.Succeeded, response.Error);
            Assert.Contains("model=" + model, Transcript(request));
        }

        [Fact]
        public void Execute_ReportsARaisingScript_WithItsTracebackInErrorAndTranscript()
        {
            var request = Request("__result__ = 1\nx = 1 / 0");

            var response = Execute(request);

            Assert.False(response.Succeeded);
            Assert.Contains("ZeroDivisionError", response.Error);
            Assert.Null(response.ResultJson);
            var text = Transcript(request);
            Assert.Contains("Traceback", text);
            Assert.Contains("ZeroDivisionError", text);
            Assert.False(RunnerContext.IsActive);
            Assert.False(RunGate.IsHeld);
        }

        [Fact]
        public void Execute_SendsToastsToTheTranscript()
        {
            var request = Request("from pynavis import toast\ntoast.info('Counted', '3 items')");

            var response = Execute(request);

            Assert.True(response.Succeeded, response.Error);
            Assert.Contains("[info] Counted: 3 items", Transcript(request));
        }

        [Fact]
        public void Execute_FailsAScriptThatAsksForAnAnswer_WithTheRunnerMessage()
        {
            var request = Request("from pynavis import forms\nforms.confirm('Delete the clashes?')\nprint('answered')");

            var response = Execute(request);

            Assert.False(response.Succeeded);
            Assert.Contains(DialogsUnavailable, response.Error);
            Assert.DoesNotContain("answered", Transcript(request));
        }

        [Fact]
        public void Execute_RefusesWhileAnotherScriptHoldsTheGate_AndDoesNotRunTheScript()
        {
            var marker = Path.Combine(_dir, "ran.txt");
            var request = Request("open(r'" + marker + "', 'w').close()");

            Assert.True(RunGate.TryEnter("Clash grouper"));
            try
            {
                var response = Execute(request);

                Assert.False(response.Succeeded);
                Assert.Equal("Another pyNavis script is still running.", response.Error);
                Assert.False(File.Exists(marker));
                // The refused run must not release a gate it never took.
                Assert.True(RunGate.IsHeld);
                Assert.Equal("Clash grouper", RunGate.CurrentTitle);
            }
            finally
            {
                RunGate.Exit();
            }
            Assert.False(RunnerContext.IsActive);
        }

        // RunRequestFile is called with the test engine and no boot: its public form takes
        // the default engine from EngineManager, which only EngineManagerInvalidationTests
        // may touch, and boots RuntimeHost, which outside Navisworks only logs a failure.
        private static void NoBoot() { }

        /// <summary>An engine that cannot run anything, like one whose install is broken.</summary>
        private sealed class ThrowingEngine : IEngine
        {
            public string Id => "throwing";
            public void Initialize(EngineConfig config) { }
            public ExecResult Execute(ScriptRequest request) => throw new InvalidOperationException("Engine is broken.");
            public void InvalidateModulesUnder(System.Collections.Generic.IList<string> roots) { }
        }

        [Fact]
        public void Execute_ReportsAnEngineThatThrows_AsAFailedRun_AndReleasesEverything()
        {
            var request = Request("x = 1");

            var response = RunExecutor.Execute(request, new ThrowingEngine(), PyNavisHost.Instance);

            Assert.False(response.Succeeded);
            Assert.Contains("Engine is broken.", response.Error);
            Assert.True(response.DurationMs >= 0);
            Assert.Contains("Engine is broken.", Transcript(request));
            Assert.False(RunGate.IsHeld);
            Assert.False(RunnerContext.IsActive);
        }

        [Fact]
        public void Execute_InsideAnotherRun_FailsNamingIt_AndLeavesThatRunInPlace()
        {
            var outer = new StringWriter();
            var marker = Path.Combine(_dir, "ran.txt");
            var request = Request("open(r'" + marker + "', 'w').close()");

            using (RunnerContext.Enter(outer))
            {
                var response = Execute(request);

                Assert.False(response.Succeeded);
                Assert.Contains("A pynavis run is already active.", response.Error);
                Assert.False(File.Exists(marker));
                Assert.True(RunnerContext.IsActive);
                Assert.Same(outer, RunnerContext.Transcript);
                Assert.False(RunGate.IsHeld);
            }
        }
        [Fact]
        public void RunRequestFile_ReturnsTwo_WhenTheRequestCannotBeRead()
        {
            Assert.Equal(2, RunExecutor.RunRequestFile(Path.Combine(_dir, "request-missing.json"), () => _engine, NoBoot));
        }

        [Fact]
        public void RunRequestFile_WritesAFailedResponse_AndReturnsOne_WhenTheScriptFails()
        {
            var request = Request("raise ValueError('no clash tests')");
            var requestPath = Path.Combine(_dir, "request-1.json");
            request.Save(requestPath);

            var code = RunExecutor.RunRequestFile(requestPath, () => _engine, NoBoot);

            Assert.Equal(1, code);
            var response = RunResponse.Load(request.ResponsePath);
            Assert.False(response.Succeeded);
            Assert.Contains("no clash tests", response.Error);
            Assert.Contains("no clash tests", Transcript(request));
            Assert.Equal(CurrentPid(), response.HostPid);
            Assert.False(RunnerContext.IsActive);
            Assert.False(RunGate.IsHeld);
        }

        private static int CurrentPid()
        {
            using (var self = System.Diagnostics.Process.GetCurrentProcess()) return self.Id;
        }

        [Fact]
        public void Execute_NamesTheHostProcess_InTheResponse()
        {
            Assert.Equal(CurrentPid(), Execute(Request("x = 1")).HostPid);
            Assert.Equal(CurrentPid(), Execute(Request("raise ValueError('x')")).HostPid);
        }

        [Fact]
        public void RunRequestFile_NamesTheHostProcess_EvenWhenTheEngineCannotStart()
        {
            var request = Request("x = 1");
            var requestPath = Path.Combine(_dir, "request-1.json");
            request.Save(requestPath);

            var code = RunExecutor.RunRequestFile(requestPath, () => throw new InvalidOperationException("No engine"), NoBoot);

            Assert.Equal(1, code);
            var response = RunResponse.Load(request.ResponsePath);
            Assert.False(response.Succeeded);
            Assert.Equal(CurrentPid(), response.HostPid);
        }

        [Fact]
        public void RunRequestFile_ReturnsZero_AndWritesTheResult_WhenTheScriptSucceeds()
        {
            var request = Request("__result__ = [1, 2]");
            var requestPath = Path.Combine(_dir, "request-1.json");
            request.Save(requestPath);

            Assert.Equal(0, RunExecutor.RunRequestFile(requestPath, () => _engine, NoBoot));
            var response = RunResponse.Load(request.ResponsePath);
            Assert.True(response.Succeeded, response.Error);
            Assert.Equal("[1, 2]", response.ResultJson);
            Assert.Equal(CurrentPid(), response.HostPid);
        }
    }
}
