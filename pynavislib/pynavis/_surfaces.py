"""Curved surfaces for the face tools: whether the surface under a click
bends, the flat face a click sits in, how far another object reaches across
that face, and the cylinder a straight pipe or conduit makes.

The private half of pynavis.faces and pure like its pure half: plain
(x, y, z) tuples in, numbers out, no Navisworks. Clear Clash (the
extension's facemove) is the user.

Navisworks hands a pipe over as a ring of narrow flat strips, so the strip
under a click is tilted to the real surface by up to half a strip's turn
(7.5 degrees on a 24-strip pipe) and is never parallel to anything. What is
here looks past the strip: at the flat face on the other side, at the part
of the pipe actually in front of it, or at the pipe's axis and radius.
"""

import math

from pynavis.faces import add, angle_between, cross, dot, length, normalize, scale, sub

# Neighbouring triangles that turn by more than parallel but less than this
# many degrees make a curved surface: a pipe's strips meet at 360/n degrees
# (45 on an eight-sided conduit), a box's faces at 90.
BEND_DEGREES = 50.0

# Two triangles this close in angle, and in the same plane, are one face.
COPLANAR_DEGREES = 0.5

# A straight round item shows at least this many directions round its axis;
# the four corners of a rectangle sit on a circle too.
MIN_RING_DIRECTIONS = 6

# How far a vertex may stray from the fitted circle, as a share of the radius
# (the tolerance is used instead when that is larger).
ROUND_SLACK = 0.02

# How far a click may sit off the fitted surface, as a share of the radius: a
# strip lies inside the circle by r(1 - cos(180/n)), 13% on a six-sided bar.
CLICK_SLACK = 0.15

# Side walls face within this of square to the axis (sin 15 degrees); caps
# and flat ends face along it and are left out of the fit.
SIDE_WALL = 0.26


def triangle_normal(tri):
    """Unit normal of a triangle, or None when it has no area."""
    a, b, c = tri
    return normalize(cross(sub(b, a), sub(c, a)))


def face_region(triangles, point, normal, tol, degrees=COPLANAR_DEGREES):
    """The triangles lying in the plane through point with this normal
    (either sign): the flat face a click sits in, together with any other
    patch of the same plane on the item, such as a wall face either side of
    an opening."""
    return [triangles[i] for i in _region_indices(triangles, point, normal, tol, degrees)]


def _region_indices(triangles, point, normal, tol, degrees=COPLANAR_DEGREES):
    normal = normalize(normal)
    found = []
    for i, tri in enumerate(triangles):
        n = triangle_normal(tri)
        if n is None or angle_between(n, normal) > degrees:
            continue
        if all(abs(dot(sub(v, point), normal)) <= tol for v in tri):
            found.append(i)
    return found


def is_curved(triangles, point, faces, tol, parallel_degrees=1.0, bend_degrees=BEND_DEGREES):
    """Whether the surface under point bends, from the item's triangles and
    the unit normals faces_at found under the point.

    Curved when two of those faces meet at a shallow angle (a click on the
    seam between two strips), or when the flat patch under the point has a
    neighbouring triangle, sharing a vertex, that turns away from it by more
    than parallel_degrees and less than bend_degrees. A box face meets its
    neighbours at 90 degrees and is not curved; neither is its edge.
    """
    normals = [n for n in (normalize(f) for f in faces) if n is not None]
    for i, a in enumerate(normals):
        for b in normals[i + 1:]:
            if parallel_degrees < angle_between(a, b) < bend_degrees:
                return True
    for n in normals:
        region = set(_region_indices(triangles, point, n, tol))
        if not region:
            continue
        near = _Near([v for i in region for v in triangles[i]], tol)
        for i, tri in enumerate(triangles):
            if i in region:
                continue
            m = triangle_normal(tri)
            if m is None or not (parallel_degrees < angle_between(m, n) < bend_degrees):
                continue
            if any(near.has(v) for v in tri):
                return True
    return False


