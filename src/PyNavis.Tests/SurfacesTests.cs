using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Python that builds tessellated test shapes the way Navisworks hands them
    /// over: a round bar as a ring of flat strips, a box as twelve triangles.
    /// Shared by the surface maths tests and the curved Clear Clash tests.
    /// </summary>
    internal static class ShapeScene
    {
        public const string Python = @"
import math
from pynavis import faces as fc

def unit(v):
    n = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (v[0] / n, v[1] / n, v[2] / n)

def cylinder(a, b, r, n, toward=None, caps=False, r_end=None):
    # A round bar from a to b as n flat strips; ring vertex 0 points along
    # toward. r_end makes it a truncated cone instead.
    u = unit(fc.sub(b, a))
    t = toward if toward is not None else ((1.0, 0.0, 0.0) if abs(u[0]) < 0.9 else (0.0, 1.0, 0.0))
    e1 = unit(fc.sub(t, fc.scale(u, fc.dot(t, u))))
    e2 = fc.cross(u, e1)
    def ring(c, radius):
        return [fc.add(c, fc.add(fc.scale(e1, radius * math.cos(2 * math.pi * k / n)),
                                 fc.scale(e2, radius * math.sin(2 * math.pi * k / n))))
                for k in range(n)]
    ra = ring(a, r)
    rb = ring(b, r if r_end is None else r_end)
    tris = []
    for k in range(n):
        j = (k + 1) % n
        tris.append((ra[k], ra[j], rb[j]))
        tris.append((ra[k], rb[j], rb[k]))
    if caps:
        for k in range(n):
            j = (k + 1) % n
            tris.append((a, ra[j], ra[k]))
            tris.append((b, rb[k], rb[j]))
    return tris, ra, rb

def box(x0, y0, z0, x1, y1, z1):
    P = lambda x, y, z: (float(x), float(y), float(z))
    a, b, c, d = P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)
    e, f, g, h = P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)
    quads = [(a, b, c, d), (e, f, g, h), (a, b, f, e), (b, c, g, f), (c, d, h, g), (d, a, e, h)]
    return [tri for p, q, s, w in quads for tri in ((p, q, s), (p, s, w))]

def mid(*ps):
    return tuple(sum(p[i] for p in ps) / len(ps) for i in range(3))

def strip(ra, rb, k):
    # The middle of strip k: a click on a pipe that lands on one flat facet.
    n = len(ra)
    return mid(ra[k], ra[(k + 1) % n], rb[k], rb[(k + 1) % n])

def verts(tris):
    return [v for t in tris for v in t]

def close(a, b, eps=1e-9):
    return all(abs(x - y) < eps for x, y in zip(a, b))
