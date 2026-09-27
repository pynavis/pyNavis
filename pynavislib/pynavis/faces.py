"""Which faces a measured point sits on, and the pair of parallel faces two
points share.

The pure half (everything but faces_under) takes plain (x, y, z) tuples and
runs without Navisworks. A face is known by its unit normal; a point on an
edge belongs to both meeting faces, on a corner to all of them, so a point
yields a list of candidate normals and choose() picks one per point.

Which face a point sits on comes from the item's own triangles
(pynavis.geometry.world_triangles), not from a screen pick: field-checked,
a pick at a measured point on an edge returns whichever of the meeting
faces is in front, and that gave 3in 3/16 where the gap was 4in.

True Distance and Resolve Clash are the users.
"""

import math

# How many items under a point are walked before giving up on it. The
# front-most item is usually the right one; a translucent volume or a
# touching neighbour in front of it is the reason for the rest.
CANDIDATE_LIMIT = 6

# Half-size in pixels of the rectangle picked around each point.
PICK_HALF = 3

# Two faces whose normals differ by more than this many degrees (either way
# round, since facing faces have opposed normals) are not parallel.
PARALLEL_DEGREES = 1.0


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def scale(v, s):
    return (v[0] * s, v[1] * s, v[2] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def length(v):
    return math.sqrt(dot(v, v))


def normalize(v):
    n = length(v)
    if n == 0:
        return None
    return (v[0] / n, v[1] / n, v[2] / n)


def angle_between(a, b):
    """Degrees between two directions ignoring sign, so opposed normals
    (two faces looking at each other) count as parallel: 0."""
    c = abs(dot(a, b)) / (length(a) * length(b))
    return math.degrees(math.acos(max(-1.0, min(1.0, c))))


def tolerance_for(points, units_to_meters):
    """Distance within which a point counts as ON a triangle: half a
    millimetre in model units, widened for float32 coordinates far from the
    origin (COM vertices are singles, so a point 100,000 units out carries
    a hundredth of a unit of noise before any maths)."""
    biggest = 0.0
    for p in points:
        for c in p:
            biggest = max(biggest, abs(c))
    return max(0.0005 / units_to_meters, biggest * 4 * 2.0 ** -23)


def faces_at(point, triangles, tol):
    """Unit normals of the distinct faces the point lies on, from a list of
    world triangles. A point on an edge yields both faces, on a corner all
    of them; a point on none yields [].

    Degenerate (zero-area) triangles are skipped. Normals that differ only
    in sign are the same face orientation and are merged, keeping the first.
    """
    found = []
    for a, b, c in triangles:
        n = normalize(cross(sub(b, a), sub(c, a)))
        if n is None:
            continue
        if abs(dot(sub(point, a), n)) > tol:
            continue
        if not _inside(point, a, b, c, n, tol):
            continue
        if any(angle_between(n, m) < 0.01 for m in found):
            continue
        found.append(n)
    return found


def _inside(p, a, b, c, n, tol):
    """Point (already on the plane) inside the triangle, with tol of slack
    outside every edge so edges and corners belong to both neighbours."""
    for u, v in ((a, b), (b, c), (c, a)):
        edge = sub(v, u)
        out = normalize(cross(edge, n))          # points away from the triangle
        if out is None:
            return False
        if dot(sub(p, u), out) > tol:
            return False
    return True


def choose(faces_first, faces_end, diff, parallel_degrees=PARALLEL_DEGREES):
    """The normal to measure along, given each point's candidate faces.

    Returns (normal, source, angle):
      source  'pair' when a parallel pair exists (one face from each point),
              'first' or 'end' when only that point has faces, or when no
              pair is parallel (the face best aligned with the measured line
              is used and angle says how far off the nearest pair was),
              None when neither point has a face.
      angle   degrees between the two chosen faces, or None with one side.
    Among several parallel pairs (two corners sharing both orientations),
    the one the measured line follows most closely wins: a user measuring
    between two faces clicks roughly square to them.
    """
    best = None
    for n1 in faces_first:
        for n2 in faces_end:
            angle = angle_between(n1, n2)
            key = (0 if angle <= parallel_degrees else 1,
                   -abs(dot(diff, n1)) if angle <= parallel_degrees else angle)
            if best is None or key < best[0]:
                best = (key, n1, n2, angle)
    if best is not None:
        _key, n1, n2, angle = best
        if angle <= parallel_degrees:
            return n1, 'pair', angle
        return n1, 'first', angle
    if faces_first:
        return max(faces_first, key=lambda n: abs(dot(diff, n))), 'first', None
    if faces_end:
        return max(faces_end, key=lambda n: abs(dot(diff, n))), 'end', None
    return None, None, None


def side_of(vertices, point, normal):
    """The unit normal flipped, if need be, to point from the plane through
    `point` towards where most of `vertices` lie: the inward direction of a
    face on a body. Returns normal unchanged when the vertices balance."""
    total = 0.0
    for v in vertices:
        total += dot(sub(v, point), normal)
    return scale(normal, -1.0) if total < 0 else normal


# ---- API half --------------------------------------------------------------


def _as_tuple(p):
    return (float(p.X), float(p.Y), float(p.Z))


def candidates_at(view, point, log, label='point'):
    """Items under the pixel a world point projects to, front-most first,
    with the single-item pick's result leading when it exists."""
    from pynavis._api import Api
    items = []
    try:
        shot = view.ProjectPoint(point, False, False)
        log.info('%s point %s -> pixel (%d, %d)' % (label, _as_tuple(point), shot.X, shot.Y))
        hit = view.PickItemFromPoint(shot.X, shot.Y, PICK_HALF, True,
                                     getattr(Api.PickPrimitives, 'None'))
        if hit is not None and hit.ModelItem is not None:
            items.append(hit.ModelItem)
        near = view.PickItemsFromRectangle(shot.X - PICK_HALF, shot.Y - PICK_HALF,
                                           2 * PICK_HALF, 2 * PICK_HALF, False, True)
        if near is not None:
            for item in near:
                if not any(item.Equals(seen) for seen in items):
                    items.append(item)
    except Exception as error:
        log.warning('%s pick failed: %s' % (label, error))
    return items[:CANDIDATE_LIMIT]


def faces_under(view, point, tol, log, label='point'):
    """Candidate face normals for a measured point (an API Point3D): the
    first item whose triangles contain the point supplies them. Every item
    tried is logged. Returns (faces, item), item being the one that
    supplied the faces or None."""
    from pynavis import geometry
    p = _as_tuple(point)
    for item in candidates_at(view, point, log, label):
        notes = []
        triangles = geometry.world_triangles(item, note=notes.append)
        faces = faces_at(p, triangles, tol) if triangles else []
        log.info('%s item "%s" (%s): %d triangle(s), %d face(s) under the point%s'
                 % (label, item.DisplayName, item.ClassDisplayName, len(triangles),
                    len(faces), ('; ' + notes[0]) if notes else ''))
        if faces:
            for n in faces:
                log.info('%s face normal (%.4f, %.4f, %.4f)' % (label, n[0], n[1], n[2]))
            return faces, item
    return [], None
