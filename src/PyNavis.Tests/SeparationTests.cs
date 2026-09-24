using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// pynavis.separation: how far one mesh must slide along an axis to
    /// stop touching another. Pure Python, run through the real IronPython
    /// engine on synthetic boxes and cylinders shaped like the meshes the
    /// COM primitives walk returns.
    /// </summary>
    public class SeparationTests
    {
        private readonly IronPythonEngine _engine;

        public SeparationTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private const string Prelude =
            "import math\n" +
            "from pynavis import separation as sp\n" +
            "def box(x0, y0, z0, x1, y1, z1):\n" +
            "    P = lambda x, y, z: (float(x), float(y), float(z))\n" +
            "    a, b, c, d = P(x0,y0,z0), P(x1,y0,z0), P(x1,y1,z0), P(x0,y1,z0)\n" +
            "    e, f, g, h = P(x0,y0,z1), P(x1,y0,z1), P(x1,y1,z1), P(x0,y1,z1)\n" +
            "    quads = [(a,b,c,d), (e,f,g,h), (a,b,f,e), (b,c,g,f), (c,d,h,g), (d,a,e,h)]\n" +
            "    return [(p, q, r) for p, q, r, s in quads for (p, q, r) in ((p, q, r), (p, r, s))]\n" +
            "def cylinder(cx, cy, z0, z1, radius, segments=24):\n" +
            "    tris, ring0, ring1 = [], [], []\n" +
            "    for i in range(segments):\n" +
            "        a = 2 * math.pi * i / segments\n" +
            "        ring0.append((cx + radius * math.cos(a), cy + radius * math.sin(a), float(z0)))\n" +
            "        ring1.append((cx + radius * math.cos(a), cy + radius * math.sin(a), float(z1)))\n" +
            "    for i in range(segments):\n" +
            "        j = (i + 1) % segments\n" +
            "        tris.append((ring0[i], ring0[j], ring1[j]))\n" +
            "        tris.append((ring0[i], ring1[j], ring1[i]))\n" +
            "        tris.append(((cx, cy, float(z0)), ring0[j], ring0[i]))\n" +
            "        tris.append(((cx, cy, float(z1)), ring1[i], ring1[j]))\n" +
            "    return tris\n" +
            "def along_x(tris):\n" +                                  // (x,y,z) -> (z,y,-x)
            "    return [tuple((z, y, -x) for x, y, z in t) for t in tris]\n" +
            "def rotate_z(tris, degrees):\n" +
            "    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))\n" +
            "    return [tuple((x * c - y * s, x * s + y * c, z) for x, y, z in t) for t in tris]\n" +
            "def close(a, b, tol=1e-6):\n" +
            "    return abs(a - b) <= tol\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = Prelude + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void TwoBoxesOverlappingInZ_ShortestMoveIsDown_AndEveryAxisIsExact()
        {
            Run("a = box(0, 0, 0, 1, 1, 1)\n" +
                "b = box(0, 0, 0.7, 1, 1, 1.7)\n" +
                "assert close(sp.along(a, b, '-z')['move'], 0.3)\n" +
                "assert close(sp.along(a, b, '+z')['move'], 1.7)\n" +
                "assert close(sp.along(a, b, '+x')['move'], 1.0)\n" +
                "assert close(sp.along(a, b, '-y')['move'], 1.0)\n" +
                "r = sp.resolve(a, b)\n" +
                "assert r['status'] == 'clash', r\n" +
                "assert r['axis'] == '-z' and close(r['move'], 0.3), r\n" +
                "assert all(close(v, w) for v, w in zip(r['vector'], (0.0, 0.0, -0.3))), r\n" +
                "assert r['candidates'][0][0] == '-z' and r['candidates'][0][2] is True, r\n" +
                "assert len(r['candidates']) == 6");
        }

        [Fact]
        public void Clearance_IsAddedAlongTheAxisMoved()
        {
            Run("a = box(0, 0, 0, 1, 1, 1)\n" +
                "b = box(0, 0, 0.7, 1, 1, 1.7)\n" +
                "r = sp.resolve(a, b, clearance=0.05)\n" +
                "assert r['status'] == 'clash' and close(r['move'], 0.35), r\n" +
                "assert close(r['vector'][2], -0.35), r");
        }

        [Fact]
        public void CrossedBars_AreFoundThroughEdgeToEdgeContacts()
        {
            // No vertex of either bar lies inside the other; only edges cross.
            Run("a = box(-3, -0.5, 0, 3, 0.5, 1)\n" +
                "b = box(-0.5, -3, 0.5, 0.5, 3, 1.5)\n" +
                "assert close(sp.along(a, b, '-z')['move'], 0.5)\n" +
                "assert close(sp.along(a, b, '+z')['move'], 1.5)\n" +
                "assert close(sp.along(a, b, '+x')['move'], 3.5)\n" +
                "assert close(sp.along(a, b, '+y')['move'], 3.5)\n" +
                "r = sp.resolve(a, b)\n" +
                "assert r['axis'] == '-z' and close(r['move'], 0.5), r");
        }

        [Fact]
        public void SeparatedBoxes_ReportTheGap_AndNeverAlongAMissingAxis()
        {
            Run("a = box(0, 0, 0, 1, 1, 1)\n" +
                "b = box(0, 0, 2, 1, 1, 3)\n" +
                "up = sp.along(a, b, '+z')\n" +
                "assert up['contact'] is False and close(up['move'], 0.0) and close(up['gap'], 1.0), up\n" +
                "down = sp.along(a, b, '-z')\n" +
                "assert down['contact'] is False and close(down['gap'], 1.0), down\n" +
                "assert sp.along(a, b, '+x')['gap'] is None\n" +
                "r = sp.resolve(a, b)\n" +
                "assert r['status'] == 'clear' and close(r['gap'], 1.0) and r['axis'] == '+z', r");
        }

        [Fact]
        public void ClearButInsideTheClearance_MovesAwayFromTheObstacle()
        {
            Run("a = box(0, 0, 0, 1, 1, 1)\n" +
                "b = box(0, 0, 1.2, 1, 1, 2.2)\n" +
                "r = sp.resolve(a, b, clearance=0.5)\n" +
                "assert r['status'] == 'tight', r\n" +
                "assert r['axis'] == '-z' and close(r['move'], 0.3), r\n" +
                "assert close(r['gap'], 0.2), r");
        }

        [Fact]
        public void TouchingBoxes_AreClearWithoutClearance_AndPushedApartWithIt()
        {
            Run("a = box(0, 0, 0, 1, 1, 1)\n" +
                "b = box(0, 0, 1, 1, 1, 2)\n" +
                "assert close(sp.along(a, b, '-z')['move'], 0.0)\n" +
                "assert close(sp.along(a, b, '-z')['gap'], 0.0)\n" +
                "assert close(sp.along(a, b, '+z')['move'], 2.0)\n" +
                "r = sp.resolve(a, b)\n" +
                "assert r['status'] == 'clear' and close(r['gap'], 0.0), r\n" +
                "r = sp.resolve(a, b, clearance=0.1)\n" +
                "assert r['status'] == 'clash' and r['axis'] == '-z' and close(r['move'], 0.1), r");
        }

        [Fact]
        public void NotchedObstacle_FindsTheGap_NotTheFarSide()
        {
            Run("floor = box(0, 0, 0, 3, 3, 1)\n" +
                "left = box(0, 0, 1, 0.5, 3, 4)\n" +
                "right = box(2.5, 0, 1, 3, 3, 4)\n" +
                "mover = box(1, 1, 0.8, 2, 2, 1.8)\n" +
                "assert close(sp.along(mover, floor + left + right, '+z')['move'], 0.2)\n" +
                "r = sp.resolve(mover, floor + left + right)\n" +
                "assert r['axis'] == '+z' and close(r['move'], 0.2), r");
        }

        [Fact]
        public void RotatedSlab_IsSolvedOnItsFaces_NotItsBoundingBox()
        {
            Run("slab = rotate_z(box(-4, -0.2, 0, 4, 0.2, 1), 45)\n" +
                "mover = box(1.2, 1.0, 0.2, 1.8, 1.6, 0.8)\n" +
                "expected = 0.4 + 0.2 * math.sqrt(2)\n" +
                "assert close(sp.along(mover, slab, '+x')['move'], expected)\n" +
                "assert close(sp.along(mover, slab, '-y')['move'], expected)\n" +
                "assert sp.along(mover, slab, '+x')['move'] < 1.0");
        }

        [Fact]
        public void ForcedAxis_TriesOnlyThatAxisBothWays()
        {
            Run("a = box(0, 0, 0, 1, 1, 1)\n" +
                "b = box(0, 0, 0.7, 1, 1, 1.7)\n" +
                "r = sp.resolve(a, b, axes=('+x', '-x'))\n" +
                "assert r['axis'] in ('+x', '-x') and close(r['move'], 1.0), r\n" +
                "assert len(r['candidates']) == 2");
        }

        [Fact]
        public void PipeThroughBeam_LiftsThePipeByTheOverlap()
        {
            Run("pipe = along_x(cylinder(0, 0, -3, 3, 0.1, segments=32))\n" +
                "beam = box(-0.5, -1, -0.3, 0.5, 1, 0.05)\n" +
                "r = sp.resolve(pipe, beam)\n" +
                "assert r['status'] == 'clash' and r['axis'] == '+z', r\n" +
                "assert close(r['move'], 0.15), r\n" +
                "assert close(sp.along(pipe, beam, '-y')['move'], 1.1)");
        }

        [Fact]
        public void DenseFitting_SolvesInSeconds_AndTheBudgetRaises()
        {
            Run("import time\n" +
                "pipe = along_x(cylinder(0, 0, -3, 3, 0.1, segments=24))\n" +
                "fitting = []\n" +
                "for i in range(12):\n" +
                "    fitting += cylinder(0.0, 0.0, -0.3 + 0.03 * i, -0.27 + 0.03 * i, 0.6, segments=64)\n" +
                "started = time.time()\n" +
                "r = sp.resolve(pipe, fitting)\n" +
                "elapsed = time.time() - started\n" +
                "assert r['status'] == 'clash' and r['axis'] == '+z', r\n" +
                "assert close(r['move'], 0.16), r\n" +
                "assert elapsed < 20.0, elapsed\n" +
                "try:\n" +
                "    sp.resolve(pipe, fitting, budget_seconds=0.0001)\n" +
                "except sp.TooDetailed:\n" +
                "    pass\n" +
                "else:\n" +
                "    raise AssertionError('expected TooDetailed')");
        }

        [Fact]
        public void EmptyMesh_Raises()
        {
            Run("try:\n" +
                "    sp.resolve([], box(0, 0, 0, 1, 1, 1))\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "else:\n" +
                "    raise AssertionError('expected ValueError')");
        }
    }
}
