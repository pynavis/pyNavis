using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure fit engine (pynavis.section) through the real IronPython engine:
    /// hull, minimum-area yaw, the axis snap, padding, face derivation, and the
    /// invariant that compact() cannot change a fit. Running these at all proves
    /// the module's Navisworks imports stay lazy - there is no Navisworks here.
    /// </summary>
    public class SectionFitTests
    {
        private readonly IronPythonEngine _engine;

        // A 10 x 2 rectangle centred on the origin, rotated by `deg`, sampled
        // along its edges so a hull fit has something to bite on. z spans 0..3.
        private const string Fixture =
            "import math\n" +
            "from pynavis import section\n" +
            "def bar(deg, half_x=5.0, half_y=1.0, z0=0.0, z1=3.0, n=10):\n" +
            "    a = math.radians(deg)\n" +
            "    ca, sa = math.cos(a), math.sin(a)\n" +
            "    pts = []\n" +
            "    for i in range(n + 1):\n" +
            "        t = -half_x + 2.0 * half_x * i / float(n)\n" +
            "        for dy in (-half_y, half_y):\n" +
            "            for z in (z0, z1):\n" +
            "                pts.append((t * ca - dy * sa, t * sa + dy * ca, z))\n" +
            "    return pts\n" +
            "def close(a, b, tol=1e-6):\n" +
            "    assert abs(a - b) <= tol, (a, b)\n";

        public SectionFitTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private void Run(string code)
        {
            var r = _engine.Execute(new ScriptRequest { Code = Fixture + code });
            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ConvexHull_KeepsCorners_DropsInteriorAndCollinearPoints()
        {
            Run(
                "pts = [(0, 0), (4, 0), (4, 4), (0, 4), (2, 2), (2, 0)]\n" +
                "h = section.convex_hull(pts)\n" +
                "assert len(h) == 4, h\n" +
                "assert set(h) == set([(0, 0), (4, 0), (4, 4), (0, 4)]), h\n");
        }

        [Fact]
        public void ConvexHull_DegenerateInput_DoesNotThrow()
        {
            Run(
                "assert section.convex_hull([]) == []\n" +
                "assert section.convex_hull([(1, 1)]) == [(1, 1)]\n" +
                "assert len(section.convex_hull([(0, 0), (1, 1), (2, 2)])) <= 2\n");
        }

        [Fact]
        public void MinAreaYaw_FindsTheRotationOfATiltedBar()
        {
            Run(
                "y = section.min_area_yaw(bar(30.0))\n" +
                "close(math.degrees(y), 30.0, 0.5)\n");
        }

        [Fact]
        public void MinAreaYaw_SnapsToZero_WhenNearlyAxisAligned()
        {
            Run(
                "assert section.min_area_yaw(bar(0.0)) == 0.0\n" +
                "assert section.min_area_yaw(bar(0.8)) == 0.0\n" +
                "assert section.min_area_yaw(bar(89.4)) == 0.0\n");
        }

        [Fact]
        public void Fit_OnATiltedBar_ProducesTheTightBox_NotTheWorldAlignedOne()
        {
            // The whole point of the tool: axis-aligned would be ~9.7 x 6.7.
            Run(
                "b = section.fit(bar(30.0))\n" +
                "close(b['size'][0], 10.0, 0.01)\n" +
                "close(b['size'][1], 2.0, 0.01)\n" +
                "close(b['size'][2], 3.0, 0.01)\n");
        }

        [Fact]
        public void Fit_EmptyInput_IsNone()
        {
            Run("assert section.fit([]) is None\n");
        }

        [Fact]
        public void Fit_Padding_GrowsEveryAxisByTwiceThePadding()
        {
            Run(
                "a = section.fit(bar(30.0))\n" +
                "b = section.fit(bar(30.0), padding=0.25)\n" +
                "for i in range(3):\n" +
                "    close(b['size'][i], a['size'][i] + 0.5, 1e-9)\n");
        }

        [Fact]
        public void Faces_AreSix_OnTheirFace_WithNormalsPointingIntoTheKeptVolume()
        {
            Run(
                "b = section.fit(bar(30.0))\n" +
                "fs = section.faces(b, inward=True)\n" +
                "assert len(fs) == 6, len(fs)\n" +
                "wc = section.to_world(b, b['center'])\n" +
                "for origin, normal in fs:\n" +
                "    to_centre = [wc[i] - origin[i] for i in range(3)]\n" +
                "    dot = sum(to_centre[i] * normal[i] for i in range(3))\n" +
                "    assert dot > 0, (origin, normal, dot)\n" +
                "    close(sum(n * n for n in normal), 1.0, 1e-9)\n");
        }

        [Fact]
        public void Faces_Outward_FlipsEveryNormal()
        {
            Run(
                "b = section.fit(bar(30.0))\n" +
                "ins = section.faces(b, inward=True)\n" +
                "out = section.faces(b, inward=False)\n" +
                "for i in range(6):\n" +
                "    assert ins[i][0] == out[i][0], i\n" +
                "    for a in range(3):\n" +
                "        close(ins[i][1][a], -out[i][1][a], 1e-12)\n");
        }

        [Fact]
        public void WorldAabb_OfATiltedBox_IsBiggerThanTheBox_AndContainsIt()
        {
            Run(
                "b = section.fit(bar(30.0))\n" +
                "lo, hi = section.world_aabb(b)\n" +
                "assert (hi[0] - lo[0]) > b['size'][0] * 0.9, (lo, hi)\n" +
                "close(hi[2] - lo[2], b['size'][2], 1e-9)\n" +
                "c = section.to_world(b, b['center'])\n" +
                "for i in range(3):\n" +
                "    assert lo[i] <= c[i] <= hi[i], (i, lo, c, hi)\n");
        }

        [Fact]
        public void Compact_ThrowsAwayPoints_ButNeverChangesTheFit()
        {
            Run(
                "pts = bar(30.0, n=60)\n" +
                "small = section.compact(pts)\n" +
                "assert len(small) < len(pts), (len(small), len(pts))\n" +
                "a, b = section.fit(pts), section.fit(small)\n" +
                "close(a['yaw'], b['yaw'], 1e-9)\n" +
                "for i in range(3):\n" +
                "    close(a['size'][i], b['size'][i], 1e-9)\n" +
                "    close(a['center'][i], b['center'][i], 1e-9)\n");
        }

        [Fact]
        public void PickSource_WalksTriangles_OnlyWhileUnderBudget_AndOnlyWithCom()
        {
            Run(
                "assert section.pick_source(1000, True, budget=5000) == 'triangles'\n" +
                "assert section.pick_source(5000, True, budget=5000) == 'triangles'\n" +
                "assert section.pick_source(5001, True, budget=5000) == 'fragments'\n" +
                "assert section.pick_source(10, False, budget=5000) == 'boxes'\n" +
                "assert section.pick_source(0, True, budget=5000) == 'boxes'\n");
        }

        [Fact]
        public void Agrees_AcceptsPointsInsideTheApiBox_AndRejectsATransposedMatrix()
        {
            // The failure this guards against: a wrongly transposed local-to-world
            // matrix scatters points nowhere near the item, and a silently wrong
            // section box is worse than no section box.
            Run(
                "lo, hi = (0.0, 0.0, 0.0), (10.0, 4.0, 3.0)\n" +
                "good = [(0.1, 0.1, 0.1), (9.9, 3.9, 2.9), (5.0, 2.0, 1.5)]\n" +
                "assert section.agrees(good, lo, hi) is True\n" +
                "bad = [(0.1, 0.1, 0.1), (140.0, 3.0, 2.0)]\n" +
                "assert section.agrees(bad, lo, hi) is False\n" +
                "assert section.agrees([], lo, hi) is False\n");
        }

        [Fact]
        public void Agrees_ToleratesPointsJustOutsideTheBox_BecauseSlackIsRelative()
        {
            Run(
                "lo, hi = (0.0, 0.0, 0.0), (10.0, 4.0, 3.0)\n" +
                "assert section.agrees([(10.2, 4.1, 3.05)], lo, hi) is True\n" +
                "assert section.agrees([(12.0, 2.0, 1.0)], lo, hi) is False\n");
        }

        [Fact]
        public void Describe_ReportsSizeInMetres_AndTheAngleOnlyWhenThereIsOne()
        {
            Run(
                "b = section.fit(bar(0.0))\n" +
                "d = section.describe(b, 4, 'triangles', 1.0)\n" +
                "assert '10.0' in d and '2.0' in d and '3.0' in d, d\n" +
                "assert 'square to the world' in d, d\n");
        }

        [Fact]
        public void Describe_NamesTheAngle_WhenTheBoxIsTurned()
        {
            Run(
                "b = section.fit(bar(30.0))\n" +
                "d = section.describe(b, 4, 'triangles', 1.0)\n" +
                "assert '30' in d, d\n");
        }

        [Fact]
        public void Describe_SaysWhenTheFitCameFromABlunterSource()
        {
            Run(
                "b = section.fit(bar(30.0))\n" +
                "assert 'bounding boxes' in section.describe(b, 4, 'boxes', 1.0)\n" +
                "assert 'bounding boxes' not in section.describe(b, 4, 'triangles', 1.0)\n");
        }

        [Fact]
        public void Describe_ConvertsDocumentUnitsToMetres()
        {
            // A document in millimetres: unit_scale 0.001 turns 10 units into 0.01 m.
            Run(
                "b = section.fit(bar(0.0))\n" +
                "d = section.describe(b, 4, 'triangles', 0.001)\n" +
                "assert '0.01' in d, d\n");
        }

        // A rotation of 30 degrees about Z plus a translation of (10, 20, 30),
        // written out in both storage orders. The column form is the transpose
        // of the row form, which is exactly the confusion that scattered every
        // vertex in the field and silently dropped the fit to bounding boxes.
        private const string Matrices =
            "ca, sa = math.cos(math.radians(30)), math.sin(math.radians(30))\n" +
            "ROW = [ca, sa, 0, 0,  -sa, ca, 0, 0,  0, 0, 1, 0,  10, 20, 30, 1]\n" +
            "COL = [ca, -sa, 0, 10,  sa, ca, 0, 20,  0, 0, 1, 30,  0, 0, 0, 1]\n" +
            "def expected(x, y, z):\n" +
            "    return (x * ca - y * sa + 10, x * sa + y * ca + 20, z + 30)\n";

        [Fact]
        public void ApplyLayout_RowAndColumn_BothRecoverTheSameWorldPoint()
        {
            Run(Matrices +
                "for p in ((1.0, 2.0, 3.0), (-4.0, 0.5, 7.0)):\n" +
                "    want = expected(*p)\n" +
                "    got_row = section._apply_layout(ROW, p[0], p[1], p[2], 'row')\n" +
                "    got_col = section._apply_layout(COL, p[0], p[1], p[2], 'column')\n" +
                "    for i in range(3):\n" +
                "        close(got_row[i], want[i], 1e-9)\n" +
                "        close(got_col[i], want[i], 1e-9)\n");
        }

        [Fact]
        public void ApplyLayout_WrongLayout_MovesThePointSomewhereElse()
        {
            // If the two layouts agreed on a non-symmetric matrix there would be
            // nothing for _detect_layout to discriminate on.
            Run(Matrices +
                "a = section._apply_layout(ROW, 1.0, 2.0, 3.0, 'row')\n" +
                "b = section._apply_layout(ROW, 1.0, 2.0, 3.0, 'column')\n" +
                "assert max(abs(a[i] - b[i]) for i in range(3)) > 1.0, (a, b)\n");
        }

        [Fact]
        public void ApplyLayout_World_PassesTheVertexStraightThrough()
        {
            Run(Matrices +
                "p = section._apply_layout(ROW, 1.5, 2.5, 3.5, 'world')\n" +
                "assert p == (1.5, 2.5, 3.5), p\n");
        }

        [Fact]
        public void ApplyLayout_DividesByW_WhenTheMatrixIsProjective()
        {
            Run(
                "m = [2, 0, 0, 0,  0, 2, 0, 0,  0, 0, 2, 0,  0, 0, 0, 4]\n" +
                "p = section._apply_layout(m, 1.0, 2.0, 3.0, 'row')\n" +
                "close(p[0], 0.5); close(p[1], 1.0); close(p[2], 1.5)\n");
        }

        [Fact]
        public void Layouts_AreTheThreeTheDetectorTries()
        {
            Run("assert section._LAYOUTS == ('row', 'column', 'world')\n");
        }

        [Fact]
        public void Result_CarriesWhatTheButtonScriptToasts()
        {
            Run(
                "r = section.Result('success', 'Fitted', 'detail', 4)\n" +
                "assert (r.level, r.message, r.detail, r.count) == " +
                "       ('success', 'Fitted', 'detail', 4)\n");
        }
    }
}
