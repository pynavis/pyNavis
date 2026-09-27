using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// CPython engine tests against the machine's real CPython (pythonnet hosts one
    /// interpreter per process - these tests share it, like scripts share it in
    /// Navisworks). Mirrors the IronPython engine contract: output capture, globals,
    /// per-run search-path isolation, formatted tracebacks.
    /// </summary>
    public class CPythonEngineTests : IDisposable
    {
        private readonly CPythonEngine _engine;
        private readonly string _modDir;

        public CPythonEngineTests()
        {
            _engine = new CPythonEngine();
            var config = new EngineConfig();
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);

            _modDir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_modDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_modDir, true); } catch { }
        }

        [Fact]
        public void RealCPython_Runs_CapturesOutput_AndInjectsGlobals()
        {
            var outw = new StringWriter();
            var req = new ScriptRequest
            {
                Code = "import sys\nprint('major=%d' % sys.version_info[0])\nprint('impl=' + sys.implementation.name)\nprint('injected=' + __probe__)",
                Output = outw,
            };
            req.Globals["__probe__"] = "42";

            var r = _engine.Execute(req);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString();
            Assert.Contains("major=3", text);
            Assert.Contains("impl=cpython", text);
            Assert.Contains("injected=42", text);
        }

        // Engine parity: the same script must end the same way under both engines
        // (see the matching IronPythonEngineTests).
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
        public void Id_IsCPython()
        {
            Assert.Equal("cpython", _engine.Id);
        }

        [Fact]
        public void PythonErrors_ReturnFormattedTraceback()
        {
            var r = _engine.Execute(new ScriptRequest { Code = "def f():\n    raise ValueError('boom')\nf()" });

            Assert.False(r.Succeeded);
            Assert.Contains("ValueError", r.ErrorText);
            Assert.Contains("boom", r.ErrorText);
        }

        [Fact]
        public void RequestSearchPath_MakesModuleImportable_AndDoesNotLeak()
        {
            File.WriteAllText(Path.Combine(_modDir, "m_cpy_import.py"), "VALUE = 'v1'\n");
            var withPath = new ScriptRequest { Code = "import m_cpy_import\nassert m_cpy_import.VALUE == 'v1'" };
            withPath.SearchPaths.Add(_modDir);
            Assert.True(_engine.Execute(withPath).Succeeded);

            var without = new ScriptRequest { Code = "import m_cpy_leak" };
            File.WriteAllText(Path.Combine(_modDir, "m_cpy_leak.py"), "VALUE = 1\n");
            Assert.False(_engine.Execute(without).Succeeded);
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

        [Fact]
        public void SharedSearchPaths_MakePynavisImportable_OnCPython()
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import pynavis\nfrom pynavis import output\nprint('v=' + pynavis.__version__)\nprint(output.format_table([['a', 1]], ['N', 'C']))",
                Output = outw,
            });

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("v=1.", outw.ToString());
        }

        [Fact]
        public void IronPythonPaths_AreNotVisible_ToCPython()
        {
            // The bundled IronPython 3.4 stdlib must never shadow CPython's own.
            var marker = Path.Combine(_modDir, "ironpython_only");
            Directory.CreateDirectory(marker);
            File.WriteAllText(Path.Combine(marker, "m_ipy_only.py"), "VALUE = 1\n");

            var engine = new CPythonEngine();
            var config = new EngineConfig();
            config.IronPythonPaths.Add(marker);
            engine.Initialize(config);

            Assert.False(engine.Execute(new ScriptRequest { Code = "import m_ipy_only" }).Succeeded);
        }

        [Fact]
        public void ModuleEdits_ArePickedUp_OnNextRun_ForSearchPathModules()
        {
            var mod = Path.Combine(_modDir, "m_cpy_edit.py");
            File.WriteAllText(mod, "VALUE = 'first'\n");

            var outw1 = new StringWriter();
            var run1 = new ScriptRequest { Code = "import m_cpy_edit\nprint(m_cpy_edit.VALUE)", Output = outw1 };
            run1.SearchPaths.Add(_modDir);
            Assert.True(_engine.Execute(run1).Succeeded, "run1 failed");

            File.WriteAllText(mod, "VALUE = 'second'\n");

            var outw2 = new StringWriter();
            var run2 = new ScriptRequest { Code = "import m_cpy_edit\nprint(m_cpy_edit.VALUE)", Output = outw2 };
            run2.SearchPaths.Add(_modDir);
            Assert.True(_engine.Execute(run2).Succeeded, "run2 failed");

            Assert.Contains("first", outw1.ToString());
            Assert.Contains("second", outw2.ToString());
        }

        [Fact]
        public async System.Threading.Tasks.Task ConcurrentExecutes_DoNotCrossTheirOutputStreams()
        {
            // sys.stdout/sys.path are interpreter-global and the GIL cycles during
            // exec, so unsynchronized concurrent runs interleave on them. time.sleep
            // forces the overlap that made the suite flaky before Execute serialized.
            var writers = new StringWriter[4];
            var tasks = new System.Threading.Tasks.Task<ExecResult>[writers.Length];
            for (var i = 0; i < writers.Length; i++)
            {
                var marker = "marker_" + i;
                var w = writers[i] = new StringWriter();
                tasks[i] = System.Threading.Tasks.Task.Run(() => _engine.Execute(new ScriptRequest
                {
                    Code = "import time\ntime.sleep(0.05)\nprint('" + marker + "')",
                    Output = w,
                }));
            }
            var results = await System.Threading.Tasks.Task.WhenAll(tasks);

            for (var i = 0; i < writers.Length; i++)
            {
                Assert.True(results[i].Succeeded, results[i].ErrorText);
                Assert.Equal("marker_" + i, writers[i].ToString().Trim());
            }
        }

        [Fact]
        public void Numpy_Imports_AndComputes()
        {
            // THE CPython acceptance test: the full PyPI ecosystem inside the host.
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import numpy as np\nprint('numpy=' + np.__version__)\nprint('sum=%d' % int(np.arange(10).sum()))",
                Output = outw,
            });

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("numpy=", outw.ToString());
            Assert.Contains("sum=45", outw.ToString());
        }
    }
}
