using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Resolve Clash bundle's pure half: which axes a direction setting
    /// tries, clearance conversion between unit names, the length readout,
    /// and the toast wording for each solver outcome. Imported straight out
    /// of the shipped bundle folder; script.py only runs its click behaviour
    /// behind "if '__commandpath__' in globals()".
    /// </summary>
    public class ResolveClashScriptTests
    {
        private readonly IronPythonEngine _engine;

        public ResolveClashScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Dir(Path.Combine(
                "pyNavis.tab", "Tools.panel", "Resolve_Clash.pushbutton")));
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
                Code = "import resolveclash as rc\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void AxesFor_AutoIsEverything_ALetterIsBothWaysAlongIt()
        {
            Run("assert rc.axes_for('auto') is None\n" +
                "assert rc.axes_for('z') == ('+z', '-z')\n" +
                "assert rc.axes_for('x') == ('+x', '-x')\n" +
                "assert rc.axes_for('nonsense') is None");
        }

        [Fact]
        public void Convert_MovesAClearanceBetweenUnitNames()
        {
            Run("assert abs(rc.convert(25.4, 'Millimeters', 'Inches') - 1.0) < 1e-9\n" +
                "assert abs(rc.convert(1.0, 'Feet', 'Millimeters') - 304.8) < 1e-9\n" +
                "assert abs(rc.convert(2.0, 'Meters', 'Meters') - 2.0) < 1e-9\n" +
                "assert abs(rc.convert(3.0, 'Unknown', 'Meters') - 3.0) < 1e-9");
        }

        [Fact]
        public void FormatLength_ReadsLikeSomethingYouWouldType()
        {
            Run("assert rc.format_length(152.4, 'Millimeters') == '152.4 mm'\n" +
                "assert rc.format_length(150.0, 'Millimeters') == '150 mm'\n" +
                "assert rc.format_length(0.1524, 'Meters') == '0.152 m'\n" +
                "assert rc.format_length(0.5, 'Feet') == '0ft 6in'\n" +
                "assert rc.format_length(2.2604, 'Feet') == '2ft 3in 1/8'\n" +
                "assert rc.format_length(0.99999, 'Feet') == '1ft 0in'");
        }

        [Fact]
        public void Label_NamesTheFirstItem_AndCountsTheRest()
        {
            Run("assert rc.label(['Pipe 1234']) == 'Pipe 1234'\n" +
                "assert rc.label(['Pipe 1234', 'Pipe 1235', 'Elbow']) == 'Pipe 1234 and 2 more'\n" +
                "assert rc.label(['', None]) == 'selection'");
        }

        [Fact]
        public void Describe_ClashSaysHowFarAndWhichWay()
        {
            Run("r = {'status': 'clash', 'axis': '+z', 'move': 152.4, 'gap': 0.0, 'vector': (0, 0, 152.4)}\n" +
                "level, title, detail = rc.describe(r, 'Millimeters', 'Pipe 1234', 'Beam 7')\n" +
                "assert level == 'success', level\n" +
                "assert title == 'Moved Pipe 1234 up by 152.4 mm', title\n" +
                "assert detail == 'Clear of Beam 7. Ctrl+Z puts it back.', detail\n" +
                "r['axis'] = '-x'\n" +
                "level, title, detail = rc.describe(r, 'Millimeters', 'Pipe 1234', 'Beam 7', clearance=25.0)\n" +
                "assert title == 'Moved Pipe 1234 in -X by 152.4 mm', title\n" +
                "assert detail == 'Clear of Beam 7 with 25 mm to spare. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void Describe_TightSaysWhatTheGapWasAndIsNow()
        {
            Run("r = {'status': 'tight', 'axis': '-z', 'move': 0.3, 'gap': 0.2, 'vector': (0, 0, -0.3)}\n" +
                "level, title, detail = rc.describe(r, 'Meters', 'Duct', 'Slab', clearance=0.5)\n" +
                "assert level == 'success'\n" +
                "assert title == 'Moved Duct down by 0.300 m', title\n" +
                "assert detail == 'It was clear of Slab by 0.200 m; now by 0.500 m. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void Describe_ClearHasThreeVoices()
        {
            Run("gap = {'status': 'clear', 'axis': '+y', 'move': 0.0, 'gap': 0.25, 'vector': (0, 0, 0)}\n" +
                "level, title, detail = rc.describe(gap, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'info' and title == 'Already clear', (level, title)\n" +
                "assert detail == 'Duct is 0.250 m from Slab along Y.', detail\n" +
                "touch = dict(gap, gap=0.0)\n" +
                "assert 'touches Slab' in rc.describe(touch, 'Meters', 'Duct', 'Slab')[2]\n" +
                "never = dict(gap, gap=None)\n" +
                "assert 'never meet' in rc.describe(never, 'Meters', 'Duct', 'Slab')[2]");
        }

        [Fact]
        public void Settings_DefaultToAutoDirection_AndNoClearance()
        {
            Run("assert rc.DEFAULTS['direction'] == 'auto'\n" +
                "assert rc.DEFAULTS['clearance'] == 0.0\n" +
                "assert rc.DEFAULTS['clearance_units'] == 'Millimeters'\n" +
                "assert rc.direction_label('z') == 'Z only'\n" +
                "assert rc.direction_label('bogus') == rc.DIRECTIONS[0][1]");
        }
    }
}
