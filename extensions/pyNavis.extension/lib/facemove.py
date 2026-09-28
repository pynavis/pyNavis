"""Pure half of Clear Clash and Set Gap: the plane-to-plane move, settings,
unit conversion, and the words the banner uses. No Navisworks import, so
this is unit-testable anywhere. It lives in the extension's lib because a
module inside a bundle folder is only importable by that one bundle.

The user measures two faces: one on the object that moves, one on the other
object. The move is along the faces' shared normal, pointing from the
mover's face into the mover's own body (so away from the other object).
Clear Clash (plan) travels only when the mover's face is short of the
obstacle's plane plus the clearance, and only away. Set Gap (plan_gap)
travels whatever distance, in either direction, puts the two faces exactly
the wanted gap apart. Nothing else about either object's shape enters into
it: the user chose the two planes, so the two planes decide.
"""

from pynavis.faces import add, choose, dot, normalize, scale, side_of, sub

TOOL = 'resolve_clash'
DEFAULTS = {
    # Air to leave between the two faces after the move, in clearance_units,
    # so the same setting means the same thing in a millimetre document and
    # a feet-and-inches one.
    'clearance': 0.0,
    'clearance_units': 'Millimeters',
}

# Set Gap keeps its own default gap, under its own key, so changing one
# tool's number never moves the other's.
GAP_TOOL = 'set_gap'
GAP_DEFAULTS = {
    'gap': 0.0,
    'gap_units': 'Millimeters',
}

# Two faces whose normals differ by more than this many degrees are not
# parallel enough to be "the two faces": the tool refuses rather than guess.
PARALLEL_DEGREES = 1.0

_METERS_PER_UNIT = {
    'Meters': 1.0, 'Centimeters': 0.01, 'Millimeters': 0.001,
    'Kilometers': 1000.0, 'Feet': 0.3048, 'Inches': 0.0254,
    'Yards': 0.9144, 'Miles': 1609.344, 'Micrometers': 1e-6,
    'Microinches': 2.54e-8, 'Mils': 2.54e-5,
}

# Model units that read naturally as feet and inches, with inches per unit.
_INCHES_PER_UNIT = {'Feet': 12.0, 'Inches': 1.0, 'Yards': 36.0, 'Miles': 63360.0}

# Short suffix for a decimal reading in every other unit.
_SUFFIX = {'Meters': 'm', 'Centimeters': 'cm', 'Millimeters': 'mm',
           'Kilometers': 'km', 'Micrometers': 'um', 'Mils': 'mil',
           'Microinches': 'uin'}


def convert(value, from_units, to_units):
    """A length in one Navisworks unit name expressed in another; unknown
    unit names are treated as metres."""
    return (float(value) * _METERS_PER_UNIT.get(from_units, 1.0)
            / _METERS_PER_UNIT.get(to_units, 1.0))


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


def label(names):
    """'Pipe 1234', or 'Pipe 1234 and 2 more', from the selection's names."""
    names = [n for n in names if n] or ['selection']
    if len(names) == 1:
        return names[0]
    return '%s and %d more' % (names[0], len(names) - 1)


def _situate(mover_point, mover_faces, obstacle_point, obstacle_faces, mover_vertices,
             parallel_degrees, result):
    """Fills result with normal, gap and angle from the two measured points,
    or with the refusal status. Returns True when there is a pair of planes
    to work with. Shared by plan() and plan_gap(): the two differ only in
    how far they travel once the geometry is known."""
    mover_faces = [n for n in (normalize(f) for f in mover_faces) if n is not None]
    obstacle_faces = [n for n in (normalize(f) for f in obstacle_faces) if n is not None]
    if not mover_faces or not obstacle_faces:
        result['status'] = 'no-face'
        result['missing'] = 'mover' if not mover_faces else 'obstacle'
        return False
    diff = sub(obstacle_point, mover_point)
    normal, source, angle = choose(mover_faces, obstacle_faces, diff, parallel_degrees)
    result['angle'] = angle
    if source != 'pair':
        result['status'] = 'not-parallel'
        return False
    # Travel is into the mover's body: away from the face it was clicked on.
    normal = side_of(mover_vertices, mover_point, normal)
    result['normal'] = normal
    result['gap'] = dot(sub(mover_point, obstacle_point), normal)
    return True


def _blank():
    return {'normal': None, 'gap': None, 'move': 0.0, 'vector': (0.0, 0.0, 0.0),
            'angle': None, 'missing': None}


