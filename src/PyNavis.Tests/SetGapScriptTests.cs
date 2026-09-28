using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Set Gap half of extensions/pyNavis.extension/lib/facemove.py: the
    /// move that puts a measured face exactly a chosen gap from another,
    /// pulling tighter or pushing apart as needed, and the wording for each
    /// outcome. The clash half of the same module is in ResolveClashScriptTests.
    /// </summary>
    public class SetGapScriptTests
    {
        private readonly IronPythonEngine _engine;

        public SetGapScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Dir("lib"));
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

        // The same scene as the clash tests: a pipe-shaped box from z=0 to z=1
        // whose underside is the face the user clicks, and a beam whose top
        // face is the one it should sit a chosen distance from.
        private const string Scene =
            "pipe = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0),\n" +
            "        (0.0, 0.0, 1.0), (1.0, 0.0, 1.0), (1.0, 1.0, 1.0), (0.0, 1.0, 1.0)]\n" +
            "down = [(0.0, 0.0, -1.0)]\n" +
            "up = [(0.0, 0.0, 1.0)]\n" +
            "pipe_face = (0.5, 0.5, 0.0)\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import facemove as fm\n" + Scene + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void PlanGap_PushesApart_WhenTheGapIsSmallerThanWanted()
        {
            // The pipe's underside sits 0.1 above the beam; a 0.5 gap lifts it 0.4.
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, -0.1), up, pipe, 0.5)\n" +
                "assert r['status'] == 'moved', r\n" +
                "assert abs(r['gap'] - 0.1) < 1e-9, r\n" +
                "assert abs(r['move'] - 0.4) < 1e-9, r\n" +
                "assert r['normal'] == (0.0, 0.0, 1.0), r\n" +
                "assert all(abs(a - b) < 1e-9 for a, b in zip(r['vector'], (0, 0, 0.4))), r");
        }

        [Fact]
        public void PlanGap_PullsTighter_WhenTheGapIsLargerThanWanted()
        {
            // 0.8 above the beam, wanted 0.3: the pipe drops 0.5, towards the beam.
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, -0.8), up, pipe, 0.3)\n" +
                "assert r['status'] == 'moved', r\n" +
                "assert abs(r['gap'] - 0.8) < 1e-9, r\n" +
                "assert abs(r['move'] + 0.5) < 1e-9, r\n" +
                "assert all(abs(a - b) < 1e-9 for a, b in zip(r['vector'], (0, 0, -0.5))), r");
        }

        [Fact]
        public void PlanGap_ClearsAClash_TheSameWayClearClashWould()
        {
            // Sunk 0.3 into the beam, wanted 0.1: rise 0.4.
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "assert r['status'] == 'moved' and abs(r['move'] - 0.4) < 1e-9, r\n" +
                "assert abs(r['gap'] + 0.3) < 1e-9, r");
        }

        [Fact]
        public void PlanGap_AlreadyThere_MovesNothing()
        {
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, -0.25), up, pipe, 0.25)\n" +
                "assert r['status'] == 'same', r\n" +
                "assert r['move'] == 0.0 and r['vector'] == (0.0, 0.0, 0.0), r\n" +
                "zero = fm.plan_gap(pipe_face, down, (0.7, 0.2, 0.0), up, pipe, 0.0)\n" +
                "assert zero['status'] == 'same', zero");
        }

        [Fact]
        public void PlanGap_TravelDirection_DoesNotDependOnWhichWayTheNormalsPoint()
        {
            Run("a = fm.plan_gap(pipe_face, down, (0.7, 0.2, -0.8), up, pipe, 0.3)\n" +
                "b = fm.plan_gap(pipe_face, up, (0.7, 0.2, -0.8), down, pipe, 0.3)\n" +
                "assert a['vector'] == b['vector'], (a, b)\n" +
                "assert a['normal'] == (0.0, 0.0, 1.0)");
        }

        [Fact]
        public void PlanGap_RefusesFacesThatAreNotParallel_AndPointsWithNoFace()
        {
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, 0.3), [(1.0, 0.0, 0.0)], pipe, 0.1)\n" +
                "assert r['status'] == 'not-parallel' and abs(r['angle'] - 90) < 1e-6, r\n" +
                "assert r['move'] == 0.0 and r['normal'] is None\n" +
                "m = fm.plan_gap(pipe_face, [], (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "assert m['status'] == 'no-face' and m['missing'] == 'mover', m\n" +
                "o = fm.plan_gap(pipe_face, down, (0.7, 0.2, 0.3), [], pipe, 0.1)\n" +
                "assert o['status'] == 'no-face' and o['missing'] == 'obstacle', o");
        }

        [Fact]
        public void DescribeGap_SaysWhichWayItWent_AndWhatTheGapIsNow()
        {
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, -100.0), up, pipe, 500.0)\n" +
                "level, title, detail = fm.describe_gap(r, 'Millimeters', 'Pipe 1234', 'Beam 7')\n" +
                "assert level == 'success', level\n" +
                "assert title == 'Moved Pipe 1234 by 400 mm', title\n" +
                "assert detail == 'Away from Beam 7: the gap was 100 mm, now 500 mm. Ctrl+Z puts it back.', detail\n" +
                "r = fm.plan_gap(pipe_face, down, (0.7, 0.2, -0.8), up, pipe, 0.3)\n" +
                "level, title, detail = fm.describe_gap(r, 'Meters', 'Duct', 'Slab')\n" +
                "assert title == 'Moved Duct by 0.500 m', title\n" +
                "assert detail == 'Towards Slab: the gap was 0.800 m, now 0.300 m. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void DescribeGap_ClashSaysSo_AndSameAndRefusalsHaveTheirOwnVoice()
        {
            Run("r = fm.plan_gap(pipe_face, down, (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "level, title, detail = fm.describe_gap(r, 'Meters', 'Duct', 'Slab')\n" +
                "assert detail == 'Away from Slab: it was 0.300 m into it, now 0.100 m clear. Ctrl+Z puts it back.', detail\n" +
                "same = fm.plan_gap(pipe_face, down, (0.7, 0.2, -0.25), up, pipe, 0.25)\n" +
                "level, title, detail = fm.describe_gap(same, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'info' and title == 'Already there', (level, title)\n" +
                "assert detail == 'Duct is 0.250 m from Slab.', detail\n" +
                "skew = fm.plan_gap(pipe_face, down, (0.7, 0.2, 0.3), [(1.0, 0.0, 0.0)], pipe, 0.1)\n" +
                "level, title, detail = fm.describe_gap(skew, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'error' and title == 'The two faces are not parallel', (level, title)\n" +
                "assert detail == '90.0 deg apart. Pick a face on Duct and the face of Slab it should sit against.', detail\n" +
                "none = fm.plan_gap(pipe_face, [], (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "level, title, detail = fm.describe_gap(none, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'error' and title == 'No face under the first point', title");
        }

        [Fact]
        public void GapSettings_DefaultToNoGap_InMillimetres_UnderTheirOwnKey()
        {
            Run("assert fm.GAP_TOOL == 'set_gap', fm.GAP_TOOL\n" +
                "assert fm.GAP_DEFAULTS == {'gap': 0.0, 'gap_units': 'Millimeters'}, fm.GAP_DEFAULTS\n" +
                "assert fm.TOOL == 'resolve_clash'");
        }
    }
}
