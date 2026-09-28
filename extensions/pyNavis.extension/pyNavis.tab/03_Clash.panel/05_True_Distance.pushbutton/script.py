"""Perpendicular distance between the two faces the current measurement
touches.

A rotated project makes the measure tool's X/Y/Z readouts meaningless and
the straight line overshoots whenever the two clicks are not exactly
opposite each other. A click starts a native point-to-point measurement
(pynavis.pick.measure_points), waits for its two points, finds which faces
each point sits on from the items' own triangles (pynavis.faces), and shows
the measurement projected onto the normal of the parallel pair on a banner
across the bottom of the window. The native measurement is dropped as soon
as its two points are in and the previous tool comes back; the same value
is drawn in the view as a dimension square to the faces and stays until
the next run. Everything above the guard at the bottom is importable
without Navisworks; see truedistance.py for the math.
"""

import truedistance

from pynavis import app, banner, faces, overlay, pick, script, settings
from pynavis.clash import units_to_meters

# Overlay tag for the dimension this tool draws; one at a time.
DIMENSION_TAG = 'true-distance'


def run():
    from pynavis._api import Api
    doc = app.get_doc()
    log = script.get_logger()
    # The dimension and banner from the last run make way for this one.
    overlay.clear(DIMENSION_TAG)
    overlay.redraw()
    banner.clear()
    measured = pick.measure_points('Click the first face', 'Now click the second face',
                                   notify=banner.prompt, keep=False)
    if measured is None:
        banner.clear()
        return                              # Esc, right-click or another tool: say nothing
    p1, p2 = measured

    values = settings.load(truedistance.TOOL, truedistance.DEFAULTS)
    view = doc.ActiveView
    first, end = Api.Point3D(p1[0], p1[1], p1[2]), Api.Point3D(p2[0], p2[1], p2[2])
    log.info('measurement %s -> %s, units %s' % (p1, p2, doc.Units))

    tol = truedistance.tolerance_for((p1, p2), units_to_meters(doc))
    result = truedistance.resolve(
        p1, p2,
        faces.faces_under(view, first, tol, log, 'first')[0],
        faces.faces_under(view, end, tol, log, 'end')[0],
        parallel_degrees=float(values['parallel_degrees']))
    log.info('result %s' % result)

    units = str(doc.Units)
    denominator = int(values['denominator'])
    title, detail = truedistance.describe(result, units, denominator)
    if result['status'] == 'no-face':
        overlay.clear(DIMENSION_TAG)
        overlay.redraw()
        banner.error(title, detail)
        return
    if result['status'] == 'ok':
        banner.success(title, detail)
    else:
        banner.warning(title, detail)

    # The same value drawn in the view: a dimension square to the faces from
    # the first point, a dashed extension to the second. The measurement is
    # gone, so this is not anchored to it; the next run clears it.
    start, foot = truedistance.dimension(p1, p2, result['normal'])
    drawn = overlay.dimension(DIMENSION_TAG, start, foot,
                              truedistance.format_length(result['across'], units, denominator),
                              extension_to=p2)
    overlay.redraw()
    log.info('dimension %s -> %s drawn=%s' % (start, foot, drawn))


if '__commandpath__' in globals():
    run()