def plan(mover_point, mover_faces, obstacle_point, obstacle_faces, mover_vertices,
         clearance=0.0, parallel_degrees=PARALLEL_DEGREES, eps=1e-9):
    """The move that puts the mover's measured face clear of the obstacle's.

    mover_point, obstacle_point: the two measured points, (x, y, z).
    mover_faces, obstacle_faces: candidate unit normals under each point
        (pynavis.faces.faces_at); a point on an edge has several.
    mover_vertices: any points of the mover's body, used only to tell
        which side of its face the mover lies on.
    clearance: air to leave between the two planes, in model units.

    Returns a dict:
      status    'clash' (the mover's face is past the obstacle's plane),
                'tight' (clear, but by less than the clearance),
                'clear' (nothing to do),
                'not-parallel' (a face under each point but no parallel pair),
                'no-face' (a point with no face under it: which says 'missing')
      normal    unit direction of travel, into the mover's body, or None
      gap       signed distance from the obstacle's plane to the mover's
                face along normal before moving: negative is a clash
      move      distance travelled (clearance included), 0 when clear
      vector    (dx, dy, dz) to apply
      angle     degrees between the two chosen faces
      missing   'mover', 'obstacle' or None
    """
    result = _blank()
    if not _situate(mover_point, mover_faces, obstacle_point, obstacle_faces,
                    mover_vertices, parallel_degrees, result):
        return result
    gap = result['gap']
    move = clearance - gap
    if move <= eps:
        result['status'] = 'clear'
        return result
    result['status'] = 'clash' if gap < -eps else 'tight'
    result['move'] = move
    result['vector'] = scale(result['normal'], move)
    return result


def plan_gap(mover_point, mover_faces, obstacle_point, obstacle_faces, mover_vertices,
             target, parallel_degrees=PARALLEL_DEGREES, eps=1e-9):
    """The move that puts the mover's measured face exactly target from the
    other face, in model units, whichever way that is.

    Same arguments and dict as plan(), except:
      status    'moved' (there is a move), 'same' (already at the target
                within eps), or the two refusals
      move      signed distance travelled along normal: positive is away
                from the other object, negative is towards it
      target    the gap asked for
    """
    result = _blank()
    result['target'] = target
    if not _situate(mover_point, mover_faces, obstacle_point, obstacle_faces,
                    mover_vertices, parallel_degrees, result):
        return result
    move = target - result['gap']
    if abs(move) <= eps:
        result['status'] = 'same'
        return result
    result['status'] = 'moved'
    result['move'] = move
    result['vector'] = scale(result['normal'], move)
    return result


def describe(result, units, mover, obstacle, clearance=0.0):
    """(level, title, detail) for the banner from a plan() result, after the
    move has been applied when there was one."""
    status = result['status']
    if status == 'no-face':
        which = 'first' if result['missing'] == 'mover' else 'second'
        return ('error', 'No face under the %s point' % which,
                'Snap both points onto the two faces and try again.')
    if status == 'not-parallel':
        return ('error', 'The two faces are not parallel',
                '%.1f deg apart. Pick a face on %s and the face of %s it must clear.'
                % (result['angle'], mover, obstacle))
    if status == 'clear':
        if result['gap'] <= 0:
            return ('info', 'Already clear',
                    '%s touches %s but does not cross it. Set a clearance to push it away.'
                    % (mover, obstacle))
        return ('info', 'Already clear',
                '%s is %s from %s.' % (mover, format_length(result['gap'], units), obstacle))

    title = 'Moved %s by %s' % (mover, format_length(result['move'], units))
    if status == 'tight':
        detail = ('It was clear of %s by %s; now by %s. Ctrl+Z puts it back.'
                  % (obstacle, format_length(result['gap'], units),
                     format_length(clearance, units)))
    elif clearance > 0:
        detail = ('Clear of %s with %s to spare. Ctrl+Z puts it back.'
                  % (obstacle, format_length(clearance, units)))
    else:
        detail = 'Clear of %s. Ctrl+Z puts it back.' % obstacle
    return ('success', title, detail)


def describe_gap(result, units, mover, obstacle):
    """(level, title, detail) for a plan_gap() result, after the move has
    been applied when there was one."""
    status = result['status']
    if status == 'no-face':
        which = 'first' if result['missing'] == 'mover' else 'second'
        return ('error', 'No face under the %s point' % which,
                'Snap both points onto the two faces and try again.')
    if status == 'not-parallel':
        return ('error', 'The two faces are not parallel',
                '%.1f deg apart. Pick a face on %s and the face of %s it should sit against.'
                % (result['angle'], mover, obstacle))
    gap, target = result['gap'], result['target']
    if status == 'same':
        return ('info', 'Already there',
                '%s is %s from %s.' % (mover, format_length(gap, units), obstacle))
    move = result['move']
    title = 'Moved %s by %s' % (mover, format_length(abs(move), units))
    if gap < 0:
        detail = ('Away from %s: it was %s into it, now %s clear. Ctrl+Z puts it back.'
                  % (obstacle, format_length(-gap, units), format_length(target, units)))
    else:
        detail = ('%s %s: the gap was %s, now %s. Ctrl+Z puts it back.'
                  % ('Away from' if move > 0 else 'Towards', obstacle,
                     format_length(gap, units), format_length(target, units)))
    return ('success', title, detail)
