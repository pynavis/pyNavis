"""Moves the view to a set of coordinates and marks the point.

The camera keeps the direction it is already looking and only where it stands
changes, so the angle the user set up survives and the point lands in the
middle of the view, coords.VIEW_DISTANCE_METERS away. The focal distance moves
with it, so an orbit straight afterwards turns around the point instead of
around wherever the camera was pivoting before.

The point is also drawn as a cross in the view, because a camera that has
moved to nothing in particular looks like a tool that did nothing. Shift+Click
(config.py) clears it.

Everything above the guard at the bottom is importable without Navisworks; see
lib/coords.py for the parsing and the arithmetic.
"""

import coords

from pynavis import app, forms, lengths, overlay, script, toast
from pynavis.clash import units_to_meters


def prefill(units):
    """The default for the box: the clipboard when it already holds three
    coordinates, which is the round trip straight out of Get Coordinates, and
    an empty box when it holds anything else."""
    pasted = coords.parse(script.clipboard_text(), units)
    if pasted is None:
        return ''
    return coords.format_point(pasted)


def viewing_distance(doc):
    """VIEW_DISTANCE_METERS in this model's own units, so the same 5m frames
    the same thing in a model drawn in feet. units_to_meters reports metres per
    model unit and already answers 1.0 for units it does not know; the guard is
    for a document that reports nothing usable at all."""
    per_unit = units_to_meters(doc)
    if not per_unit:
        return coords.VIEW_DISTANCE_METERS
    return coords.VIEW_DISTANCE_METERS / per_unit


def move_camera(document, target, distance, log):
    """Stands the camera back from target along the direction it is already
    looking. True when the view moved, False when the camera reported no
    usable orientation.

    CurrentViewpoint.Value does not track the live camera (it reports the same
    coordinates however far the user has orbited), so the copy has to come from
    CreateCopy(). Nothing reaches the view until CopyFrom, which is also what
    makes this one change: the same shape pynavis.section and pynavis.viewstate
    use.
    """
    from pynavis._api import Api

    viewpoint = document.CurrentViewpoint.CreateCopy()
    rotation = viewpoint.Rotation
    direction = coords.view_direction(
        (rotation.A, rotation.B, rotation.C, rotation.D))
    if direction is None:
        return False

    position = coords.camera_position(target, direction, distance)
    log.info('direction (%.4f, %.4f, %.4f), camera to %s'
             % (direction[0], direction[1], direction[2],
                coords.format_point(position)))
    viewpoint.Position = Api.Point3D(position[0], position[1], position[2])

    # The focal distance is what a later orbit pivots on, so moving it with the
    # camera is the difference between orbiting around the point and orbiting
    # around thin air. An orthographic camera still takes the new position, so
    # the point is centred either way; only this part may not apply, and a view
    # that moved correctly must not fail over it.
    try:
        viewpoint.FocalDistance = distance
    except Exception as error:
        log.info('focal distance left alone: %s' % error)

    document.CurrentViewpoint.CopyFrom(viewpoint)
    return True


def mark(target, size):
    """Draws the cross on the point, labelled with its own coordinates. The
    overlay answers False when its render plugin is not loaded; the view still
    moved, so a missing drawing is not a failed tool."""
    overlay.clear(coords.MARKER_TAG)
    drawn = overlay.add(coords.MARKER_TAG, coords.marker_segments(target, size),
                        label=coords.format_point(target), label_at=target)
    overlay.redraw()
    return drawn


def run():
    doc = app.get_doc()
    log = script.get_logger()

    units = str(doc.Units)
    ask = 'Coordinates to move to (X, Y, Z):'
    if lengths.is_imperial(units):
        ask = 'Coordinates to move to (X, Y, Z), with commas between them for feet and inches:'
    answer = forms.ask_string(ask, default=prefill(units), title='Go to Coordinates')
    if answer is None:                         # None means cancelled: say nothing
        return

    target = coords.parse(answer, units)
    if target is None:
        toast.error('Could not read coordinates',
                    "Expected three values, like 12.5, -3, 40 or 1' 6\", -3' 3\", 40'.")
        return

    distance = viewing_distance(doc)
    if not move_camera(doc, target, distance, log):
        toast.error('Could not move the view',
                    'The current view reported no usable orientation.')
        return

    # An arm a twentieth of the viewing distance: big enough to find, small
    # enough that it marks a point rather than covering what is around it.
    drawn = mark(target, distance / 20.0)

    units = str(doc.Units)
    text = coords.format_point(target)
    log.info('moved to %s (%s), %.4f units away, marker drawn=%s'
             % (text, units, distance, drawn))
    toast.success('Moved to coordinates', '%s %s' % (text, units))


if '__commandpath__' in globals():
    run()
