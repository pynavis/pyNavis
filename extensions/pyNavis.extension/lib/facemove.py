"""Pure half of Clear Clash: the face-to-face move, the remembered gap,
unit conversion, and the words the banner, prompt and dialog use. No
Navisworks import, so this is unit-testable anywhere. It lives in the
extension's lib, beside the other pure modules the tests import.

The user measures two faces: one on the object that moves, one on the other
object. Two flat faces are used as they are: the move is along their shared
normal, pointing from the mover's face into the mover's own body (so away
from the other object), and the two planes decide the distance. plan()
travels whatever distance, in either direction, puts the two faces exactly
the wanted gap apart, whether they clash or not.

Pipes and conduits arrive as rings of narrow strips, none of them parallel
to anything, so with both items' triangles to hand a curved side is looked
past (pynavis._surfaces). Against a flat face, the flat face sets the
direction and the curved object is measured to its real surface where it is
in front of that face. Two straight round items are measured axis to axis,
less both radii, along their line of closest approach.
"""

from pynavis import _surfaces, lengths
from pynavis.faces import add, choose, dot, normalize, scale, side_of, sub

# Clear Clash remembers the last gap it was given, with its unit, so the next
# prompt opens on it and Enter accepts it. Older versions kept a fixed
# clearance in the same file, set with Shift+Click; until a gap has been
# given, that clearance is the starting value (starting_gap).
TOOL = 'resolve_clash'
DEFAULTS = {
    'gap': None,                     # None: no gap given yet
    'gap_units': 'Millimeters',
    'clearance': 0.0,                # read only, from older versions
    'clearance_units': 'Millimeters',
}

# Two faces whose normals differ by more than this many degrees are not
# parallel enough to be "the two faces": the tool refuses rather than guess.
PARALLEL_DEGREES = 1.0

# The statuses that move nothing and put the reason on the banner.
REFUSALS = ('no-face', 'not-parallel', 'no-overlap', 'curved')

# Model units that read naturally as feet and inches, with inches per unit.
_INCHES_PER_UNIT = {'Feet': 12.0, 'Inches': 1.0, 'Yards': 36.0, 'Miles': 63360.0}

# Short suffix for a decimal reading in every other unit.
_SUFFIX = {'Meters': 'm', 'Centimeters': 'cm', 'Millimeters': 'mm',
           'Kilometers': 'km', 'Micrometers': 'um', 'Mils': 'mil',
           'Microinches': 'uin'}


def convert(value, from_units, to_units):
    """A length in one Navisworks unit name expressed in another; unknown
    unit names are treated as metres (pynavis.lengths)."""
    return lengths.convert(value, from_units, to_units)


def format_length(value, units, denominator=16):
    """A length in model units as people type it: feet and inches to the
    nearest 1/denominator for imperial documents, a decimal with a suffix
    otherwise."""
    per_unit = _INCHES_PER_UNIT.get(units)
    if per_unit is not None:
        return format_feet_inches(value * per_unit, denominator)
    suffix = _SUFFIX.get(units, units)
    if units == 'Millimeters':
        text = ('%.1f' % value).rstrip('0').rstrip('.')
        return '%s %s' % (text or '0', suffix)
    return '%.3f %s' % (value, suffix)


def format_feet_inches(total_inches, denominator=16):
    """'2ft 3in 3/16' from a length in inches; the fraction is reduced and
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


# ---- typed lengths ---------------------------------------------------------
# The shared reader (pynavis.lengths): what a Revit user types into a length
# field, in whatever unit the document is in.


def parse_length(text, units):
    """A typed length in the document's units, or None when it cannot be read."""
    return lengths.parse(text, units)


def format_input(value, units, denominator=16):
    """A length as a user would type it back; parse_length reads it back."""
    return lengths.format_input(value, units, denominator)


def gap_state(gap, units, mover, obstacle):
    """Where the two faces stand now, for the prompt: 'Pipe 1 is 0ft 3in into
    Beam 7' for a clash (a negative gap), 'Tray 42 is 350 mm from Duct 7'
    otherwise."""
    if gap < 0:
        return '%s is %s into %s' % (mover, format_length(-gap, units), obstacle)
    return '%s is %s from %s' % (mover, format_length(gap, units), obstacle)


def gap_prompt(state, units):
    """Clear Clash's question: where the two faces stand now, and what may be
    typed, in the document's own style."""
    return '%s. Gap to leave between the two faces, %s:' % (state, lengths.hint(units))


def starting_gap(values, units):
    """The gap the prompt opens on, in the document's units, from the saved
    settings (TOOL, DEFAULTS): the last gap given or, before any, the
    clearance older versions set with Shift+Click, else 0."""
    if values.get('gap') is not None:
        return convert(float(values['gap']), values.get('gap_units') or 'Millimeters', units)
    return convert(float(values.get('clearance') or 0.0),
                   values.get('clearance_units') or 'Millimeters', units)


