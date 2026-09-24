"""Pure half of Resolve Clash: settings, unit conversion, and the words the
toast uses. No Navisworks import, so this is unit-testable anywhere."""

TOOL = 'resolve_clash'
DEFAULTS = {
    # Air to leave between the two objects along the axis moved, in
    # clearance_units, so the same setting means the same thing in a
    # millimetre document and a feet-and-inches one.
    'clearance': 0.0,
    'clearance_units': 'Millimeters',
    # 'auto' tries all six axis directions and keeps the shortest; 'x',
    # 'y' or 'z' tries both ways along that one axis.
    'direction': 'auto',
}

# Session variable holding the first click's selection until the second.
PENDING = 'resolve_clash.mover'

DIRECTIONS = (
    ('auto', 'Auto (shortest of the six axis moves)'),
    ('x', 'X only'),
    ('y', 'Y only'),
    ('z', 'Z only'),
)

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

_WAY = {'+x': 'in +X', '-x': 'in -X', '+y': 'in +Y', '-y': 'in -Y',
        '+z': 'up', '-z': 'down'}


def axes_for(direction):
    """The axis directions pynavis.separation should try for a setting."""
    if direction in ('x', 'y', 'z'):
        return ('+' + direction, '-' + direction)
    return None


def direction_label(direction):
    for key, label in DIRECTIONS:
        if key == direction:
            return label
    return DIRECTIONS[0][1]


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


def describe(result, units, mover, obstacle, clearance=0.0):
    """(level, title, detail) for a toast from a pynavis.separation.resolve
    result, after the move has been applied when there was one."""
    axis = result['axis']
    way = _WAY.get(axis, axis)
    if result['status'] == 'clear':
        if result['gap'] is None:
            return ('info', 'Already clear',
                    '%s and %s never meet along any axis.' % (mover, obstacle))
        if result['gap'] == 0:
            return ('info', 'Already clear',
                    '%s touches %s but does not cross it. Set a clearance to push it away.'
                    % (mover, obstacle))
        return ('info', 'Already clear',
                '%s is %s from %s along %s.'
                % (mover, format_length(result['gap'], units), obstacle, _axis_name(axis)))

    title = 'Moved %s %s by %s' % (mover, way, format_length(result['move'], units))
    if result['status'] == 'tight':
        detail = ('It was clear of %s by %s; now by %s. Ctrl+Z puts it back.'
                  % (obstacle, format_length(result['gap'], units),
                     format_length(clearance, units)))
    elif clearance > 0:
        detail = ('Clear of %s with %s to spare. Ctrl+Z puts it back.'
                  % (obstacle, format_length(clearance, units)))
    else:
        detail = 'Clear of %s. Ctrl+Z puts it back.' % obstacle
    return ('success', title, detail)


def _axis_name(axis):
    return axis[1].upper()
