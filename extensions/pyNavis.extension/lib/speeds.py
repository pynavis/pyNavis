# -*- coding: utf-8 -*-
"""Navigation speeds and field of view, with no Navisworks in sight.

Apply Speeds and Speeds to Saved both push three numbers at a viewpoint: how fast
walking moves the camera (linear speed), how fast it turns (angular speed),
and how wide the lens is (field of view). Every decision about those numbers
lives here, so the two buttons can never disagree and both can be tested
outside Navisworks (src/PyNavis.Tests/SpeedsTests.cs).

Linear speed is the awkward one. Navisworks stores it in the DOCUMENT's units,
so the same number means 30 metres per second in one model and 30 feet per
second in the next. This store therefore keeps the number in the unit the user
typed it in, and converts at the moment it is applied to a viewpoint or written
into an exported XML. Whatever the document is in, "30 m/s" stays 30 m/s. The
original add-in stored the converted value instead, which drifted the first
time a model in other units was opened.

Three spellings of every unit are accepted, because all three turn up: a
document stringifies its Units to the enum name ("Feet"), an exported
viewpoints XML declares the short code in <exchange units="ft">, and the
settings dropdown shows the per-second label ("ft/s").

No f-strings: this runs on IronPython 3.4 as well as CPython.
"""

import math

# Both buttons read one store: how fast you want to walk is a fact about you,
# not about which button you pressed. pynavis.script.get_config derives its key
# from the bundle path and so cannot be shared, which is why this is an
# explicit pynavis.settings key instead.
SETTINGS_KEY = 'cfg_reset_speeds'

# What the settings dropdown offers. The stored speed is in one of these,
# never in document units.
UNITS = ('m/s', 'ft/s', 'in/s')

DEFAULTS = {
    'linear': 30.0,           # in linear_unit, not in document units
    'linear_unit': 'm/s',
    'change_linear': True,
    'angular': 45.0,          # degrees per second
    'change_angular': True,
    'fov': 84.0,              # HORIZONTAL field of view, in degrees
    'change_fov': True,
}

# Metres in one of each unit. All eleven members of the Navisworks Units enum
# are here: the original add-in listed six and silently read a model in
# kilometres as one in metres.
_METRES = {
    'm': 1.0, 'm/s': 1.0, 'Meters': 1.0,
    'cm': 0.01, 'cm/s': 0.01, 'Centimeters': 0.01,
    'mm': 0.001, 'mm/s': 0.001, 'Millimeters': 0.001,
    'ft': 0.3048, 'ft/s': 0.3048, 'Feet': 0.3048,
    'in': 0.0254, 'in/s': 0.0254, 'Inches': 0.0254,
    'yd': 0.9144, 'yd/s': 0.9144, 'Yards': 0.9144,
    'km': 1000.0, 'km/s': 1000.0, 'Kilometers': 1000.0,
    'mi': 1609.344, 'mi/s': 1609.344, 'Miles': 1609.344,
    'um': 1e-06, 'Micrometers': 1e-06,
    'mil': 2.54e-05, 'Mils': 2.54e-05,
    'uin': 2.54e-08, 'Microinches': 2.54e-08,
}


def metres_per(unit):
    """Metres in one of unit.

    Anything unrecognised reads as metres rather than raising: a Units member
    this table has not heard of must not throw in the middle of a click.
    """
    return _METRES.get(str(unit), 1.0)


def convert_linear(value, from_unit, to_unit):
    """A linear speed moved between any two units this module knows."""
    if from_unit == to_unit:
        return float(value)
    return float(value) * metres_per(from_unit) / metres_per(to_unit)


def angular_radians(degrees):
    """Degrees per second to radians per second, which is what
    Viewpoint.AngularSpeed wants."""
    return float(degrees) * math.pi / 180.0


def vertical_fov(horizontal_degrees, aspect_ratio):
    """Vertical field of view in radians, which is what Viewpoint.HeightField is.

    Navisworks has no horizontal setter, so a horizontal angle has to be bent
    through the viewport's aspect ratio. Returns None instead of raising when
    the angle or the ratio is unusable, so the caller can skip the field of
    view and still apply the two speeds.
    """
    try:
        degrees = float(horizontal_degrees)
        ratio = float(aspect_ratio)
    except (TypeError, ValueError):
        return None
    # Written as a range test so NaN (every comparison False) falls out here
    # alongside zero, negatives and infinity.
    if not 0.0 < ratio < float('inf'):
        return None
    if not 0.0 < degrees < 180.0:
        return None
    half = math.radians(degrees) / 2.0
    return 2.0 * math.atan(math.tan(half) / ratio)


def parse_number(text, fallback):
    """A number typed into a text box, or fallback when it is not one.

    Blank and junk both fall back rather than refusing to close the window.
    The original add-in warned and substituted a default; losing the other two
    values because one box had a typo would be worse than a quiet default.
    """
    try:
        value = float(str(text).strip())
    except (TypeError, ValueError):
        return float(fallback)
    if not -float('inf') < value < float('inf'):    # NaN and both infinities
        return float(fallback)
    return value


def normalise(values):
    """A settings dict repaired into something both buttons can apply.

    Settings files are user-editable JSON, so nothing here may assume the types
    it finds: numbers that arrived as text or as nonsense fall back to
    DEFAULTS, the unit is forced to one the dropdown offers, negative speeds
    clamp to zero, a field of view outside (0, 180) falls back, and the three
    flags become real booleans.
    """
    source = values if isinstance(values, dict) else {}
    result = dict(DEFAULTS)

    result['linear'] = max(0.0, parse_number(source.get('linear'), DEFAULTS['linear']))
    unit = str(source.get('linear_unit', DEFAULTS['linear_unit']))
    result['linear_unit'] = unit if unit in UNITS else DEFAULTS['linear_unit']
    result['angular'] = max(0.0, parse_number(source.get('angular'), DEFAULTS['angular']))
    fov = parse_number(source.get('fov'), DEFAULTS['fov'])
    result['fov'] = fov if 0.0 < fov < 180.0 else DEFAULTS['fov']
    for key in ('change_linear', 'change_angular', 'change_fov'):
        result[key] = bool(source.get(key, DEFAULTS[key]))
    return result


def trim(number):
    """A number without its trailing zeros, so 30.0 prints as 30."""
    text = '%.4f' % float(number)
    return text.rstrip('0').rstrip('.') or '0'


def summary(values):
    """One line naming what a reset will change, for the toast detail.

    Empty when all three are switched off, which is the caller's cue to say
    nothing was reset rather than claim a success.
    """
    parts = []
    if values['change_linear']:
        parts.append('%s %s' % (trim(values['linear']), values['linear_unit']))
    if values['change_angular']:
        parts.append('%s deg/sec' % trim(values['angular']))
    if values['change_fov']:
        parts.append('%s deg FOV' % trim(values['fov']))
    return ', '.join(parts)
