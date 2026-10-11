using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Engine behavior tests. T1-T5 are the regression suite ported from the original
    /// smoke harness (each guarded a bug found live); the module-invalidation test is
    /// newer behavior.
    /// </summary>
    public class IronPythonEngineTests : IDisposable
    {
        private readonly IronPythonEngine _engine;
        private readonly string _modDir;

        public IronPythonEngineTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            // stdlib ships next to the test binaries via the IronPython.StdLib package
            var lib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(lib)) config.SearchPaths.Add(lib);
            _engine.Initialize(config);

            _modDir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_modDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_modDir, true); } catch { }
        }

        [Fact]
        public void Stdlib_Globals_And_OutputCapture_Work()
        {
            var outw = new StringWriter();
            var req = new ScriptRequest
            {
                Code = "import sys, os\nprint('pyver=%s' % sys.version_info[0])\nprint('osname=' + os.name)\nprint('injected=' + __probe__)",
                Output = outw,
            };
            req.Globals["__probe__"] = "42";

            var r = _engine.Execute(req);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString();
            Assert.Contains("pyver=3", text);
            Assert.Contains("osname=nt", text);
            Assert.Contains("injected=42", text);
        }

        // sys.exit() is how a script says "done, stop here" (after a cancelled dialog,
        // say). It used to come back as a failure with a SystemExit traceback in the
        // output window.
        [Fact]
        public void SysExit_EndsTheRunCleanly_NotAsAnError()
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import sys\nprint('before')\nsys.exit()\nprint('after')",
                Output = outw,
            });

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("before", outw.ToString());
            Assert.DoesNotContain("after", outw.ToString());
        }

        [Fact]
        public void SysExit_WithAMessage_IsStillAFailure_CarryingThatMessage()
        {
            var r = _engine.Execute(new ScriptRequest { Code = "import sys\nsys.exit('no model is open')" });

            Assert.False(r.Succeeded);
            Assert.Contains("no model is open", r.ErrorText);
            Assert.DoesNotContain("Traceback", r.ErrorText);
        }

        // Without __name__ set, the standard `if __name__ == '__main__':` guard is
        // False and the body of the script silently never runs.
        [Fact]
        public void Script_RunsAs_Main()
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "if __name__ == '__main__':\n    print('ran as main')",
                Output = outw,
            });

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("ran as main", outw.ToString());
        }

        [Fact]
        public void RequestSearchPath_MakesModuleImportable()
        {
            File.WriteAllText(Path.Combine(_modDir, "m_import.py"), "VALUE = 'v1'\n");
            var req = new ScriptRequest { Code = "import m_import\nassert m_import.VALUE == 'v1'" };
            req.SearchPaths.Add(_modDir);

            Assert.True(_engine.Execute(req).Succeeded);
        }

        [Fact]
        public void SearchPath_DoesNotLeak_IntoNextRun()
        {
            File.WriteAllText(Path.Combine(_modDir, "m_leak.py"), "VALUE = 1\n");
            var withPath = new ScriptRequest { Code = "pass" };
            withPath.SearchPaths.Add(_modDir);
            Assert.True(_engine.Execute(withPath).Succeeded);

            var without = new ScriptRequest { Code = "import m_leak" };
            Assert.False(_engine.Execute(without).Succeeded);
        }

        [Fact]
        public void PythonErrors_ReturnFormattedTraceback()
        {
            var r = _engine.Execute(new ScriptRequest { Code = "def f():\n    raise ValueError('boom')\nf()" });
            Assert.False(r.Succeeded);
            Assert.Contains("ValueError", r.ErrorText);
        }

        [Fact]
        public void OutputRedirection_DoesNotLeak_IntoNextRun()
        {
            var outw = new StringWriter();
            Assert.True(_engine.Execute(new ScriptRequest { Code = "print('captured')", Output = outw }).Succeeded);
            var lenBefore = outw.ToString().Length;

            Assert.True(_engine.Execute(new ScriptRequest { Code = "print('elsewhere')" }).Succeeded);
            Assert.Equal(lenBefore, outw.ToString().Length);
        }

        // A script calling script.reload_pynavis() runs Reload on its own thread, and
        // Reload runs dock pane, startup and smartbutton scripts through this same engine.
        // When that nested run ends, the outer script must get its own bundle folder on
        // sys.path and its own writers back, not the engine's baseline.
        [Fact]
        public void NestedRun_HandsTheOuterRunBackItsSearchPathAndWriters()
        {
            File.WriteAllText(Path.Combine(_modDir, "m_outer.py"), "VALUE = 'outer'\n");
            var innerDir = Path.Combine(_modDir, "inner");
            Directory.CreateDirectory(innerDir);
            var outerOut = new StringWriter();
            var outerErr = new StringWriter();
            var innerOut = new StringWriter();
            var innerErr = new StringWriter();
            ExecResult inner = null;

            var outer = new ScriptRequest
            {
                Code = "import sys\n" +
                       "__nested__()\n" +
                       "import m_outer\n" +
                       "print('outer after: ' + m_outer.VALUE)\n" +
                       "sys.stderr.write('outer error after\\n')\n",
                Output = outerOut,
                ErrorOutput = outerErr,
            };
            outer.SearchPaths.Add(_modDir);
            outer.Globals["__nested__"] = new Action(() =>
            {
                var request = new ScriptRequest
                {
                    Code = "import sys\nprint('inner ran')\nsys.stderr.write('inner error\\n')\n",
                    Output = innerOut,
                    ErrorOutput = innerErr,
                };
                request.SearchPaths.Add(innerDir);
                inner = _engine.Execute(request);
            });

            var result = _engine.Execute(outer);

            Assert.True(inner != null && inner.Succeeded, inner?.ErrorText);
            Assert.True(result.Succeeded, result.ErrorText);
            Assert.Contains("outer after: outer", outerOut.ToString());
            Assert.Contains("outer error after", outerErr.ToString());
            Assert.Contains("inner ran", innerOut.ToString());
            Assert.DoesNotContain("outer", innerOut.ToString());
            Assert.DoesNotContain("outer", innerErr.ToString());
        }

        [Fact]
        public void ModuleEdits_ArePickedUp_OnNextRun_ForSearchPathModules()
        {
            // Bundle/lib modules must reload each run so users can
            // iterate on shared code without restarting the host application.
            var mod = Path.Combine(_modDir, "m_edit.py");
            File.WriteAllText(mod, "VALUE = 'first'\n");

            var run1 = new ScriptRequest { Code = "import m_edit\n__result_value__.Add(m_edit.VALUE)" };
            run1.SearchPaths.Add(_modDir);
            var collected = new System.Collections.Generic.List<string>();
            run1.Globals["__result_value__"] = collected;
            Assert.True(_engine.Execute(run1).Succeeded, "run1 failed");

            File.WriteAllText(mod, "VALUE = 'second'\n");

            var run2 = new ScriptRequest { Code = "import m_edit\n__result_value__.Add(m_edit.VALUE)" };
            run2.SearchPaths.Add(_modDir);
            run2.Globals["__result_value__"] = collected;
            Assert.True(_engine.Execute(run2).Succeeded, "run2 failed");

            Assert.Equal(new[] { "first", "second" }, collected);
        }

        // ---- CaptureGlobals (pynavis run reads __result__ back as JSON) ------------

        /// <summary>Re-serialises JSON text with object keys sorted, so key order and
        /// spacing do not matter (IronPython's dicts do not keep insertion order).</summary>
        internal static string NormaliseJson(string json)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            return serializer.Serialize(SortKeys(serializer.DeserializeObject(json)));
        }

        private static object SortKeys(object value)
        {
            if (value is System.Collections.Generic.IDictionary<string, object> map)
            {
                var sorted = new System.Collections.Generic.SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in map) sorted[kv.Key] = SortKeys(kv.Value);
                return sorted;
            }
            if (value is object[] items)
                return Array.ConvertAll(items, SortKeys);
            return value;
        }

        private ExecResult RunCapturing(string code)
        {
            var request = new ScriptRequest { Code = code };
            request.CaptureGlobals.Add("__result__");
            return _engine.Execute(request);
        }

        [Fact]
        public void CaptureGlobals_ReturnsTheGlobalAsJson()
        {
            var r = RunCapturing("__result__ = {'count': 3, 'names': ['a']}");

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Equal(NormaliseJson("{\"count\": 3, \"names\": [\"a\"]}"),
                         NormaliseJson(r.Captured["__result__"]));
        }

        [Fact]
        public void CaptureGlobals_LeavesOutAGlobalTheScriptNeverSet()
        {
            var r = RunCapturing("x = 1");

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.False(r.Captured.ContainsKey("__result__"));
        }

        [Fact]
        public void CaptureGlobals_LeavesOutAnUnserialisableValue_AndTheRunStillSucceeds()
        {
            var r = RunCapturing("__result__ = object()");

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.False(r.Captured.ContainsKey("__result__"));
        }

        // A clean sys.exit() is a success, so a result set before it still comes back.
        [Fact]
        public void CaptureGlobals_CapturesTheResult_WhenTheScriptEndsWithACleanSysExit()
        {
            var r = RunCapturing("import sys\n__result__ = 0\nsys.exit()");

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Equal("0", r.Captured["__result__"]);
        }

        // NaN is not JSON: plain json.dumps writes it anyway and the CLI could not read it back.
        [Fact]
        public void CaptureGlobals_LeavesOutNaN()
        {
            var r = RunCapturing("__result__ = float('nan')");

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.False(r.Captured.ContainsKey("__result__"));
        }

        [Fact]
        public void CaptureGlobals_CapturesNothing_WhenTheScriptFails()
        {
            var r = RunCapturing("__result__ = 1\nraise ValueError('late')");

            Assert.False(r.Succeeded);
            Assert.Empty(r.Captured);
        }
    }
}