def extent_across(region, normal, triangles, tol):
    """(lo, hi) of x . normal over the parts of triangles that lie across
    region: inside the prism the flat face sweeps along its normal. None when
    no part of them does.

    This is how far a pipe, a fixture or anything else reaches towards or
    past a flat face, counting only what is actually in front of the face:
    a sloped pipe under a beam is measured where it passes under the beam,
    not at its high end.
    """
    n = normalize(normal)
    e1, e2 = _basis(n)

    def flat(v):
        return (dot(v, e1), dot(v, e2), dot(v, n))

    pieces = []
    for tri in region:
        a, b, c = [flat(v) for v in tri]
        area = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        if area == 0:
            continue
        if area < 0:
            b, c = c, b
        edges = []
        for u, w in ((a, b), (b, c), (c, a)):
            ex, ey = w[0] - u[0], w[1] - u[1]
            run = math.hypot(ex, ey)
            edges.append((u, -ey / run, ex / run))      # inward normal of a CCW edge
        xs, ys = (a[0], b[0], c[0]), (a[1], b[1], c[1])
        pieces.append(((min(xs) - tol, min(ys) - tol, max(xs) + tol, max(ys) + tol), edges))
    if not pieces:
        return None

    grid = _PieceGrid(pieces, tol)
    lo = hi = None
    for tri in triangles:
        points = [flat(v) for v in tri]
        xs = [p[0] for p in points]
        ys = [p[1] for p in points]
        box = (min(xs), min(ys), max(xs), max(ys))
        for box2, edges in grid.near(box):
            if box[2] < box2[0] or box[0] > box2[2] or box[3] < box2[1] or box[1] > box2[3]:
                continue
            polygon = points
            for u, nx, ny in edges:
                polygon = _clip(polygon, u, nx, ny, tol)
                if not polygon:
                    break
            for p in polygon:
                if lo is None or p[2] < lo:
                    lo = p[2]
                if hi is None or p[2] > hi:
                    hi = p[2]
    return None if lo is None else (lo, hi)