def remember(gap, units):
    """What to save after a gap is given: the gap with its unit, so it means
    the same in a millimetre document and a feet-and-inches one. The old
    clearance is left behind; the gap has replaced it."""
    return {'gap': float(gap), 'gap_units': units}


def label(names):
    """'Pipe 1234', or 'Pipe 1234 and 2 more', from the selection's names."""
    names = [n for n in names if n] or ['selection']
    if len(names) == 1:
        return names[0]
    return '%s and %d more' % (names[0], len(names) - 1)


def _situate(mover_point, mover_faces, obstacle_point, obstacle_faces, mover_vertices,
             parallel_degrees, result, mover_triangles=None, obstacle_triangles=None,
             mover_body=None, tol=1e-6):
    """Fills result with normal (the direction of travel, away from the
    obstacle), gap (signed, negative is a clash), angle and method from the
    two measured points, or with the refusal status. Returns True when there
    is a gap to work with; plan() then decides how far to travel.

    Without triangles only two parallel planes can be measured. With them,
    a curved side is looked past (see the module docstring); a parallel pair
    that is found anyway is the fallback, so nothing that measured before
    stops measuring."""
    mover_faces = [n for n in (normalize(f) for f in mover_faces) if n is not None]
    obstacle_faces = [n for n in (normalize(f) for f in obstacle_faces) if n is not None]
    if not mover_faces or not obstacle_faces:
        result['status'] = 'no-face'
        result['missing'] = 'mover' if not mover_faces else 'obstacle'
        return False
    diff = sub(obstacle_point, mover_point)
    normal, source, angle = choose(mover_faces, obstacle_faces, diff, parallel_degrees)
    result['angle'] = angle
    pair = source == 'pair'
    status = 'not-parallel'
    if mover_triangles is not None and obstacle_triangles is not None:
        mover = {'point': mover_point, 'faces': mover_faces, 'triangles': mover_triangles,
                 'body': mover_body if mover_body is not None else mover_triangles}
        obstacle = {'point': obstacle_point, 'faces': obstacle_faces,
                    'triangles': obstacle_triangles}
        mover['curved'] = _surfaces.is_curved(mover_triangles, mover_point, mover_faces,
                                              tol, parallel_degrees)
        obstacle['curved'] = _surfaces.is_curved(obstacle_triangles, obstacle_point,
                                                 obstacle_faces, tol, parallel_degrees)
        result['curved'] = {'mover': mover['curved'], 'obstacle': obstacle['curved']}
        if mover['curved'] and obstacle['curved']:
            if _pipes(result, mover, obstacle, parallel_degrees, tol):
                return True
            status = 'curved'
        elif mover['curved'] or obstacle['curved']:
            if _across(result, diff, mover, obstacle, mover_vertices, tol):
                return True
            status = 'no-overlap'
    if not pair:
        result['status'] = status
        return False
    # Travel is into the mover's body: away from the face it was clicked on.
    normal = side_of(mover_vertices, mover_point, normal)
    result['normal'] = normal
    result['gap'] = dot(sub(mover_point, obstacle_point), normal)
    result['method'] = 'planes'
    return True


def _across(result, diff, mover, obstacle, mover_vertices, tol):
    """A flat face against a curved object. The flat face sets the direction;
    the curved one is measured to its real surface, counting only the part in
    front of the flat face. False when no part of it is."""
    flat = mover if obstacle['curved'] else obstacle
    result['flat'] = 'mover' if flat is mover else 'obstacle'
    # Of the flat side's faces (several on an edge), the one the measured
    # line runs most squarely into.
    face = max(flat['faces'], key=lambda n: abs(dot(diff, n)))
    region = _surfaces.face_region(flat['triangles'], flat['point'], face, tol)
    if flat is obstacle:
        # Away from the obstacle is out of its body through the clicked face.
        corners = [v for tri in obstacle['triangles'] for v in tri]
        travel = scale(side_of(corners, obstacle['point'], face), -1.0)
        reach = _surfaces.extent_across(region, travel, mover['body'], tol)
        if reach is None:
            return False
        gap = reach[0] - dot(obstacle['point'], travel)
    else:
        travel = side_of(mover_vertices, mover['point'], face)
        reach = _surfaces.extent_across(region, travel, obstacle['triangles'], tol)
        if reach is None:
            return False
        gap = dot(mover['point'], travel) - reach[1]
    result['normal'] = travel
    result['gap'] = gap
    result['method'] = 'surface'
    return True


