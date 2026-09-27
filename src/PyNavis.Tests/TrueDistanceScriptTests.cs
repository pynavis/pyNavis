using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The True Distance bundle's pure half: which faces a measured point sits
    /// on (from an item's triangles), choosing the parallel pair, projecting the
    /// measurement onto their normal, and the feet-inches readout. Imported
    /// straight out of the shipped bundle folder; script.py only runs its
    /// click behaviour behind "if '__commandpath__' in globals()".
    /// </summary>
    public class TrueDistanceScriptTests
    {
        private readonly IronPythonEngine _engine;

        public TrueDistanceScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Dir(Path.Combine(
                "pyNavis.tab", "03_Clash.panel", "04_True_Distance.pushbutton")));
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

        // A unit cube from (0,0,0) to (1,1,1) as twelve triangles, the shape a
        // wall or a box arrives in from the COM primitives walk.
        private const string Cube =
            "def box(x0, y0, z0, x1, y1, z1):\n" +
            "    P = lambda x, y, z: (float(x), float(y), float(z))\n" +
            "    a, b, c, d = P(x0,y0,z0), P(x1,y0,z0), P(x1,y1,z0), P(x0,y1,z0)\n" +
            "    e, f, g, h = P(x0,y0,z1), P(x1,y0,z1), P(x1,y1,z1), P(x0,y1,z1)\n" +
            "    quads = [(a,b,c,d), (e,f,g,h), (a,b,f,e), (b,c,g,f), (c,d,h,g), (d,a,e,h)]\n" +
            "    return [(p, q, r) for p, q, r, s in quads for (p, q, r) in ((p, q, r), (p, r, s))]\n" +
            "cube = box(0, 0, 0, 1, 1, 1)\n" +
            "tol = 1e-6\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import truedistance as fd\n" + Cube + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void FacesAt_OneFaceInside_TwoOnAnEdge_ThreeOnACorner_NoneOffTheItem()
        {
            Run("one = fd.faces_at((0.5, 0.5, 1.0), cube, tol)\n" +
                "assert len(one) == 1 and abs(abs(one[0][2]) - 1) < 1e-9, one\n" +
                "edge = fd.faces_at((1.0, 0.5, 1.0), cube, tol)\n" +
                "assert len(edge) == 2, edge\n" +
                "corner = fd.faces_at((1.0, 1.0, 1.0), cube, tol)\n" +
                "assert len(corner) == 3, corner\n" +
                "assert fd.faces_at((0.5, 0.5, 0.5), cube, tol) == []\n" +      // inside the solid
                "assert fd.faces_at((0.5, 0.5, 1.01), cube, tol) == []\n" +     // 1cm above the top
                "assert fd.faces_at((1.5, 0.5, 1.0), cube, tol) == []");         // on the plane, off the face
        }

        [Fact]
        public void FacesAt_TreatsATriangleAsFlatWithinTolerance_AndSkipsDegenerateOnes()
        {
            Run("tris = cube + [((0, 0, 0), (1, 1, 1), (2, 2, 2))]\n" +          // zero area
                "assert len(fd.faces_at((0.5, 0.5, 1.0 + 5e-7), tris, tol)) == 1\n" +
                "assert fd.faces_at((0.5, 0.5, 1.0 + 5e-6), tris, tol) == []");
        }

        [Fact]
        public void Resolve_MeasuresBetweenTheParallelPair_EvenFromEdgesAndCorners()
        {
            // The user's case: a box and a wall 4in apart on a project rotated
            // in plan, both points snapped to corners so each carries every
            // meeting face. Only one face per side is parallel to one on the
            // other side; the gap is measured along that pair.
            Run("import math\n" +
                "a = math.radians(18)\n" +
                "u = (math.cos(a), math.sin(a), 0.0)\n" +
                "v = (-math.sin(a), math.cos(a), 0.0)\n" +
                "up = (0.0, 0.0, 1.0)\n" +
                "first = (10.0, 20.0, 5.0)\n" +
                "gap = 4.0 / 12\n" +
                // end sits 4in across, 3in along and 1in up from first
                "end = tuple(first[i] + gap * u[i] + 0.25 * v[i] + (1.0 / 12) * up[i] for i in range(3))\n" +
                "corner1 = [v, up, u]\n" +
                "corner2 = [(-u[0], -u[1], 0.0), (0.0, 0.0, -1.0), (-v[0], -v[1], 0.0)]\n" +
                "r = fd.resolve(first, end, corner1, corner2)\n" +
                "assert r['status'] == 'ok' and r['source'] == 'pair', r\n" +
                // the u/up/v pairs are all parallel; the line runs mostly along u
                "assert abs(r['across'] * 12 - 4.0) < 1e-9, r['across'] * 12\n" +
                "assert abs(abs(fd.dot(r['normal'], u)) - 1) < 1e-9, r\n" +
                // a wall face only on the far side: the pair is forced
                "r = fd.resolve(first, end, corner1, [(-v[0], -v[1], 0.0)])\n" +
                "assert r['status'] == 'ok' and abs(r['across'] * 12 - 3.0) < 1e-9, r");
        }

        [Fact]
        public void Resolve_ReportsNotParallel_OneSide_AndNoFace()
        {
            Run("import math\n" +
                "a = math.radians(5)\n" +
                "tilted = (math.cos(a), math.sin(a), 0.0)\n" +
                "r = fd.resolve((0, 0, 0), (10, 0, 0), [(1, 0, 0)], [tilted])\n" +
                "assert r['status'] == 'not-parallel' and abs(r['angle'] - 5) < 1e-6, r\n" +
                "assert abs(r['across'] - 10) < 1e-9 and r['source'] == 'first', r\n" +
                "title, detail = fd.describe(r, 'Feet')\n" +
                "assert title == 'True distance: 10ft 0in', title\n" +
                "assert detail.startswith('The two faces are not parallel (5.0 deg apart)'), detail\n" +
                "r = fd.resolve((0, 0, 0), (3, 4, 0), [], [(0, 1, 0)])\n" +
                "assert r['status'] == 'one-side' and r['source'] == 'end', r\n" +
                "assert abs(r['across'] - 4) < 1e-9, r\n" +
                "assert 'Only the end point' in fd.describe(r, 'Feet')[1]\n" +
                "r = fd.resolve((0, 0, 0), (3, 4, 0), [], [])\n" +
                "assert r['status'] == 'no-face' and r['across'] is None, r\n" +
                "assert fd.describe(r, 'Feet')[0] == 'No face found under either point'");
        }

        [Fact]
        public void Resolve_FieldRun_GivesFourInches_WithTheRightFace()
        {
            // Points verbatim from the pyNavis log. The
            // screen pick returned the (0.3076, -0.9515, 0) face for BOTH
            // points and read 3in 3/16; the faces the points actually sit on
            // are the perpendicular ones, and the gap along them is 4in.
            Run("first = (-1032.4821051520087, 860.31006277897359, 3180.3904882241727)\n" +
                "end = (-1032.0835314430701, 860.16074683656541, 3180.4887214251821)\n" +
                "side = (0.3076, -0.9515, 0.0)\n" +
                "face = (0.9515, 0.3076, 0.0)\n" +
                "r = fd.resolve(first, end, [side, face], [(-0.9515, -0.3076, 0.0)])\n" +
                "assert r['status'] == 'ok', r\n" +
                "assert abs(r['across'] * 12 - 4.0) < 0.01, r['across'] * 12\n" +
                "title, detail = fd.describe(r, 'Feet')\n" +
                "assert title == 'True distance: 0ft 4in' and detail is None, (title, detail)\n" +
                "start, foot = fd.dimension(first, end, r['normal'])\n" +
                "assert abs(fd.length(fd.sub(foot, first)) * 12 - 4.0) < 0.01\n" +
                "assert abs(fd.dot(fd.sub(end, foot), r['normal'])) < 1e-9");
        }

        [Fact]
        public void ToleranceFor_HalfAMillimetre_WidenedForFloat32FarFromOrigin()
        {
            Run("t = fd.tolerance_for(((1.0, 2.0, 3.0), (4.0, 5.0, 6.0)), 0.3048)\n" +
                "assert abs(t - 0.0005 / 0.3048) < 1e-12, t\n" +
                "far = fd.tolerance_for(((250000.0, 0.0, 0.0), (0.0, 0.0, 0.0)), 0.3048)\n" +
                "assert far > 0.1, far");
        }

        [Fact]
        public void FormatFeetInches_MatchesTheNativeReadout()
        {
            Run("assert fd.format_feet_inches(17 * 12 + 37 / 128.0) == '17ft 0in 37/128'\n" +
                "assert fd.format_feet_inches(11 * 12 + 1 + 165 / 256.0, 256) == '11ft 1in 165/256'\n" +
                "assert fd.format_feet_inches(16 * 12) == '16ft 0in'\n" +
                "assert fd.format_feet_inches(0.5) == '0ft 0in 1/2'\n" +
                "assert fd.format_feet_inches(12 * 12 - 0.001) == '12ft 0in', fd.format_feet_inches(12 * 12 - 0.001)\n" +
                "assert fd.format_length(16.5, 'Feet') == '16ft 6in'\n" +
                "assert fd.format_length(198, 'Inches') == '16ft 6in'\n" +
                "assert fd.format_length(2.5, 'Meters') == '2.500 m'\n" +
                "assert fd.format_length(2500, 'Millimeters') == '2500.000 mm'");
        }
    }
}
