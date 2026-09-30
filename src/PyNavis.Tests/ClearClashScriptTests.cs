using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of Clear Clash, extensions/pyNavis.extension/lib/facemove.py:
    /// the move that puts a measured face exactly a chosen gap from another,
    /// pulling tighter or pushing apart as needed, clash or no clash; the
    /// wording for each outcome; the prompt; the length readout; and the
    /// remembered gap, seeded from the clearance older versions set with
    /// Shift+Click. Pipes and conduits are in CurvedFaceMoveTests.
    /// </summary>
    public class ClearClashScriptTests
    {
        private readonly IronPythonEngine _engine;

        public ClearClashScriptTests()
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
        // the user clicks, and a beam whose top face is the one it must clear.
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

        // ---- the move -------------------------------------------------------

        [Fact]
        public void Plan_ClearsAClash_IntoTheMoversBody_ByTheDepthPlusTheGap()
        {
            // The pipe's underside sits 0.3 below the beam's top: with no gap
            // it rises 0.3, into its own body and away from the face clicked;
            // with 0.1 of gap, 0.4.
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), up, pipe, 0.0)\n" +
                "assert r['status'] == 'moved', r\n" +
                "assert abs(r['gap'] + 0.3) < 1e-9 and abs(r['move'] - 0.3) < 1e-9, r\n" +
                "assert r['normal'] == (0.0, 0.0, 1.0), r\n" +
                "assert all(abs(a - b) < 1e-9 for a, b in zip(r['vector'], (0, 0, 0.3))), r\n" +
                "r = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "assert r['status'] == 'moved' and abs(r['move'] - 0.4) < 1e-9, r\n" +
                "assert r['target'] == 0.1, r");
        }

        [Fact]
        public void Plan_PushesApart_WhenTheGapIsSmallerThanWanted()
        {
            // The pipe's underside sits 0.1 above the beam; a 0.5 gap lifts it 0.4.
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, -0.1), up, pipe, 0.5)\n" +
                "assert r['status'] == 'moved', r\n" +
                "assert abs(r['gap'] - 0.1) < 1e-9 and abs(r['move'] - 0.4) < 1e-9, r\n" +
                "assert all(abs(a - b) < 1e-9 for a, b in zip(r['vector'], (0, 0, 0.4))), r");
        }

        [Fact]
        public void Plan_PullsTighter_WhenTheGapIsLargerThanWanted()
        {
            // 0.8 above the beam, wanted 0.3: the pipe drops 0.5, towards the beam.
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, -0.8), up, pipe, 0.3)\n" +
                "assert r['status'] == 'moved', r\n" +
                "assert abs(r['gap'] - 0.8) < 1e-9 and abs(r['move'] + 0.5) < 1e-9, r\n" +
                "assert all(abs(a - b) < 1e-9 for a, b in zip(r['vector'], (0, 0, -0.5))), r");
        }

        [Fact]
        public void Plan_AlreadyThere_MovesNothing()
        {
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, -0.25), up, pipe, 0.25)\n" +
                "assert r['status'] == 'same', r\n" +
                "assert r['move'] == 0.0 and r['vector'] == (0.0, 0.0, 0.0), r\n" +
                "touch = fm.plan(pipe_face, down, (0.7, 0.2, 0.0), up, pipe, 0.0)\n" +
                "assert touch['status'] == 'same' and abs(touch['gap']) < 1e-9, touch");
        }

        [Fact]
        public void Plan_TravelDirection_DoesNotDependOnWhichWayTheNormalsPoint()
        {
            // Normals from faces_at carry no reliable sign; the mover's own
            // vertices say which way is into the body.
            Run("a = fm.plan(pipe_face, down, (0.7, 0.2, -0.8), up, pipe, 0.3)\n" +
                "b = fm.plan(pipe_face, up, (0.7, 0.2, -0.8), down, pipe, 0.3)\n" +
                "assert a['vector'] == b['vector'], (a, b)\n" +
                "assert a['normal'] == (0.0, 0.0, 1.0)");
        }

        [Fact]
        public void Plan_PicksTheParallelPair_WhenAPointSitsOnAnEdge()
        {
            // The obstacle point on an edge offers a vertical face too; the pair
            // parallel to the pipe's underside is the one that counts.
            Run("r = fm.plan(pipe_face, down, (1.0, 0.2, 0.3), [(1.0, 0.0, 0.0), (0.0, 0.0, 1.0)], pipe, 0.0)\n" +
                "assert r['status'] == 'moved' and abs(r['move'] - 0.3) < 1e-9, r");
        }

        [Fact]
        public void Plan_RefusesFacesThatAreNotParallel_AndPointsWithNoFace()
        {
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), [(1.0, 0.0, 0.0)], pipe, 0.1)\n" +
                "assert r['status'] == 'not-parallel' and abs(r['angle'] - 90) < 1e-6, r\n" +
                "assert r['move'] == 0.0 and r['normal'] is None\n" +
                "m = fm.plan(pipe_face, [], (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "assert m['status'] == 'no-face' and m['missing'] == 'mover', m\n" +
                "o = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), [], pipe, 0.1)\n" +
                "assert o['status'] == 'no-face' and o['missing'] == 'obstacle', o\n" +
                "assert all(x['status'] in fm.REFUSALS for x in (r, m, o))");
        }

        [Fact]
        public void TheAwayOnlyPlanner_AndItsSecondSetOfWords_AreGone()
        {
            // One button, one code path: plan() takes the gap to leave.
            Run("for name in ('plan_gap', 'describe_gap', 'GAP_TOOL', 'GAP_DEFAULTS'):\n" +
                "    assert not hasattr(fm, name), name");
        }

        // ---- the words ------------------------------------------------------

        [Fact]
        public void Describe_SaysWhichWayItWent_AndWhatTheGapIsNow()
        {
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, -100.0), up, pipe, 500.0)\n" +
                "level, title, detail = fm.describe(r, 'Millimeters', 'Pipe 1234', 'Beam 7')\n" +
                "assert level == 'success', level\n" +
                "assert title == 'Moved Pipe 1234 by 400 mm', title\n" +
                "assert detail == 'Away from Beam 7: the gap was 100 mm, now 500 mm. Ctrl+Z puts it back.', detail\n" +
                "r = fm.plan(pipe_face, down, (0.7, 0.2, -0.8), up, pipe, 0.3)\n" +
                "level, title, detail = fm.describe(r, 'Meters', 'Duct', 'Slab')\n" +
                "assert title == 'Moved Duct by 0.500 m', title\n" +
                "assert detail == 'Towards Slab: the gap was 0.800 m, now 0.300 m. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void Describe_AClashSaysHowDeepItWas()
        {
            Run("r = fm.plan(pipe_face, down, (0.7, 0.2, 152.4), up, pipe, 0.0)\n" +
                "level, title, detail = fm.describe(r, 'Millimeters', 'Pipe 1234', 'Beam 7')\n" +
                "assert title == 'Moved Pipe 1234 by 152.4 mm', title\n" +
                "assert detail == 'Away from Beam 7: it was 152.4 mm into it, now just clear. Ctrl+Z puts it back.', detail\n" +
                "r = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "level, title, detail = fm.describe(r, 'Meters', 'Duct', 'Slab')\n" +
                "assert detail == 'Away from Slab: it was 0.300 m into it, now 0.100 m clear. Ctrl+Z puts it back.', detail");
        }

        [Fact]
        public void Describe_SameAndRefusals_HaveTheirOwnVoice()
        {
            Run("same = fm.plan(pipe_face, down, (0.7, 0.2, -0.25), up, pipe, 0.25)\n" +
                "level, title, detail = fm.describe(same, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'info' and title == 'Already there', (level, title)\n" +
                "assert detail == 'Duct is 0.250 m from Slab.', detail\n" +
                "skew = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), [(1.0, 0.0, 0.0)], pipe, 0.1)\n" +
                "level, title, detail = fm.describe(skew, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'error' and title == 'The two faces are not parallel', (level, title)\n" +
                "assert detail == '90.0 deg apart. Pick a face on Duct and the face of Slab it must clear.', detail\n" +
                "none = fm.plan(pipe_face, [], (0.7, 0.2, 0.3), up, pipe, 0.1)\n" +
                "level, title, detail = fm.describe(none, 'Meters', 'Duct', 'Slab')\n" +
                "assert level == 'error' and title == 'No face under the first point', title\n" +
                "none = fm.plan(pipe_face, down, (0.7, 0.2, 0.3), [], pipe, 0.1)\n" +
                "assert fm.describe(none, 'Meters', 'Duct', 'Slab')[1] == 'No face under the second point'");
        }

        [Fact]
        public void GapState_SaysHowDeepOrHowFar_BeforeTheNumberIsAsked()
        {
            Run("assert fm.gap_state(-3.0 / 12, 'Feet', 'Pipe 1', 'Beam 7') == 'Pipe 1 is 0ft 3in into Beam 7'\n" +
                "assert fm.gap_state(350.0, 'Millimeters', 'Tray 42', 'Duct 7') == 'Tray 42 is 350 mm from Duct 7'\n" +
                "assert fm.gap_state(0.0, 'Millimeters', 'Tray 42', 'Duct 7') == 'Tray 42 is 0 mm from Duct 7'");
        }

        [Fact]
        public void GapPrompt_ShowsWhatCanBeTyped_InTheDocumentsStyle()
        {
            Run("p = fm.gap_prompt('Tray 42 is 0ft 4in from Duct 7', 'Feet')\n" +
                "assert p == 'Tray 42 is 0ft 4in from Duct 7. Gap to leave between the two faces, ' \\\n" +
                "    'like 3\", 1\\' 6 1/2\" or 25mm:', p\n" +
                "p = fm.gap_prompt('Tray 42 is 350 mm from Duct 7', 'Millimeters')\n" +
                "assert p == 'Tray 42 is 350 mm from Duct 7. Gap to leave between the two faces, ' \\\n" +
                "    'in millimeters, or with a unit like 2\" or 0.1m:', p");
        }

        [Fact]
        public void FormatLength_ReadsLikeSomethingYouWouldType()
        {
            Run("assert fm.format_length(152.4, 'Millimeters') == '152.4 mm'\n" +
                "assert fm.format_length(150.0, 'Millimeters') == '150 mm'\n" +
                "assert fm.format_length(0.1524, 'Meters') == '0.152 m'\n" +
                "assert fm.format_length(0.5, 'Feet') == '0ft 6in'\n" +
                "assert fm.format_length(2.2604, 'Feet') == '2ft 3in 1/8'\n" +
                "assert fm.format_length(0.99999, 'Feet') == '1ft 0in'");
        }

        [Fact]
        public void Label_NamesTheFirstItem_AndCountsTheRest()
        {
            Run("assert fm.label(['Pipe 1234']) == 'Pipe 1234'\n" +
                "assert fm.label(['Pipe 1234', 'Pipe 1235', 'Elbow']) == 'Pipe 1234 and 2 more'\n" +
                "assert fm.label(['', None]) == 'selection'");
        }

        // ---- the remembered gap ---------------------------------------------

        [Fact]
        public void Settings_LiveUnderClearClashsOwnKey_AndStartWithNoGapSaved()
        {
            Run("assert fm.TOOL == 'resolve_clash', fm.TOOL\n" +
                "assert fm.DEFAULTS['gap'] is None and fm.DEFAULTS['gap_units'] == 'Millimeters', fm.DEFAULTS\n" +
                "assert fm.PARALLEL_DEGREES == 1.0");
        }

        [Fact]
        public void StartingGap_IsTheLastGapGiven_InTheDocumentsUnits()
        {
            Run("v = dict(fm.DEFAULTS, gap=25.4, gap_units='Millimeters', clearance=50.0)\n" +
                "assert abs(fm.starting_gap(v, 'Inches') - 1.0) < 1e-9, fm.starting_gap(v, 'Inches')\n" +
                "v = dict(fm.DEFAULTS, gap=0.0, gap_units='Feet', clearance=50.0)\n" +
                "assert fm.starting_gap(v, 'Millimeters') == 0.0");
        }

        [Fact]
        public void StartingGap_BeforeAnyGapIsSaved_IsTheOldClearance_ElseZero()
        {
            // Earlier versions set a fixed clearance with Shift+Click, in the
            // same settings file. Until a gap is given, it is the starting value.
            Run("v = dict(fm.DEFAULTS, clearance=1.0, clearance_units='Feet')\n" +
                "assert abs(fm.starting_gap(v, 'Millimeters') - 304.8) < 1e-9, fm.starting_gap(v, 'Millimeters')\n" +
                "assert fm.starting_gap(dict(fm.DEFAULTS), 'Feet') == 0.0\n" +
                "assert fm.starting_gap({}, 'Feet') == 0.0");
        }

        [Fact]
        public void Remember_KeepsTheGapWithItsUnit_AndDropsTheOldClearance()
        {
            Run("saved = fm.remember(0.25, 'Feet')\n" +
                "assert saved == {'gap': 0.25, 'gap_units': 'Feet'}, saved\n" +
                "back = dict(fm.DEFAULTS, **saved)\n" +
                "assert abs(fm.starting_gap(back, 'Inches') - 3.0) < 1e-9");
        }

        // ---- typed lengths --------------------------------------------------
        // Lengths as a Revit user types them into a feet-and-inches document.
        private const string Close =
            "def near(text, want, units='Feet'):\n" +
            "    got = fm.parse_length(text, units)\n" +
            "    assert got is not None and abs(got - want) < 1e-9, (text, got, want)\n";

        [Fact]
        public void Convert_MovesALengthBetweenUnitNames()
        {
            Run("assert abs(fm.convert(25.4, 'Millimeters', 'Inches') - 1.0) < 1e-9\n" +
                "assert abs(fm.convert(1.0, 'Feet', 'Millimeters') - 304.8) < 1e-9\n" +
                "assert abs(fm.convert(2.0, 'Meters', 'Meters') - 2.0) < 1e-9\n" +
                "assert abs(fm.convert(3.0, 'Unknown', 'Meters') - 3.0) < 1e-9");
        }

        [Fact]
        public void ParseLength_ReadsFeetAndFractionalInches_TheWayRevitDoes()
        {
            Run(Close +
                "near(\"1' 6\\\"\", 1.5)\n" +
                "near(\"1'6\\\"\", 1.5)\n" +
                "near(\"1'-6\\\"\", 1.5)\n" +
                "near(\"1' - 6 1/2\\\"\", 1 + 6.5 / 12)\n" +
                "near(\"6\\\"\", 0.5)\n" +
                "near(\"6 1/2\\\"\", 6.5 / 12)\n" +
                "near(\"3/4\\\"\", 0.75 / 12)\n" +
                "near(\"1'\", 1.0)\n" +
                "near(\"1.5'\", 1.5)\n" +
                "near(\"6.25\\\"\", 6.25 / 12)\n" +
                "near(\"  6 \\\" \", 0.5)\n" +
                // Revit's space rule: feet, then inches
                "near('1 6', 1.5)\n" +
                "near('1 6 1/2', 1 + 6.5 / 12)\n" +
                "near('1-6', 1.5)\n" +
                // a bare number is in the document's own unit; a bare fraction is
                // inches, as in Revit (the reader is shared: see LengthsTests)
                "near('2', 2.0)\n" +
                "near('1/2', 0.5 / 12)\n" +
                "near('0', 0.0)\n");
        }

        [Fact]
        public void ParseLength_AcceptsWordsUnitsAndItsOwnReadout()
        {
            Run(Close +
                "near('18in', 1.5)\n" +
                "near('1ft 6in', 1.5)\n" +
                "near('1 ft 6 1/4 in', 1 + 6.25 / 12)\n" +
                "near('2ft 3in 1/8', 2 + 3.125 / 12)\n" +     // format_length's own style
                // pasted from Word or an email: curly marks and primes, and '' for inches
                "near('1' + chr(0x2019) + ' 6' + chr(0x201D), 1.5)\n" +
                "near('1' + chr(0x2032) + ' 6' + chr(0x2033), 1.5)\n" +
                "near(\"6''\", 0.5)\n" +
                "near('25mm', 25 / 304.8)\n" +
                "near('2.5 cm', 0.025 / 0.3048)\n" +
                "near('0.1m', 0.1 / 0.3048)\n" +
                // a millimetre document: bare numbers are millimetres, imperial still reads
                "near('150', 150.0, 'Millimeters')\n" +
                "near('150mm', 150.0, 'Millimeters')\n" +
                "near('6\"', 152.4, 'Millimeters')\n" +
                "near(\"1' 6\\\"\", 457.2, 'Millimeters')\n" +
                "near('0.1m', 100.0, 'Millimeters')\n" +
                "near('150mm', 0.15, 'Meters')\n");
        }

        [Fact]
        public void ParseLength_RefusesWhatItCannotRead()
        {
            Run("for bad in ('', '   ', 'abc', \"1'' 2\", '-6\"', \"6\\\" 1'\", '1/0', '1 6', '6 in 2 ft', '5 mm 2'):\n" +
                "    units = 'Millimeters' if bad == '1 6' else 'Feet'\n" +
                "    assert fm.parse_length(bad, units) is None, (bad, fm.parse_length(bad, units))\n" +
                "assert fm.parse_length(None, 'Feet') is None");
        }

        [Fact]
        public void FormatInput_PrefillsTheWayRevitShowsIt_AndReadsBack()
        {
            Run("assert fm.format_input(1.5, 'Feet') == \"1' 6\\\"\", fm.format_input(1.5, 'Feet')\n" +
                "assert fm.format_input(3.25 / 12, 'Feet') == \"0' 3 1/4\\\"\", fm.format_input(3.25 / 12, 'Feet')\n" +
                "assert fm.format_input(2.0, 'Feet') == \"2' 0\\\"\"\n" +
                "assert fm.format_input(0.0, 'Feet') == \"0' 0\\\"\"\n" +
                "assert fm.format_input(6.5, 'Inches') == \"0' 6 1/2\\\"\"\n" +
                "assert fm.format_input(150.0, 'Millimeters') == '150'\n" +
                "assert fm.format_input(150.5, 'Millimeters') == '150.5'\n" +
                "assert fm.format_input(0.15, 'Meters') == '0.15'\n" +
                "for value, units in ((1.5, 'Feet'), (3.25 / 12, 'Feet'), (6.5, 'Inches'),\n" +
                "                     (150.5, 'Millimeters'), (0.15, 'Meters')):\n" +
                "    back = fm.parse_length(fm.format_input(value, units), units)\n" +
                "    assert abs(back - value) < 1e-9, (value, units, back)");
        }
    }
}