def _pipes(result, mover, obstacle, parallel_degrees, tol):
    """Two straight round items: axis to axis along their line of closest
    approach, less both radii. False when either is not a straight round item
    or the two share an axis."""
    first = _surfaces.fit_cylinder(mover['triangles'], mover['point'], tol)
    second = _surfaces.fit_cylinder(obstacle['triangles'], obstacle['point'], tol)
    if first is None or second is None:
        return False
    c1, u1, r1 = first
    c2, u2, r2 = second
    link = _surfaces.between_cylinders(c1, u1, r1, c2, u2, r2, mover['point'], parallel_degrees)
    if link is None:
        return False
    toward, distance = link
    result['normal'] = scale(toward, -1.0)
    result['gap'] = distance - r1 - r2
    result['method'] = 'pipes'
    result['radii'] = (r1, r2)
    return True


def _blank():
    return {'normal': None, 'gap': None, 'move': 0.0, 'vector': (0.0, 0.0, 0.0),
            'angle': None, 'missing': None, 'method': None, 'flat': None}


def plan(mover_point, mover_faces, obstacle_point, obstacle_faces, mover_vertices,
         target, parallel_degrees=PARALLEL_DEGREES, eps=1e-9,
         mover_triangles=None, obstacle_triangles=None, mover_body=None, tol=1e-6):
    """The move that puts the mover's measured face exactly target from the
    other face, in model units, whichever way that is, clash or no clash.

    mover_point, obstacle_point: the two measured points, (x, y, z).
    mover_faces, obstacle_faces: candidate unit normals under each point
        (pynavis.faces.faces_at); a point on an edge has several.
    mover_vertices: any points of the mover's body, used only to tell
        which side of its face the mover lies on.
    target: the gap to leave between the two faces, in model units.
    mover_triangles, obstacle_triangles: world triangles of the item under
        each point (pynavis.faces.surface_under); with both, pipes and other
        curved surfaces can be measured. mover_body: every triangle that
        moves (the whole selection), when that is more than the item
        clicked. tol: the tolerance the faces were found with.

    Returns a dict:
      status    'moved' (there is a move), 'same' (already at the target
                within eps), or one of the REFUSALS:
                'not-parallel' (two flat faces, not parallel),
                'no-overlap' (a flat face with the curved object nowhere in
                front of it: which side was flat says 'flat'),
                'curved' (both round, and not both straight pipes),
                'no-face' (a point with no face under it: which says 'missing')
      normal    unit direction of travel, away from the other object, or None
      gap       signed distance from the other object to the mover along
                normal before moving: negative is a clash
      move      signed distance travelled along normal: positive is away
                from the other object, negative is towards it
      vector    (dx, dy, dz) to apply
      target    the gap asked for
      angle     degrees between the two chosen faces
      method    'planes', 'surface' (flat against curved) or 'pipes'
      missing   'mover', 'obstacle' or None
    """
    result = _blank()
    result['target'] = target
    if not _situate(mover_point, mover_faces, obstacle_point, obstacle_faces,
                    mover_vertices, parallel_degrees, result, mover_triangles,
                    obstacle_triangles, mover_body, tol):
        return result
    move = target - result['gap']
    if abs(move) <= eps:
        result['status'] = 'same'
        return result
    result['status'] = 'moved'
    result['move'] = move
    result['vector'] = scale(result['normal'], move)
    return result


def describe(result, units, mover, obstacle, eps=1e-9):
    """(level, title, detail) for a plan() result, after the move has been
    applied when there was one."""
    status = result['status']
    if status == 'no-face':
        which = 'first' if result['missing'] == 'mover' else 'second'
        return ('error', 'No face under the %s point' % which,
                'Snap both points onto the two faces and try again.')
    if status == 'not-parallel':
        return ('error', 'The two faces are not parallel',
                '%.1f deg apart. Pick a face on %s and the face of %s it must clear.'
                % (result['angle'], mover, obstacle))
    if status == 'curved':
        return ('error', 'Both points are on curved surfaces',
                'Round to round works on straight pipe and conduit runs only. '
                'Click a straight run, or a flat face on %s or %s.' % (mover, obstacle))
    if status == 'no-overlap':
        flat, curved = (obstacle, mover) if result['flat'] == 'obstacle' else (mover, obstacle)
        return ('error', 'The two do not face each other',
                'No part of %s is in front of the face you clicked on %s. '
                'Pick a face of %s that %s is in front of.' % (curved, flat, flat, curved))
    gap, target = result['gap'], result['target']
    if status == 'same':
        return ('info', 'Already there',
                '%s is %s from %s.' % (mover, format_length(gap, units), obstacle))
    move = result['move']
    title = 'Moved %s by %s' % (mover, format_length(abs(move), units))
    if gap < 0:
        now = ('just clear' if target <= eps
               else '%s clear' % format_length(target, units))
        detail = ('Away from %s: it was %s into it, now %s. Ctrl+Z puts it back.'
                  % (obstacle, format_length(-gap, units), now))
    else:
        detail = ('%s %s: the gap was %s, now %s. Ctrl+Z puts it back.'
                  % ('Away from' if move > 0 else 'Towards', obstacle,
                     format_length(gap, units), format_length(target, units)))
    return ('success', title, detail)
