"""Pure half of True Distance: the pairing, the projection, the words.

Takes plain (x, y, z) tuples so it runs without Navisworks; script.py reads
the live measurement and each point's faces (pynavis.faces), then calls
resolve() here. Tested from src/PyNavis.Tests/TrueDistanceScriptTests.cs.

The true distance is measured between the two parallel faces the measured
points sit on: the measured vector projected onto those faces' normal. The
face finding and the vector maths live in pynavis.faces and are re-exported
here so the tests and older callers keep one import.
"""

from pynavis.faces import (add, angle_between, choose, cross, dot, faces_at, length,  # noqa: F401
                           normalize, scale, sub, tolerance_for)

TOOL = 'true_distance'
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


def resolve(first, end, faces_first, faces_end, parallel_degrees=1.0):
    """True distance between the faces the two points sit on.

    Returns a dict:
      status    'ok', 'not-parallel' (a face on each side but no parallel
                pair), 'one-side' (faces under one point only) or 'no-face'
      across    measured vector projected on the chosen normal, or None
      normal    the chosen unit normal, or None
      source    'pair', 'first', 'end' or None (see pynavis.faces.choose)
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
    """(title, detail) for the banner: the true distance alone, with a
    detail line only when there is a reason to doubt it."""
    if result['status'] == 'no-face':
        return ('No face found under either point',
                'Measured %s point to point. Snap both points onto surfaces and try again.'
                % format_length(result['direct'], units, denominator))
    title = 'True distance: %s' % format_length(result['across'], units, denominator)
    if result['status'] == 'not-parallel':
        return (title, 'The two faces are not parallel (%.1f deg apart); measured '
                       'square to the face under the first point.' % result['angle'])
    if result['status'] == 'one-side':
        return (title, 'Only the %s point sits on a face; measured square to that face.'
                       % result['source'])
    return (title, None)
