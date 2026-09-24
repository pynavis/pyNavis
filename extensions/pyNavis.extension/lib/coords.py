"""Coordinates as plain text, plus the camera arithmetic behind Go to
Coordinates.

Two bundles share this module: Get Coordinates formats the point the native
measure tool is sitting on, and Go to Coordinates reads a point back out of
whatever the user pasted, then works out where the camera has to stand to look
at it.

parse() is deliberately strict about how many numbers it accepts and
deliberately loose about how they are written, because both halves of that
come from the same place: the text arrives from a clipboard that could hold a
schedule row, a chat message or another tool's readout. Three numbers or
nothing, so a truncated paste moves nothing; commas, semicolons, tabs,
brackets and X=/Y=/Z= labels all understood, so a reasonable paste is not
rejected for punctuation. Note the comma always separates: "1,5 2 3" reads as
four numbers and is refused rather than being read as a European decimal, the
safe way round, since the alternative is flying the camera to a coordinate
nobody typed.

Everything here takes plain tuples and strings and imports nothing but the
stdlib, so it is unit-tested outside Navisworks
(src/PyNavis.Tests/CoordsTests.cs). No f-strings, for IronPython 3.4
compatibility.
"""

import math
import re

# Overlay tag for the marker Go to Coordinates draws. One marker at a time, and
# Shift+Click (config.py) clears this same tag.
MARKER_TAG = 'go-to-coordinates'

# How far in front of the point the camera ends up, in METRES. Far enough that
# a door-sized object around the point is in frame, close enough that the point
# is not a speck. The caller converts it into model units through
# pynavis.clash.units_to_meters, because the same 5m has to read the same in a
# model drawn in feet.
VIEW_DISTANCE_METERS = 5.0

# The direction a Navisworks camera looks with no rotation applied: down its
# own negative Z, up being positive Y. view_direction() rotates this by the
# viewpoint's own rotation to get the world direction, because the API exposes
# no property that hands the direction over ready made.
CAMERA_FORWARD = (0.0, 0.0, -1.0)

# Text longer than this is refused unread. Go to Coordinates reads the
# clipboard on every run to fill its box in, and whatever the user last copied
# is usually not a coordinate; a copied spreadsheet does not deserve a scan.
MAX_TEXT_CHARS = 100000

# "X=1", "y: 2", "Z =3": an axis label in front of a number, dropped before the
# split so the numbers are all that is left.
_LABEL = re.compile(r'[XxYyZz]\s*[:=]\s*')

# Commas, semicolons, tabs, newlines and plain spaces all separate.
_SEPARATORS = re.compile(r'[,;\s]+')

# Brackets and quotes, stripped off the whole text and off each number. The
# decimal point is deliberately NOT in here: stripping it would turn ".5" into
# "5" and move the camera ten times too far.
_EDGES = '[](){}<>"\'`'


def parse(text):
    """(x, y, z) floats from text a human typed or pasted, or None.

    None means "this is not a coordinate": no text, too much text, anything
    that is not a number, and any count other than exactly three. Two numbers
    is a truncated paste and four is a spreadsheet row, and guessing which
    three were meant would move the view somewhere nobody asked for.
    """
    if not text:
        return None
    if len(text) > MAX_TEXT_CHARS:
        return None

    cleaned = _LABEL.sub(' ', text.strip().strip(_EDGES))
    values = []
    for token in _SEPARATORS.split(cleaned):
        token = token.strip(_EDGES)
        if not token:
            continue
        if len(values) == 3:
            return None
        try:
            value = float(token)
        except ValueError:
            return None
        if math.isnan(value) or math.isinf(value):
            return None
        values.append(value)

    if len(values) != 3:
        return None
    return (values[0], values[1], values[2])


def format_point(point, decimals=3):
    """'X, Y, Z' at a fixed number of decimals, in the shape parse() reads
    back, so a point survives the round trip through the clipboard."""
    spec = '%.' + str(max(0, int(decimals))) + 'f'
    return ', '.join([spec % float(value) for value in tuple(point)[:3]])


def normalize(vector):
    """The unit vector along vector, or None when it has no length."""
    x, y, z = float(vector[0]), float(vector[1]), float(vector[2])
    size = math.sqrt(x * x + y * y + z * z)
    if size == 0.0:
        return None
    return (x / size, y / size, z / size)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def view_direction(rotation):
    """The world direction a camera carrying this rotation looks along, as a
    unit vector, or None when the rotation is degenerate.

    rotation is the viewpoint's Rotation3D as (A, B, C, D): A, B, C are the
    quaternion's vector part and D its scalar, which is the order
    Rotation3D(a, b, c, d) takes them in. Rotating CAMERA_FORWARD by it is
    what turns "the camera's own negative Z" into a world direction.
    """
    a, b, c, d = (float(rotation[0]), float(rotation[1]),
                  float(rotation[2]), float(rotation[3]))
    size = math.sqrt(a * a + b * b + c * c + d * d)
    if size == 0.0:
        return None
    a, b, c, d = a / size, b / size, c / size, d / size

    axis = (a, b, c)
    once = cross(axis, CAMERA_FORWARD)
    twice = cross(axis, once)
    turned = (CAMERA_FORWARD[0] + 2.0 * (d * once[0] + twice[0]),
              CAMERA_FORWARD[1] + 2.0 * (d * once[1] + twice[1]),
              CAMERA_FORWARD[2] + 2.0 * (d * once[2] + twice[2]))
    return normalize(turned)


def camera_position(target, view_direction, distance):
    """Where the camera has to stand for target to sit in the middle of the
    view, distance away, looking along view_direction: the target pulled back
    down the line of sight.

    Raises ValueError when view_direction has no length, because there is then
    no line of sight to pull back along and any answer would be invented.
    """
    direction = normalize(view_direction)
    if direction is None:
        raise ValueError('the view direction has no length')
    away = float(distance)
    return (float(target[0]) - direction[0] * away,
            float(target[1]) - direction[1] * away,
            float(target[2]) - direction[2] * away)


def marker_segments(point, size):
    """The three axis-aligned arms of a 3D cross centred on point, in the
    [((x, y, z), (x, y, z), dashed), ...] shape pynavis.overlay.add takes.

    A cross rather than a dot: a dot is lost against the model, while three
    arms read as one position from any angle the user orbits to.
    """
    x, y, z = float(point[0]), float(point[1]), float(point[2])
    arm = abs(float(size))
    return [((x - arm, y, z), (x + arm, y, z), False),
            ((x, y - arm, z), (x, y + arm, z), False),
            ((x, y, z - arm), (x, y, z + arm), False)]