def fit_cylinder(triangles, point, tol):
    """(centre, axis, radius) of the straight round item point sits on, or
    None when the item is not one (a box, a cone, an elbow) or the point is
    not on its surface.

    The axis is the direction the side walls' normals all stand square to
    (least eigenvector of their area-weighted spread), found once, then again
    without the caps. The radius and centre come from a circle through the
    side walls' vertices seen down the axis; a tessellated pipe's vertices
    sit exactly on it. centre is on the axis, in the cross-section through
    point. Everything is worked relative to point, so a model a million
    units from the origin loses nothing to the arithmetic.
    """
    walls = []
    for tri in triangles:
        n, area = _normal_and_area(tri)
        if n is not None:
            walls.append((tri, n, area))
    if len(walls) < MIN_RING_DIRECTIONS:
        return None
    axis = _least_axis(walls)
    walls = [w for w in walls if abs(dot(w[1], axis)) < SIDE_WALL]
    if len(walls) < MIN_RING_DIRECTIONS:
        return None
    axis = _least_axis(walls)
    e1, e2 = _basis(axis)

    seen = set()
    ring = []
    for tri, _n, _area in walls:
        for v in tri:
            if v in seen:
                continue
            seen.add(v)
            d = sub(v, point)
            ring.append((dot(d, e1), dot(d, e2)))
    circle = _fit_circle(ring)
    if circle is None:
        return None
    cx, cy, r = circle
    if max(abs(math.hypot(x - cx, y - cy) - r) for x, y in ring) > max(ROUND_SLACK * r, tol):
        return None
    directions = set(int(math.degrees(math.atan2(y - cy, x - cx)) % 360.0 // 3.0) for x, y in ring)
    if len(directions) < MIN_RING_DIRECTIONS:
        return None
    if abs(math.hypot(cx, cy) - r) > max(CLICK_SLACK * r, tol):
        return None
    centre = add(point, add(scale(e1, cx), scale(e2, cy)))
    return centre, axis, r


def between_cylinders(c1, u1, r1, c2, u2, r2, click, parallel_degrees=1.0):
    """(toward, distance): the unit direction from the mover's axis
    (c1, u1) towards the obstacle's (c2, u2) along their line of closest
    approach, and the distance between the axes along it. None when the two
    are one axis.

    Crossing axes meet square to both; parallel ones are joined square to
    the pair. Axes that actually meet, two pipes crossing through each other
    at one level, have no way apart of their own, so the mover goes away from
    the side it was clicked on (click, a point on the mover's surface).
    """
    u1 = normalize(u1)
    u2 = normalize(u2)
    w = sub(c2, c1)
    touching = 1e-3 * (r1 + r2)
    k = cross(u1, u2)
    if length(k) > math.sin(math.radians(parallel_degrees)):
        n = normalize(k)
        d = dot(w, n)
        if abs(d) <= touching:
            if dot(sub(click, c1), n) < 0:
                n = scale(n, -1.0)
            return n, abs(d)
        return (n, d) if d > 0 else (scale(n, -1.0), -d)
    across = sub(w, scale(u1, dot(w, u1)))
    d = length(across)
    if d <= touching:
        return None
    return scale(across, 1.0 / d), d


# ---- helpers ------------------------------------------------------------------


def _normal_and_area(tri):
    a, b, c = tri
    c3 = cross(sub(b, a), sub(c, a))
    twice = length(c3)
    if twice == 0:
        return None, 0.0
    return scale(c3, 1.0 / twice), twice / 2.0


def _basis(n):
    """Two unit vectors square to n and to each other."""
    helper = (0.0, 1.0, 0.0) if abs(n[0]) > 0.9 else (1.0, 0.0, 0.0)
    e1 = normalize(sub(helper, scale(n, dot(helper, n))))
    return e1, cross(n, e1)


def _least_axis(walls):
    """The direction the normals spread least along: the eigenvector of the
    smallest eigenvalue of sum(area * n n^T)."""
    m = [[0.0] * 3 for _ in range(3)]
    for _tri, n, area in walls:
        for i in range(3):
            for j in range(3):
                m[i][j] += area * n[i] * n[j]
    values, vectors = _jacobi(m)
    k = min(range(3), key=lambda i: values[i])
    return normalize((vectors[0][k], vectors[1][k], vectors[2][k]))


def _jacobi(m, sweeps=50):
    """Eigenvalues and eigenvectors (as columns) of a symmetric 3x3."""
    a = [row[:] for row in m]
    v = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    scale_ = sum(abs(a[i][j]) for i in range(3) for j in range(3)) or 1.0
    for _ in range(sweeps):
        if abs(a[0][1]) + abs(a[0][2]) + abs(a[1][2]) <= 1e-15 * scale_:
            break
        for p, q in ((0, 1), (0, 2), (1, 2)):
            if a[p][q] == 0.0:
                continue
            theta = (a[q][q] - a[p][p]) / (2.0 * a[p][q])
            if abs(theta) > 1e150:
                t = 0.5 / theta
            else:
                t = 1.0 / (abs(theta) + math.sqrt(theta * theta + 1.0))
                if theta < 0:
                    t = -t
            c = 1.0 / math.sqrt(t * t + 1.0)
            s = t * c
            for k in range(3):
                akp, akq = a[k][p], a[k][q]
                a[k][p] = c * akp - s * akq
                a[k][q] = s * akp + c * akq
            for k in range(3):
                apk, aqk = a[p][k], a[q][k]
                a[p][k] = c * apk - s * aqk
                a[q][k] = s * apk + c * aqk
            for k in range(3):
                vkp, vkq = v[k][p], v[k][q]
                v[k][p] = c * vkp - s * vkq
                v[k][q] = s * vkp + c * vkq
    return [a[0][0], a[1][1], a[2][2]], v


def _fit_circle(points):
    """(cx, cy, r) of the least-squares circle through 2D points (the
    algebraic fit), or None when they do not pin one down."""
    count = len(points)
    if count < 3:
        return None
    mx = sum(p[0] for p in points) / count
    my = sum(p[1] for p in points) / count
    sxx = sxy = syy = sx = sy = bx = by = b1 = 0.0
    for px, py in points:
        x, y = px - mx, py - my
        z = x * x + y * y
        sxx += x * x
        sxy += x * y
        syy += y * y
        sx += x
        sy += y
        bx -= x * z
        by -= y * z
        b1 -= z
    solved = _solve3([[sxx, sxy, sx], [sxy, syy, sy], [sx, sy, float(count)]], [bx, by, b1])
    if solved is None:
        return None
    d, e, f = solved
    cx, cy = -d / 2.0, -e / 2.0
    r2 = cx * cx + cy * cy - f
    if r2 <= 0:
        return None
    return cx + mx, cy + my, math.sqrt(r2)


def _solve3(m, b):
    """x with m x = b by elimination with partial pivoting, or None."""
    a = [m[i][:] + [b[i]] for i in range(3)]
    size = max(abs(a[i][j]) for i in range(3) for j in range(3)) or 1.0
    for col in range(3):
        pivot = max(range(col, 3), key=lambda r: abs(a[r][col]))
        if abs(a[pivot][col]) <= 1e-14 * size:
            return None
        a[col], a[pivot] = a[pivot], a[col]
        for r in range(col + 1, 3):
            f = a[r][col] / a[col][col]
            for k in range(col, 4):
                a[r][k] -= f * a[col][k]
    x = [0.0, 0.0, 0.0]
    for r in (2, 1, 0):
        x[r] = (a[r][3] - sum(a[r][k] * x[k] for k in range(r + 1, 3))) / a[r][r]
    return x


def _clip(polygon, u, nx, ny, tol):
    """Sutherland-Hodgman: the part of polygon (points (x, y, h)) on the
    inner side of the edge through u with inward normal (nx, ny), with tol
    of slack so a pipe running exactly along a face's edge still counts."""
    out = []
    count = len(polygon)
    for i in range(count):
        p = polygon[i]
        q = polygon[(i + 1) % count]
        sp = (p[0] - u[0]) * nx + (p[1] - u[1]) * ny + tol
        sq = (q[0] - u[0]) * nx + (q[1] - u[1]) * ny + tol
        if sp >= 0:
            out.append(p)
        if (sp >= 0) != (sq >= 0):
            t = sp / (sp - sq)
            out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t,
                        p[2] + (q[2] - p[2]) * t))
    return out


