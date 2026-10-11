using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// What the Clash Grouper bundle says after Ungroup: one toast for the
    /// outcome, and the output window only when there is more than one line
    /// worth reading. Imported straight out of the shipped script.py, whose
    /// click behaviour sits behind "if '__commandpath__' in globals()".
    /// </summary>
    public class ClashGrouperScriptTests
    {
        private readonly IronPythonEngine _engine;

        public ClashGrouperScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Dir(Path.Combine(
                "pyNavis.tab", "Clash.panel", "Clash_Grouper.pushbutton")));
            _engine.Initialize(config);
        }

        private static string Dir(string relative)
        {
            var candidate = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", relative));
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException("not found at " + candidate);
            return candidate;
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import script\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Ungroup_NothingRemoved_IsAnInfoToast_WithNoOutputWindow()
        {
            Run("level, message, detail, lines = script.ungroup_summary([('A vs B', 0), ('C vs D', 0)])\n" +
                "assert level == 'info', level\n" +
                "assert message == 'No groups to remove', message\n" +
                "assert 'Hand-made groups are never touched.' in detail, detail\n" +
                "assert lines == [], lines");
        }

        [Fact]
        public void Ungroup_OneTest_IsOneToast_NamingTheTest()
        {
            Run("level, message, detail, lines = script.ungroup_summary([('A vs B', 3), ('C vs D', 0)])\n" +
                "assert level == 'success', level\n" +
                "assert message == 'Removed 3 group(s)', message\n" +
                "assert detail == 'From A vs B.', detail\n" +
                "assert lines == [], lines");
        }

        [Fact]
        public void Ungroup_SeveralTests_ToastsTheTotal_AndListsEachTestInTheOutputWindow()
        {
            Run("level, message, detail, lines = script.ungroup_summary(\n" +
                "    [('A vs B', 3), ('C vs D', 0), ('E vs F', 2)])\n" +
                "assert level == 'success', level\n" +
                "assert message == 'Removed 5 group(s)', message\n" +
                "assert detail == 'From 2 tests; see the output window.', detail\n" +
                "assert lines == ['A vs B: removed 3 group(s).', 'E vs F: removed 2 group(s).'], lines");
        }
    }
}
