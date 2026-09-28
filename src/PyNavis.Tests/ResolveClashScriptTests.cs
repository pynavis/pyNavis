using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Clear Clash half of extensions/pyNavis.extension/lib/facemove.py: the
    /// plane-to-plane move from two measured faces, clearance conversion between
    /// unit names, the length readout, and the banner wording for each outcome.
    /// The module sits in the extension's lib so Set Gap can share it; its gap
    /// planner is covered in SetGapScriptTests.
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

        // A pipe-shaped box from z=0 to z=1 whose underside (z=0) is the face
        // the user clicks, and a beam whose top face is the plane it must clear.
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
                Code = "import facemove as rc\n" + Scene + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Plan_ClashingFace_MovesIntoTheMoversBody_PastThePlane()
        {
            // The pipe's underside sits 0.3 below the beam's top: the pipe must
            // rise 0.3, into its own body and away from the face that was clicked.
            Run("r = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), up, pipe)\n" +
                "assert r['status'] == 'clash', r\n" +
                "assert abs(r['gap'] + 0.3) < 1e-9, r\n" +
                "assert abs(r['move'] - 0.3) < 1e-9, r\n" +
                "assert r['normal'] == (0.0, 0.0, 1.0), r\n" +
                "assert all(abs(a - b) < 1e-9 for a, b in zip(r['vector'], (0, 0, 0.3))), r");
        }

        [Fact]
        public void Plan_TravelDirection_DoesNotDependOnWhichWayTheNormalsPoint()
        {
            // Normals from faces_at carry no reliable sign; the mover's own
            // vertices say which way is into the body.
            Run("a = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), up, pipe)\n" +
                "b = rc.plan(pipe_face, up, (0.7, 0.2, 0.3), down, pipe)\n" +
                "assert a['vector'] == b['vector'], (a, b)\n" +
                "assert a['normal'] == (0.0, 0.0, 1.0)");
        }

        [Fact]
        public void Plan_ClearanceIsAddedAlongTheNormal_AndATightGapMovesTheDifference()
        {
            Run("r = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), up, pipe, clearance=0.1)\n" +
                "assert r['status'] == 'clash' and abs(r['move'] - 0.4) < 1e-9, r\n" +
                "tight = rc.plan(pipe_face, down, (0.7, 0.2, -0.05), up, pipe, clearance=0.1)\n" +
                "assert tight['status'] == 'tight', tight\n" +
                "assert abs(tight['gap'] - 0.05) < 1e-9 and abs(tight['move'] - 0.05) < 1e-9, tight");
        }

        [Fact]
        public void Plan_AlreadyClear_MovesNothing()
        {
            Run("r = rc.plan(pipe_face, down, (0.7, 0.2, -0.25), up, pipe)\n" +
                "assert r['status'] == 'clear', r\n" +
                "assert abs(r['gap'] - 0.25) < 1e-9 and r['move'] == 0.0, r\n" +
                "assert r['vector'] == (0.0, 0.0, 0.0), r\n" +
                "touch = rc.plan(pipe_face, down, (0.7, 0.2, 0.0), up, pipe)\n" +
                "assert touch['status'] == 'clear' and abs(touch['gap']) < 1e-9, touch");
        }

        [Fact]
        public void Plan_PicksTheParallelPair_WhenAPointSitsOnAnEdge()
        {
            // The obstacle point on an edge offers a vertical face too; the pair
            // parallel to the pipe's underside is the one that counts.
            Run("r = rc.plan(pipe_face, down, (1.0, 0.2, 0.3), [(1.0, 0.0, 0.0), (0.0, 0.0, 1.0)], pipe)\n" +
                "assert r['status'] == 'clash' and abs(r['move'] - 0.3) < 1e-9, r");
        }

        [Fact]
        public void Plan_RefusesFacesThatAreNotParallel_AndPointsWithNoFace()
        {
            Run("r = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), [(1.0, 0.0, 0.0)], pipe)\n" +
                "assert r['status'] == 'not-parallel' and abs(r['angle'] - 90) < 1e-6, r\n" +
                "assert r['move'] == 0.0 and r['normal'] is None\n" +
                "m = rc.plan(pipe_face, [], (0.7, 0.2, 0.3), up, pipe)\n" +
                "assert m['status'] == 'no-face' and m['missing'] == 'mover', m\n" +
                "o = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), [], pipe)\n" +
                "assert o['status'] == 'no-face' and o['missing'] == 'obstacle', o");
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
        public void Describe_ClashSaysHowFar_AndWhatItCleared()
        {
            Run("r = rc.plan(pipe_face, down, (0.7, 0.2, 152.4), up, pipe)\n" +
                "level, title, detail = rc.describe(r, 'Millimeters', 'Pipe 1234', 'Beam 7')\n" +
                "assert level == 'success', level\n" +
                "assert title == 'Moved Pipe 1234 by 152.4 mm', title\n" +
                "assert detail == 'Clear of Beam 7. Ctrl+Z puts it back.', detail\n" +
                "r = rc.plan(pipe_face, down, (0.7, 0.2, 152.4), up, pipe, clearance=25.0)\n" +
                "level, title, detail = rc.describe(r, 'Millimeters', 'Pipe 1234', 'Beam 7', clearance=25.0)\n" +
                "assert title == 'Moved Pipe 1234 by 177.4 mm', title\n" +
                "assert detail == 'Clear of Beam 7 with 25 mm to spare. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void Describe_TightSaysWhatTheGapWasAndIsNow()
        {
            Run("r = rc.plan(pipe_face, down, (0.7, 0.2, -0.2), up, pipe, clearance=0.5)\n" +
                "level, title, detail = rc.describe(r, 'Meters', 'Duct', 'Slab', clearance=0.5)\n" +
                "assert level == 'success'\n" +
                "assert title == 'Moved Duct by 0.300 m', title\n" +
                "assert detail == 'It was clear of Slab by 0.200 m; now by 0.500 m. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void Describe_ClearAndRefusals_EachHaveTheirOwnVoice()
        {
            Run("gap = rc.plan(pipe_face, down, (0.7, 0.2, -0.25), up, pipe)\n" +
                "level, title, detail = rc.describe(gap, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'info' and title == 'Already clear', (level, title)\n" +
                "assert detail == 'Duct is 0.250 m from Slab.', detail\n" +
                "touch = rc.plan(pipe_face, down, (0.7, 0.2, 0.0), up, pipe)\n" +
                "assert 'touches Slab' in rc.describe(touch, 'Meters', 'Duct', 'Slab')[2]\n" +
                "skew = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), [(1.0, 0.0, 0.0)], pipe)\n" +
                "level, title, detail = rc.describe(skew, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'error' and title == 'The two faces are not parallel', (level, title)\n" +
                "assert detail.startswith('90.0 deg apart.'), detail\n" +
                "none = rc.plan(pipe_face, down, (0.7, 0.2, 0.3), [], pipe)\n" +
                "level, title, detail = rc.describe(none, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'error' and title == 'No face under the second point', title");
        }

        [Fact]
        public void Settings_DefaultToNoClearance_InMillimetres()
        {
            Run("assert rc.DEFAULTS == {'clearance': 0.0, 'clearance_units': 'Millimeters'}, rc.DEFAULTS\n" +
                "assert rc.PARALLEL_DEGREES == 1.0");
        }
    }
}