class _Near(object):
    """Is a point within tol of any of a fixed set of points: a grid hash, so
    a wall face with hundreds of corners is not searched corner by corner."""

    def __init__(self, points, tol):
        self._cell = max(tol, 1e-12)
        self._tol2 = tol * tol
        self._grid = {}
        for p in points:
            self._grid.setdefault(self._key(p), []).append(p)

    def _key(self, p):
        c = self._cell
        return (int(math.floor(p[0] / c)), int(math.floor(p[1] / c)), int(math.floor(p[2] / c)))

    def has(self, p):
        kx, ky, kz = self._key(p)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for q in self._grid.get((kx + dx, ky + dy, kz + dz), ()):
                        d = sub(p, q)
                        if dot(d, d) <= self._tol2:
                            return True
        return False


class _PieceGrid(object):
    """The flat face's triangles bucketed on a 2D grid over the face, so each
    triangle of the other object is clipped only against the pieces near it."""

    DIVISIONS = 32

    def __init__(self, pieces, tol):
        self._pieces = pieces
        self._x0 = min(p[0][0] for p in pieces)
        self._y0 = min(p[0][1] for p in pieces)
        self._x1 = max(p[0][2] for p in pieces)
        self._y1 = max(p[0][3] for p in pieces)
        self._cell = max(self._x1 - self._x0, self._y1 - self._y0, tol) / self.DIVISIONS
        self._grid = {}
        for index, (box, _edges) in enumerate(pieces):
            for key in self._cells(box):
                self._grid.setdefault(key, []).append(index)

    def _cells(self, box):
        top = self.DIVISIONS
        i0 = max(0, int((box[0] - self._x0) / self._cell))
        i1 = min(top, int((box[2] - self._x0) / self._cell))
        j0 = max(0, int((box[1] - self._y0) / self._cell))
        j1 = min(top, int((box[3] - self._y0) / self._cell))
        for i in range(i0, i1 + 1):
            for j in range(j0, j1 + 1):
                yield (i, j)

    def near(self, box):
        if box[2] < self._x0 or box[0] > self._x1 or box[3] < self._y0 or box[1] > self._y1:
            return []
        seen = set()
        found = []
        for key in self._cells(box):
            for index in self._grid.get(key, ()):
                if index not in seen:
                    seen.add(index)
                    found.append(self._pieces[index])
        return found
