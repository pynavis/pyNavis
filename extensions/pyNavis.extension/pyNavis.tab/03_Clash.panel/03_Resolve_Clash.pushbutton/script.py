"""Moves the selected object so the face you point at clears the face it
is clashing with, and tells you how far, so the fix can be typed straight
into the authoring tool.

Select the object that should move and click. The native Point to Point
measure opens and the banner along the bottom of the window asks for two
faces: one on the object, then the one it must clear. Both faces are found
from the items' own triangles (pynavis.faces), they must be parallel, and
the object slides along their normal, away from the face you clicked on
it, by exactly the distance that puts that face past the other plane plus
the clearance from the options. The move is the same permanent transform
Item Tools > Transform makes; Ctrl+Z reverses it. The measurement is
dropped as soon as its two points are in and the previous tool comes back.
The distance is the deliverable, so it goes in a dialog that stays until
dismissed, not on the banner; refusals and "already clear" stay on the
banner. Nothing is drawn in the view. Shift+Click sets the clearance.

Everything above the guard at the bottom is importable without Navisworks;
see resolveclash.py for the maths and the words.
"""

import resolveclash

from pynavis import app, banner, faces, forms, geometry, pick, script, selection, settings
from pynavis.clash import units_to_meters


def names_of(items):
    return resolveclash.label([str(item.DisplayName or item.ClassDisplayName or '')
                               for item in items])


def vertices_of(items, log):
    """A spread of the mover's own points, enough to say which side of a
    face its body lies on. Every item is read; what fails is logged."""
    points = []
    for item in items:
        notes = []
        for a, b, c in geometry.world_triangles(item, note=notes.append):
            points.append(a)
        if notes:
            log.info('mover "%s": %s' % (item.DisplayName, notes[0]))
    return points


def run():
    from pynavis._api import Api
    doc = app.get_doc()
    log = script.get_logger()
    mover = selection.get_items()
    if not mover:
        banner.info('Nothing selected', 'Select the object that should move, then click.')
        return
    mover_name = names_of(mover)

    banner.clear()
    measured = pick.measure_points('Click the face of %s that must move' % mover_name,
                                   'Now click the face it must clear',
                                   notify=banner.prompt, keep=False)
    if measured is None:
        banner.clear()
        return                              # Esc, right-click or another tool: say nothing
    p1, p2 = measured

    values = settings.load(resolveclash.TOOL, resolveclash.DEFAULTS)
    units = str(doc.Units)
    clearance = resolveclash.convert(values['clearance'], values['clearance_units'], units)
    view = doc.ActiveView
    first, end = Api.Point3D(p1[0], p1[1], p1[2]), Api.Point3D(p2[0], p2[1], p2[2])
    log.info('measurement %s -> %s, units %s, clearance %s' % (p1, p2, units, clearance))

    tol = faces.tolerance_for((p1, p2), units_to_meters(doc))
    mover_faces, mover_item = faces.faces_under(view, first, tol, log, 'mover')
    obstacle_faces, obstacle_item = faces.faces_under(view, end, tol, log, 'obstacle')
    obstacle_name = (str(obstacle_item.DisplayName or obstacle_item.ClassDisplayName or '')
                     if obstacle_item is not None else 'the obstacle')
    if mover_item is not None and not any(mover_item.Equals(m) for m in mover):
        log.warning('first point is on "%s", which is not in the selection'
                    % mover_item.DisplayName)

    result = resolveclash.plan(p1, mover_faces, p2, obstacle_faces, vertices_of(mover, log),
                               clearance=clearance)
    log.info('plan %s' % result)

    if result['move'] > 0:
        moved = geometry.translate(mover, result['vector'], doc, name='Resolve clash')
        log.info('moved %d item(s) by %s' % (moved, result['vector']))
        selection.set_items(mover, doc)

    level, title, detail = resolveclash.describe(result, units, mover_name, obstacle_name,
                                                 clearance)
    if result['move'] > 0:
        # The number is what the user came for and will type elsewhere: it
        # must outlive a glance, so it waits to be dismissed.
        banner.clear()
        forms.alert(title + chr(10) + chr(10) + detail, title='Resolve Clash',
                    copy=resolveclash.format_length(result['move'], units))
    else:
        banner.show(level, title, detail)


if '__commandpath__' in globals():
    run()