";
    }

    /// <summary>
    /// pynavis._surfaces: telling a curved surface from a flat one, the flat
    /// face a click sits in, how far another object reaches across that face,
    /// and the cylinder a straight pipe makes. Pure maths on plain tuples.
    /// </summary>
    public class SurfacesTests
    {
        private readonly IronPythonEngine _engine;

        public SurfacesTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = ShapeScene.Python + "from pynavis import _surfaces as sf\n" + code +
                       "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void IsCurved_OnAPipeStrip_EvenWhenTheClickFindsOneFlatFacet()
        {
            Run("tris, ra, rb = cylinder((0, 0, 0), (5, 0, 0), 0.25, 16)\n" +
                "p = strip(ra, rb, 3)\n" +
                "f = fc.faces_at(p, tris, 1e-6)\n" +
                "assert len(f) == 1, f\n" +
                "assert sf.is_curved(tris, p, f, 1e-6)\n" +
                // a coarse eight-sided conduit turns 45 degrees per strip
                "t8, a8, b8 = cylinder((0, 0, 0), (5, 0, 0), 0.02, 8)\n" +
                "p8 = strip(a8, b8, 0)\n" +
                "assert sf.is_curved(t8, p8, fc.faces_at(p8, t8, 1e-6), 1e-6)\n" +
                // on the seam between two strips both come back: curved at once
                "seam = mid(ra[3], rb[3])\n" +
                "two = fc.faces_at(seam, tris, 1e-6)\n" +
                "assert len(two) == 2, two\n" +
                "assert sf.is_curved(tris, seam, two, 1e-6)");
        }

        [Fact]
        public void IsCurved_NotOnABoxFace_NorOnItsEdge()
        {
            Run("cube = box(0, 0, 0, 1, 1, 1)\n" +
                "top = (0.5, 0.5, 1.0)\n" +
                "assert not sf.is_curved(cube, top, fc.faces_at(top, cube, 1e-6), 1e-6)\n" +
                "edge = (1.0, 0.5, 1.0)\n" +
                "assert len(fc.faces_at(edge, cube, 1e-6)) == 2\n" +
                "assert not sf.is_curved(cube, edge, fc.faces_at(edge, cube, 1e-6), 1e-6)");
        }

        [Fact]
        public void FaceRegion_IsThePlaneTheClickSitsIn_WhicheverWayTheNormalPoints()
        {
            Run("cube = box(0, 0, 0, 1, 1, 1)\n" +
                "region = sf.face_region(cube, (0.5, 0.5, 1.0), (0.0, 0.0, 1.0), 1e-6)\n" +
                "assert len(region) == 2, region\n" +
                "assert all(abs(v[2] - 1.0) < 1e-12 for t in region for v in t), region\n" +
                "assert len(sf.face_region(cube, (0.5, 0.5, 1.0), (0.0, 0.0, -1.0), 1e-6)) == 2");
        }

        [Fact]
        public void ExtentAcross_ReachesThePipeSurface_InFrontOfAWall()
        {
            // A pipe 0.3 out from a wall with a ring vertex pointing at it: the
            // nearest surface is 0.05 out, the far side 0.55.
            Run("wall = box(-0.5, -10, 0, 0, 10, 10)\n" +
                "face = sf.face_region(wall, (0.0, 0.0, 5.0), (1.0, 0.0, 0.0), 1e-6)\n" +
                "pipe, ra, rb = cylinder((0.3, -4, 5), (0.3, 4, 5), 0.25, 24, toward=(-1.0, 0.0, 0.0))\n" +
                "lo, hi = sf.extent_across(face, (1.0, 0.0, 0.0), pipe, 1e-6)\n" +
                "assert abs(lo - 0.05) < 1e-9 and abs(hi - 0.55) < 1e-9, (lo, hi)");
        }

        [Fact]
        public void ExtentAcross_CountsOnlyThePartOverTheFace()
        {
            // A sloped pipe passing under a beam: its top where it crosses under
            // the beam (about 2.7), not its high end out at x=10 (about 3.1).
            Run("beam = box(4, -10, 3, 6, 10, 4)\n" +
                "face = sf.face_region(beam, (5.0, 0.0, 3.0), (0.0, 0.0, 1.0), 1e-6)\n" +
                "pipe, ra, rb = cylinder((0, 0, 2.0), (10, 0, 3.0), 0.1, 16, toward=(0.0, 0.0, 1.0))\n" +
                "lo, hi = sf.extent_across(face, (0.0, 0.0, 1.0), pipe, 1e-6)\n" +
                "assert abs(hi - 2.7) < 0.01, hi\n" +
                "assert abs(lo - 2.3) < 0.01, lo\n" +
                "beside, _, _ = cylinder((0, 12, 2.5), (10, 12, 2.5), 0.1, 16)\n" +
                "assert sf.extent_across(face, (0.0, 0.0, 1.0), beside, 1e-6) is None");
        }

        [Fact]
        public void FitCylinder_FindsAxisRadiusAndCentre_FarFromTheOrigin()
        {
            // A half-inch conduit on a skew axis, 1.37 million feet out like the
            // georeferenced models in the field, clicked on one of its strips.
            Run("o = (1370200.0, 258260.0, 843.0)\n" +
                "u = unit((1.0, 2.0, 0.5))\n" +
                "b = fc.add(o, fc.scale(u, 6.0))\n" +
                "pipe, ra, rb = cylinder(o, b, 0.0417, 16, caps=True)\n" +
                "p = strip(ra, rb, 5)\n" +
                "cyl = sf.fit_cylinder(pipe, p, 1e-6)\n" +
                "assert cyl is not None\n" +
                "centre, axis, radius = cyl\n" +
                "assert abs(abs(fc.dot(axis, u)) - 1.0) < 1e-8, axis\n" +
                "assert abs(radius - 0.0417) < 1e-7, radius\n" +
                "w = fc.sub(centre, o)\n" +
                "assert fc.length(fc.sub(w, fc.scale(u, fc.dot(w, u)))) < 1e-7, centre\n" +
                "assert abs(fc.dot(fc.sub(centre, p), u)) < 1e-7, centre");
        }

        [Fact]
        public void FitCylinder_RefusesABox_ACone_AndAPointOffTheSurface()
        {
            Run("bar = box(0, 0, 0, 1, 1, 6)\n" +
                "assert sf.fit_cylinder(bar, (0.5, 0.0, 3.0), 1e-6) is None\n" +
                "cone, ca, cb = cylinder((0, 0, 0), (5, 0, 0), 0.3, 16, r_end=0.1)\n" +
                "assert sf.fit_cylinder(cone, strip(ca, cb, 2), 1e-6) is None\n" +
                "pipe, ra, rb = cylinder((0, 0, 0), (5, 0, 0), 0.25, 16, caps=True)\n" +
                "assert sf.fit_cylinder(pipe, (2.0, 0.0, 0.0), 1e-6) is None");
        }

        [Fact]
        public void BetweenCylinders_CrossingAndParallel_GiveTheLineOfClosestApproach()
        {
            // Returns the unit direction from the mover's axis towards the
            // obstacle's, and the distance between the axes along it.
            Run("toward, d = sf.between_cylinders((0, 0, 0), (1, 0, 0), 0.25,\n" +
                "                                 (0, 0, 0.3), (0, 1, 0), 0.25, (0, 0, 0.25))\n" +
                "assert close(toward, (0, 0, 1)) and abs(d - 0.3) < 1e-12, (toward, d)\n" +
                "toward, d = sf.between_cylinders((0, 0, 0), (1, 0, 0), 0.1,\n" +
                "                                 (0, 0.5, 0), (1, 0, 0), 0.15, (0, 0.1, 0))\n" +
                "assert close(toward, (0, 1, 0)) and abs(d - 0.5) < 1e-12, (toward, d)");
        }

        [Fact]
        public void BetweenCylinders_AxesThatMeet_FollowTheClick_AndCoaxialIsNone()
        {
            // Two pipes crossing at the same level have no way apart of their
            // own: the mover goes away from the side it was clicked on.
            Run("toward, d = sf.between_cylinders((0, 0, 0), (1, 0, 0), 0.25,\n" +
                "                                 (0, 0, 0), (0, 1, 0), 0.25, (0, 0, 0.25))\n" +
                "assert close(toward, (0, 0, 1)) and abs(d) < 1e-12, (toward, d)\n" +
                "toward, d = sf.between_cylinders((0, 0, 0), (1, 0, 0), 0.25,\n" +
                "                                 (0, 0, 0), (0, 1, 0), 0.25, (0, 0, -0.25))\n" +
                "assert close(toward, (0, 0, -1)), toward\n" +
                "assert sf.between_cylinders((0, 0, 0), (1, 0, 0), 0.25,\n" +
                "                            (3, 0, 0), (1, 0, 0), 0.25, (0, 0, 0.25)) is None");
        }
    }
}
