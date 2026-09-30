using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of the Get Coordinates / Go to Coordinates bundles:
    /// extensions/pyNavis.extension/lib/coords.py, imported through the real
    /// IronPython engine. Nothing in that module touches Navisworks, so
    /// importing it here runs no host code and moves no camera.
    /// </summary>
    public class CoordsTests
    {
        private readonly IronPythonEngine _engine;

        public CoordsTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(ExtensionLibDir());
            _engine.Initialize(config);
        }

        private static string ExtensionLibDir()
        {
            var candidate = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", "lib"));
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException("extension lib not found at " + candidate);
            return candidate;
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import coords\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        // ---- parse: what a pasted coordinate is allowed to look like -------

        [Fact]
        public void Parse_CommaSeparated_ReturnsThreeFloats()
        {
            Run("p = coords.parse('12.5, -3, 40')\n" +
                "assert p == (12.5, -3.0, 40.0), p");
        }

        [Fact]
        public void Parse_MixedSeparators_SplitsOnSpacesSemicolonsTabsAndNewlines()
        {
            Run("assert coords.parse('1 2 3') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('1;2;3') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('1\\t2\\t3') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('1\\n2\\n3') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('  1 ,  2 ;\\t3  ') == (1.0, 2.0, 3.0)");
        }

        [Fact]
        public void Parse_AxisLabels_AreDroppedWhicheverWayTheyAreWritten()
        {
            Run("assert coords.parse('X=1 Y=2 Z=3') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('x: 1, y: 2, z: 3') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('X = 1.5, Y = -2.5, Z = 0') == (1.5, -2.5, 0.0)");
        }

        [Fact]
        public void Parse_SurroundingBracketsAndQuotes_AreStripped()
        {
            Run("assert coords.parse('(1, 2, 3)') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('[1, 2, 3]') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('<1, 2, 3>') == (1.0, 2.0, 3.0)\n" +
                "assert coords.parse('\\\"1\\\", \\\"2\\\", \\\"3\\\"') == (1.0, 2.0, 3.0)");
        }

        [Fact]
        public void Parse_NegativesAndScientificNotation_AreRealNumbers()
        {
            Run("assert coords.parse('-1.5, +2, 3') == (-1.5, 2.0, 3.0)\n" +
                "p = coords.parse('1e3, -2.5E-2, .5')\n" +
                "assert p == (1000.0, -0.025, 0.5), p");
        }

        [Fact]
        public void Parse_ACommaIsASeparatorNeverADecimalMark()
        {
            // "1,5 2 3" is four numbers, not a European 1.5 followed by two:
            // reading it the other way would fly the camera somewhere nobody
            // typed, so it is refused instead.
            Run("assert coords.parse('1,5 2 3') is None");
        }

        [Fact]
        public void Parse_AnythingOtherThanExactlyThreeNumbers_IsRefused()
        {
            Run("assert coords.parse('1, 2') is None\n" +
                "assert coords.parse('1, 2, 3, 4') is None\n" +
                "assert coords.parse('') is None\n" +
                "assert coords.parse('   ') is None\n" +
                "assert coords.parse(None) is None");
        }

        [Fact]
        public void Parse_JunkAndNonFiniteNumbers_AreRefused()
        {
            Run("assert coords.parse('hello there friend') is None\n" +
                "assert coords.parse('1, two, 3') is None\n" +
                "assert coords.parse('Basic Wall [123456]') is None\n" +
                "assert coords.parse('nan, 2, 3') is None\n" +
                "assert coords.parse('1, inf, 3') is None");
        }

        [Fact]
        public void Parse_GivenTheDocumentsUnits_ReadsLengthsTheWayEveryLengthFieldDoes()
        {
            // Feet and inches carry spaces, so they are separated by commas; each
            // value reads through pynavis.lengths, sign and all, while plain
            // numbers keep reading exactly as before.
            Run("q = chr(34)\n" +
                "p = coords.parse(\"1' 6\" + q + \", 2' 0\" + q + \", -3' 3\" + q, 'Feet')\n" +
                "assert p == (1.5, 2.0, -3.25), p\n" +
                "p = coords.parse('1 6, 2, 25mm', 'Feet')\n" +
                "assert abs(p[0] - 1.5) < 1e-12 and p[1] == 2.0 and abs(p[2] - 25 / 304.8) < 1e-12, p\n" +
                "assert coords.parse('1500mm, 2m, -3', 'Millimeters') == (1500.0, 2000.0, -3.0)\n" +
                "assert coords.parse('1e3, -2.5E-2, .5', 'Feet') == (1000.0, -0.025, 0.5)\n" +
                // quotes that wrap a whole value are CSV quoting, not an inch mark
                "assert coords.parse(q + '1' + q + ', ' + q + '2' + q + ', ' + q + '3' + q, 'Feet') == (1.0, 2.0, 3.0)\n" +
                // without commas, feet and inches split into too many values
                "assert coords.parse(\"1' 6\" + q + \" 2' 0\" + q + \" 3'\", 'Feet') is None\n" +
                // without units, only plain numbers read, as before
                "assert coords.parse(\"1' 6\" + q + ', 2, 3') is None");
        }

        [Fact]
        public void Parse_TextLongerThanTheCap_IsRefusedUnread()
        {
            // The clipboard is read on every run, and a copied spreadsheet
            // does not deserve a scan.
            Run("big = '1, 2, 3' + ' ' * coords.MAX_TEXT_CHARS\n" +
                "assert coords.parse(big) is None");
        }

        // ---- format_point: the shape parse reads back ----------------------

        [Fact]
        public void FormatPoint_WritesThreeDecimalsAndParseReadsThemBack()
        {
            Run("text = coords.format_point((12.5, -3.0, 40.0))\n" +
                "assert text == '12.500, -3.000, 40.000', text\n" +
                "assert coords.parse(text) == (12.5, -3.0, 40.0)\n" +
                "assert coords.format_point((1, 2, 3), decimals=0) == '1, 2, 3'");
        }

        [Fact]
        public void FormatPoint_RoundTripsAnyPointItIsGiven()
        {
            Run("for point in [(0.0, 0.0, 0.0), (-1234.5678, 9.87, 0.001),\n" +
                "              (1e5, -1e5, 0.5)]:\n" +
                "    back = coords.parse(coords.format_point(point, decimals=6))\n" +
                "    assert back is not None, point\n" +
                "    for a, b in zip(point, back):\n" +
                "        assert abs(a - b) < 1e-6, (point, back)");
        }

        // ---- view_direction: the camera's own -Z turned into world axes ----

        [Fact]
        public void ViewDirection_IdentityRotation_LooksDownNegativeZ()
        {
            Run("d = coords.view_direction((0.0, 0.0, 0.0, 1.0))\n" +
                "assert max(abs(d[i] - coords.CAMERA_FORWARD[i]) for i in range(3)) < 1e-12, d");
        }

        [Fact]
        public void ViewDirection_QuarterTurnAboutX_LooksAlongPositiveY()
        {
            Run("import math\n" +
                "h = math.sqrt(0.5)\n" +                       // 90 degrees about X
                "d = coords.view_direction((h, 0.0, 0.0, h))\n" +
                "assert max(abs(d[i] - (0.0, 1.0, 0.0)[i]) for i in range(3)) < 1e-9, d\n" +
                "d = coords.view_direction((-h, 0.0, 0.0, h))\n" +   // and back the other way
                "assert max(abs(d[i] - (0.0, -1.0, 0.0)[i]) for i in range(3)) < 1e-9, d");
        }

        [Fact]
        public void ViewDirection_IsAlwaysAUnitVector_AndAZeroQuaternionIsNone()
        {
            Run("import math\n" +
                "d = coords.view_direction((2.0, 0.0, 0.0, 2.0))\n" +   // unnormalised
                "assert abs(math.sqrt(sum(v * v for v in d)) - 1.0) < 1e-12, d\n" +
                "assert coords.view_direction((0.0, 0.0, 0.0, 0.0)) is None");
        }

        // ---- camera_position: where the camera has to stand ----------------

        [Fact]
        public void CameraPosition_StandsTheDistanceBackAlongTheLineOfSight()
        {
            Run("p = coords.camera_position((10.0, 20.0, 5.0), (0.0, 0.0, -1.0), 5.0)\n" +
                "assert p == (10.0, 20.0, 10.0), p\n" +
                "p = coords.camera_position((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), 3.0)\n" +
                "assert p == (-3.0, 0.0, 0.0), p");
        }

        [Fact]
        public void CameraPosition_NormalisesTheDirectionItIsGiven()
        {
            Run("p = coords.camera_position((0.0, 0.0, 0.0), (0.0, 7.0, 0.0), 2.0)\n" +
                "assert p == (0.0, -2.0, 0.0), p\n" +
                // a diagonal direction lands the camera exactly `distance` away
                "import math\n" +
                "p = coords.camera_position((1.0, 1.0, 1.0), (1.0, 1.0, 0.0), 4.0)\n" +
                "gap = math.sqrt(sum((p[i] - 1.0) ** 2 for i in range(3)))\n" +
                "assert abs(gap - 4.0) < 1e-9, (p, gap)");
        }

        [Fact]
        public void CameraPosition_AZeroDirection_RaisesRatherThanInventingAPlace()
        {
            Run("try:\n" +
                "    coords.camera_position((1.0, 2.0, 3.0), (0.0, 0.0, 0.0), 5.0)\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "else:\n" +
                "    raise AssertionError('a zero view direction must raise')");
        }

        // ---- marker_segments: what the overlay is handed -------------------

        [Fact]
        public void MarkerSegments_AreThreeSolidAxisAlignedArmsAroundThePoint()
        {
            Run("segs = coords.marker_segments((10.0, 20.0, 30.0), 2.0)\n" +
                "assert len(segs) == 3, segs\n" +
                "assert all(len(s) == 3 and s[2] is False for s in segs), segs\n" +
                "assert segs[0] == ((8.0, 20.0, 30.0), (12.0, 20.0, 30.0), False), segs[0]\n" +
                "assert segs[1] == ((10.0, 18.0, 30.0), (10.0, 22.0, 30.0), False), segs[1]\n" +
                "assert segs[2] == ((10.0, 20.0, 28.0), (10.0, 20.0, 32.0), False), segs[2]");
        }

        [Fact]
        public void MarkerSegments_ANegativeSizeStillDrawsAnArmEachWay()
        {
            Run("segs = coords.marker_segments((0.0, 0.0, 0.0), -3.0)\n" +
                "assert segs[0] == ((-3.0, 0.0, 0.0), (3.0, 0.0, 0.0), False), segs[0]");
        }

        // ---- the constants both bundles agree on ---------------------------

        [Fact]
        public void TheSharedConstants_AreTheOnesTheBundlesUse()
        {
            Run("assert coords.MARKER_TAG == 'go-to-coordinates', coords.MARKER_TAG\n" +
                "assert coords.VIEW_DISTANCE_METERS == 5.0, coords.VIEW_DISTANCE_METERS\n" +
                "assert coords.CAMERA_FORWARD == (0.0, 0.0, -1.0), coords.CAMERA_FORWARD");
        }
    }
}
