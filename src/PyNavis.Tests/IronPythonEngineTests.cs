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
    }
}
