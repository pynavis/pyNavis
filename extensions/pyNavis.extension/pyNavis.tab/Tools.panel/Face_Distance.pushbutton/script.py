"""Perpendicular distance between the two faces the current measurement
touches.

A rotated project makes the measure tool's X/Y/Z readouts meaningless and
the straight line overshoots whenever the two clicks are not exactly
opposite each other. A click starts a native point-to-point measurement
(pynavis.pick.measure_points), waits for its two points, finds which faces
each point sits on from the items' own triangles, and toasts the measurement
projected onto the normal of the parallel pair. The same value is drawn in
the view as a dimension square to the faces, beside the native measurement,
which is left on screen. Everything above the guard at the bottom is
importable without Navisworks; see facedistance.py for the math.
"""

import facedistance

from pynavis import app, geometry, overlay, pick, script, settings, toast
from pynavis.clash import units_to_meters

# Overlay tag for the dimension this tool draws; one at a time.
DIMENSION_TAG = 'face-distance'

# How many items under a point are walked before giving up on it. The
# front-most item is usually the right one; a translucent volume or a
# touching neighbour in front of it is the reason for the rest.
_CANDIDATE_LIMIT = 6

# Half-size in pixels of the rectangle picked around each point.
_PICK_HALF = 3


def _as_tuple(p):
    return (float(p.X), float(p.Y), float(p.Z))


def candidates_at(view, point, label, log):
    """Items under the pixel a world point projects to, front-most first,
    with the single-item pick's result leading when it exists."""
    from pynavis._api import Api
    items = []
    try:
        shot = view.ProjectPoint(point, False, False)
        log.info('%s point %s -> pixel (%d, %d)' % (label, _as_tuple(point), shot.X, shot.Y))
        hit = view.PickItemFromPoint(shot.X, shot.Y, _PICK_HALF, True,
                                     getattr(Api.PickPrimitives, 'None'))
        if hit is not None and hit.ModelItem is not None:
            items.append(hit.ModelItem)
        near = view.PickItemsFromRectangle(shot.X - _PICK_HALF, shot.Y - _PICK_HALF,
                                           2 * _PICK_HALF, 2 * _PICK_HALF, False, True)
        if near is not None:
            for item in near:
                if not any(item.Equals(seen) for seen in items):
                    items.append(item)
    except Exception as error:
        log.warning('%s pick failed: %s' % (label, error))
    return items[:_CANDIDATE_LIMIT]


def faces_under(view, point, label, tol, log):
    """Candidate face normals for a measured point: the first item whose
    triangles contain the point supplies them. Every item tried is logged."""
    p = _as_tuple(point)
    for item in candidates_at(view, point, label, log):
        notes = []
        triangles = geometry.world_triangles(item, note=notes.append)
        faces = facedistance.faces_at(p, triangles, tol) if triangles else []
        log.info('%s item "%s" (%s): %d triangle(s), %d face(s) under the point%s'
                 % (label, item.DisplayName, item.ClassDisplayName, len(triangles),
                    len(faces), ('; ' + notes[0]) if notes else ''))
        if faces:
            for n in faces:
                log.info('%s face normal (%.4f, %.4f, %.4f)' % (label, n[0], n[1], n[2]))
            return faces
    return []


def run():
    from pynavis._api import Api
    doc = app.get_doc()
    log = script.get_logger()
    # The dimension from the last run goes with the measurement it was
    # anchored to, which the pick drops before it starts a new one.
    overlay.clear(DIMENSION_TAG)
    overlay.redraw()
    measured = pick.measure_points('Click the first face', 'Now click the second face')
    if measured is None:
        return                              # Esc, right-click or another tool: say nothing
    p1, p2 = measured

    values = settings.load(facedistance.TOOL, facedistance.DEFAULTS)
    view = doc.ActiveView
    first, end = Api.Point3D(p1[0], p1[1], p1[2]), Api.Point3D(p2[0], p2[1], p2[2])
    log.info('measurement %s -> %s, units %s' % (p1, p2, doc.Units))

    tol = facedistance.tolerance_for((p1, p2), units_to_meters(doc))
    result = facedistance.resolve(
        p1, p2,
        faces_under(view, first, 'first', tol, log),
        faces_under(view, end, 'end', tol, log),
        parallel_degrees=float(values['parallel_degrees']))
    log.info('result %s' % result)

    units = str(doc.Units)
    denominator = int(values['denominator'])
    title, detail = facedistance.describe(result, units, denominator)
    if result['status'] == 'no-face':
        overlay.clear(DIMENSION_TAG)
        overlay.redraw()
        toast.error(title, detail)
        return
    if result['status'] == 'ok':
        toast.success(title, detail)
    else:
        toast.warning(title, detail)

    # The same value drawn in the view: a dimension square to the faces from
    # the first point, a dashed extension to the second, cleared by the
    # overlay itself when the measurement changes.
    start, foot = facedistance.dimension(p1, p2, result['normal'])
    drawn = overlay.dimension(DIMENSION_TAG, start, foot,
                              facedistance.format_length(result['across'], units, denominator),
                              extension_to=p2, anchor=(p1, p2))
    overlay.redraw()
    log.info('dimension %s -> %s drawn=%s' % (start, foot, drawn))


if '__commandpath__' in globals():
    run()
