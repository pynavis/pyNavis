# -*- coding: utf-8 -*-
"""Which saved viewpoint you are looking through, decided without Navisworks.

The tracker panel and the floating tracker window both show the same two
lines: a status ("Active viewpoint" while the camera still sits where the
saved view put it, "Last viewpoint" once you have walked off it) and the view's
name. Every decision behind those two lines lives here, so both shells agree
and both can be tested outside Navisworks
(src/PyNavis.Tests/VpTrackerTests.cs). The live Navisworks wiring is next
door in vptrackerlive.py.

No f-strings: this runs on IronPython 3.4 as well as CPython.
"""

# Positions and quaternion components closer than this count as the same
# camera. Recalling a view and reading the camera back moves the numbers in
# their last decimal places, so an exact compare would report "walked away" the
# instant a view was recalled; navigating with the mouse moves a camera by far
# more than this, so nothing real is missed.
TOLERANCE = 0.01

NOTHING = 'No viewpoint selected'
ACTIVE = 'Active viewpoint'
LAST = 'Last viewpoint'


def matches(current_position, current_rotation, saved_position, saved_rotation,
            tolerance=TOLERANCE):
    """True while the camera still sits where the saved view put it.

    Positions are (x, y, z); rotations are the four components of a
    quaternion. Both are needed: orbiting around a point can leave Position
    untouched, and the quaternion is then the only thing that reports it.

    Anything missing, the wrong length, or not a number reads as "moved",
    because claiming a view is still active when it cannot be checked is the
    worse of the two errors.
    """
    return (_close(current_position, saved_position, tolerance, 3)
            and _close(current_rotation, saved_rotation, tolerance, 4))


def _close(left, right, tolerance, length):
    if left is None or right is None:
        return False
    try:
        if len(left) != length or len(right) != length:
            return False
        for a, b in zip(left, right):
            # "not <" rather than ">=" so NaN, whose comparisons are all False,
            # falls out here as moved instead of sneaking through as a match.
            if not abs(float(a) - float(b)) < tolerance:
                return False
    except (TypeError, ValueError):
        return False
    return True


def display_name(name):
    """The big line: the view's name, or the standing message when there is none."""
    text = '' if name is None else str(name).strip()
    return text or NOTHING


def status_line(name, is_active):
    """The small line above the name.

    Empty when nothing is being tracked, so the panel shows one line rather
    than a label hanging over nothing.
    """
    text = '' if name is None else str(name).strip()
    if not text or text == NOTHING:
        return ''
    return ACTIVE if is_active else LAST
