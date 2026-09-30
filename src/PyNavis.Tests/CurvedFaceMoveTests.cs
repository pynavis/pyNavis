using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Clear Clash (extensions/pyNavis.extension/lib/facemove.py) with pipes
    /// and conduits: a flat face against something round, where the
    /// flat face decides the direction and the round side is measured where it
    /// actually is; and two straight pipes, measured axis to axis less their
    /// radii. The plane-to-plane cases are in ClearClashScriptTests and keep
    /// working exactly as before.
    /// </summary>
    public class CurvedFaceMoveTests
    {
        private readonly IronPythonEngine _engine;

        public CurvedFaceMoveTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", "lib")));
            _engine.Initialize(config);
        }

        // plan() with both sides' triangles, as the script calls it.
        private const string Calls =
            "import facemove as fm\n" +
            "def clash(mover, p1, other, p2, target=0.0):\n" +
            "    return fm.plan(p1, fc.faces_at(p1, mover, 1e-6), p2, fc.faces_at(p2, other, 1e-6),\n" +
            "                   verts(mover), target, mover_triangles=mover,\n" +
            "                   obstacle_triangles=other, tol=1e-6)\n" +
            // A pipe along y, 0.2 out from a wall face at x=0 with radius 0.25:
            // a ring vertex points at the wall, 0.05 into it.
            "wall = box(-0.5, -10, 0, 0, 10, 10)\n" +
            "on_wall = (0.0, 0.0, 5.0)\n" +
            "pipe, ra, rb = cylinder((0.2, -4, 5), (0.2, 4, 5), 0.25, 24, toward=(-1.0, 0.0, 0.0))\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = ShapeScene.Python + Calls + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void ClearClash_PipeSunkIntoAWall_MovesOutByTheDepth_WhicheverStripWasClicked()
        {
            // No strip of a 24-strip pipe is parallel to the wall; that used to
            // be the refusal. The wall now sets the direction and the pipe's
            // real surface the distance, so where on the pipe the click landed
            // makes no difference.
            Run("for k in (22, 23, 0, 1, 6, 12):\n" +
                "    r = clash(pipe, strip(ra, rb, k), wall, on_wall)\n" +
                "    assert r['status'] == 'moved' and r['method'] == 'surface', (k, r)\n" +
                "    assert abs(r['gap'] + 0.05) < 1e-9 and abs(r['move'] - 0.05) < 1e-9, (k, r)\n" +
                "    assert close(r['vector'], (0.05, 0.0, 0.0)), (k, r)");
        }

        [Fact]
        public void ClearClash_AddsTheGapAskedFor_ToTheSurfaceGap()
        {
            Run("p = strip(ra, rb, 0)\n" +
                "r = clash(pipe, p, wall, on_wall, 0.1)\n" +
                "assert abs(r['move'] - 0.15) < 1e-9 and close(r['vector'], (0.15, 0.0, 0.0)), r\n" +
                "g = clash(pipe, p, wall, on_wall, 0.5)\n" +
                "assert g['status'] == 'moved' and abs(g['gap'] + 0.05) < 1e-9, g\n" +
                "assert abs(g['move'] - 0.55) < 1e-9 and close(g['vector'], (0.55, 0.0, 0.0)), g");
        }

        [Fact]
        public void ClearClash_DuctResting_OnAPipe_LiftsTheFlatUndersideOffIt()
        {
            // The flat side is the mover this time: the duct's underside sets
            // the direction, the pipe's top under it the distance.
            Run("duct = box(-1, -1, 0.2, 1, 1, 0.6)\n" +
                "under, ua, ub = cylinder((0, -3, 0), (0, 3, 0), 0.25, 16, toward=(0.0, 0.0, 1.0))\n" +
                "r = clash(duct, (0.3, 0.3, 0.2), under, strip(ua, ub, 1))\n" +
                "assert r['status'] == 'moved' and r['method'] == 'surface', r\n" +
                "assert abs(r['move'] - 0.05) < 1e-9 and close(r['vector'], (0.0, 0.0, 0.05)), r");
        }

        [Fact]
        public void ClearClash_CrossingPipes_DropsTheMover_ByTheOverlapOfTheirRadii()
        {
            // Axes 0.4 apart, radii 0.25 each: 0.1 of overlap, cleared straight
            // down, away from the pipe above.
            Run("lower, la, lb = cylinder((-3, 0, 0), (3, 0, 0), 0.25, 16)\n" +
                "upper, ua, ub = cylinder((0, -3, 0.4), (0, 3, 0.4), 0.25, 16)\n" +
                "r = clash(lower, strip(la, lb, 3), upper, strip(ua, ub, 10))\n" +
                "assert r['status'] == 'moved' and r['method'] == 'pipes', r\n" +
                "assert abs(r['gap'] + 0.1) < 1e-9 and abs(r['move'] - 0.1) < 1e-9, r\n" +
                "assert close(r['vector'], (0.0, 0.0, -0.1)), r");
        }

        [Fact]
        public void ClearClash_ParallelPipes_SetsTheOutsideToOutsideSpacing()
        {
            // Centres 0.5 apart, radii 0.1 and 0.15: 0.25 of air. Asked for
            // 0.1, the mover closes in by 0.15.
            Run("a, aa, ab = cylinder((0, 0, 0), (6, 0, 0), 0.1, 16)\n" +
                "b, ba, bb = cylinder((0, 0.5, 0), (6, 0.5, 0), 0.15, 16)\n" +
                "g = clash(a, strip(aa, ab, 2), b, strip(ba, bb, 9), 0.1)\n" +
                "assert g['status'] == 'moved' and g['method'] == 'pipes', g\n" +
                "assert abs(g['gap'] - 0.25) < 1e-9 and abs(g['move'] + 0.15) < 1e-9, g\n" +
                "assert close(g['vector'], (0.0, 0.15, 0.0)), g");
        }

        [Fact]
        public void TwoFlatParallelFaces_WithTriangles_StillMeasurePlaneToPlane()
        {
            // The case that always worked must not change: box on box.
            Run("top = box(0, 0, 0.7, 1, 1, 1.7)\n" +
                "slab = box(-2, -2, -1, 3, 3, 1)\n" +
                "r = clash(top, (0.5, 0.5, 0.7), slab, (0.2, 0.2, 1.0))\n" +
                "assert r['status'] == 'moved' and r['method'] == 'planes', r\n" +
                "assert abs(r['move'] - 0.3) < 1e-9 and close(r['vector'], (0.0, 0.0, 0.3)), r");
        }

        [Fact]
        public void Refuses_TwoRoundThingsThatAreNotStraightPipes_AndAPipeNotInFrontOfTheFace()
        {
            Run("cone, ca, cb = cylinder((0, 0, 0), (5, 0, 0), 0.3, 16, r_end=0.1)\n" +
                "other, oa, ob = cylinder((2, -3, 0.35), (2, 3, 0.35), 0.3, 16, r_end=0.1)\n" +
                "r = clash(cone, strip(ca, cb, 4), other, strip(oa, ob, 12))\n" +
                "assert r['status'] == 'curved' and r['move'] == 0.0, r\n" +
                "level, title, detail = fm.describe(r, 'Meters', 'Pipe 1', 'Pipe 2')\n" +
                "assert level == 'error' and title == 'Both points are on curved surfaces', title\n" +
                "assert detail == 'Round to round works on straight pipe and conduit runs only. ' \\\n" +
                "    'Click a straight run, or a flat face on Pipe 1 or Pipe 2.', detail\n" +
                "duct = box(-1, -1, 0.2, 1, 1, 0.6)\n" +
                "away, wa, wb = cylinder((3, -3, 0), (3, 3, 0), 0.25, 16)\n" +
                "r = clash(duct, (0.3, 0.3, 0.2), away, strip(wa, wb, 1))\n" +
                "assert r['status'] == 'no-overlap' and r['flat'] == 'mover', r\n" +
                "level, title, detail = fm.describe(r, 'Meters', 'Duct 7', 'Pipe 2')\n" +
                "assert level == 'error' and title == 'The two do not face each other', title\n" +
                "assert detail == 'No part of Pipe 2 is in front of the face you clicked on Duct 7. ' \\\n" +
                "    'Pick a face of Duct 7 that Pipe 2 is in front of.', detail\n" +
                "g = clash(duct, (0.3, 0.3, 0.2), away, strip(wa, wb, 1), 0.1)\n" +
                "assert g['status'] == 'no-overlap', g\n" +
                "assert 'curved' in fm.REFUSALS and 'no-overlap' in fm.REFUSALS and 'no-face' in fm.REFUSALS");
        }
    }
}
