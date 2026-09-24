"""Pure half of Face Distance: face finding, vector math, length formatting.

Takes plain (x, y, z) tuples so it runs without Navisworks; script.py reads
the live measurement and the items' triangles, then calls faces_at() and
resolve() here. Tested from src/PyNavis.Tests/FaceDistanceScriptTests.cs.

The face-to-face distance is measured between the two parallel faces the
measured points sit on: the measured vector projected onto those faces'
normal. Which face a point sits on comes from the item's own triangles, not
from a screen pick: field-checked, a pick at a measured point on
an edge returns whichever of the meeting faces is in front, and that gave
3in 3/16 where the gap was 4in. A point on an edge or corner legitimately
belongs to several faces, so each point yields a list of candidate normals
and resolve() picks the pair, one per point, that is parallel.
"""

import math

TOOL = 'face_distance'
DEFAULTS = {
    # Largest denominator a feet-inches reading is rounded to. Navisworks'
    # own measure readout uses 128ths, so that is the default.
    'denominator': 128,
    # Two faces whose normals differ by more than this many degrees (either
    # way round, since facing faces have opposed normals) are not parallel.
    'parallel_degrees': 1.0,
}

# Model units that read naturally as feet and inches, with inches per unit.
_INCHES_PER_UNIT = {'Feet': 12.0, 'Inches': 1.0, 'Yards': 36.0, 'Miles': 63360.0}

# Short suffix for a decimal reading in every other unit.
_SUFFIX = {'Meters': 'm', 'Centimeters': 'cm', 'Millimeters': 'mm',
           'Kilometers': 'km', 'Micrometers': 'um', 'Mils': 'mil',
           'Microinches': 'uin'}


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


def choose(faces_first, faces_end, diff, parallel_degrees=1.0):
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


def resolve(first, end, faces_first, faces_end, parallel_degrees=1.0):
    """Face-to-face distance between the faces the two points sit on.

    Returns a dict:
      status    'ok', 'not-parallel' (a face on each side but no parallel
                pair), 'one-side' (faces under one point only) or 'no-face'
      across    measured vector projected on the chosen normal, or None
      normal    the chosen unit normal, or None
      source    'pair', 'first', 'end' or None (see choose)
      angle     degrees between the chosen faces when both sides had one
      direct    straight-line measured distance
    """
    diff = sub(end, first)
    direct = length(diff)
    # Candidates may arrive hand-rounded (tests, logs); the projection needs
    # true unit normals or the across value and the dimension foot drift.
    faces_first = [n for n in (normalize(f) for f in faces_first) if n is not None]
    faces_end = [n for n in (normalize(f) for f in faces_end) if n is not None]
    normal, source, angle = choose(faces_first, faces_end, diff, parallel_degrees)
    if normal is None:
        status = 'no-face'
    elif source == 'pair':
        status = 'ok'
    elif angle is not None:
        status = 'not-parallel'
    else:
        status = 'one-side'
    return {'status': status,
            'across': abs(dot(diff, normal)) if normal is not None else None,
            'normal': normal, 'source': source, 'angle': angle, 'direct': direct}


def dimension(first, end, normal):
    """Where to draw the dimension: (first, foot), foot being first moved
    along the normal to the far face, so first-foot is the gap and foot-end
    is the slack the user's off-square click added."""
    t = dot(sub(end, first), normal)
    return first, add(first, scale(normal, t))


def format_length(value, units, denominator=128):
    """A length in model units as the readout Navisworks would show:
    'Nft Nin N/D' for imperial documents, a decimal with a suffix otherwise."""
    per_unit = _INCHES_PER_UNIT.get(units)
    if per_unit is None:
        return '%.3f %s' % (value, _SUFFIX.get(units, units))
    return format_feet_inches(value * per_unit, denominator)


def format_feet_inches(total_inches, denominator=128):
    """'17ft 0in 37/128' from a length in inches; the fraction is reduced and
    dropped when zero, and a rounded-up 12in rolls into the feet."""
    denominator = max(1, int(denominator))
    sign = '-' if total_inches < 0 else ''
    ticks = int(round(abs(total_inches) * denominator))
    feet, rest = divmod(ticks, 12 * denominator)
    inches, num = divmod(rest, denominator)
    text = '%s%dft %din' % (sign, feet, inches)
    if num:
        g = _gcd(num, denominator)
        text += ' %d/%d' % (num // g, denominator // g)
    return text


def _gcd(a, b):
    while b:
        a, b = b, a % b
    return a


def describe(result, units, denominator=128):
    """(title, detail) for the toast: the face-to-face value alone, with a
    detail line only when there is a reason to doubt it."""
    if result['status'] == 'no-face':
        return ('No face found under either point',
                'Measured %s point to point. Snap both points onto surfaces and try again.'
                % format_length(result['direct'], units, denominator))
    title = 'Face to face: %s' % format_length(result['across'], units, denominator)
    if result['status'] == 'not-parallel':
        return (title, 'The two faces are not parallel (%.1f deg apart); measured '
                       'square to the face under the first point.' % result['angle'])
    if result['status'] == 'one-side':
        return (title, 'Only the %s point sits on a face; measured square to that face.'
                       % result['source'])
    return (title, None)
