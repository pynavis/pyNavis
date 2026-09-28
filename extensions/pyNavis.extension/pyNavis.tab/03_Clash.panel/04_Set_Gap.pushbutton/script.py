"""Moves the selected object so the face you point at sits exactly a chosen
gap from another face, pulling it tighter or pushing it away as needed,
whether or not the two clash.

Clear Clash next door only moves when there is a clash and only away. This
is for the other cases: closing systems up so they run tighter, or opening
a gap to make room for something else. Select the object that should move
and click. The native Point to Point measure opens and the banner asks for
two faces: one on the object, then the one it should sit against. Both
faces are found from the items' own triangles (pynavis.faces), they must be
parallel, and a prompt then asks for the gap to leave, showing the gap as
it is now and prefilled with the default from Shift+Click. The object
slides along the faces' normal by exactly the difference. The move is the
same permanent transform Item Tools > Transform makes; Ctrl+Z reverses it.
The distance goes in a dialog that stays until dismissed, ready to copy;
refusals and "already there" stay on the banner. Nothing is drawn in the
view. Shift+Click sets the default gap.

Everything above the guard at the bottom is importable without Navisworks;
see lib/facemove.py for the maths and the words.
"""

import facemove

from pynavis import app, banner, faces, forms, geometry, pick, script, selection, settings
from pynavis.clash import units_to_meters


def names_of(items):
    return facemove.label([str(item.DisplayName or item.ClassDisplayName or '')
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
                                   'Now click the face it should sit against',
                                   notify=banner.prompt, keep=False)
    if measured is None:
        banner.clear()
        return                              # Esc, right-click or another tool: say nothing
    p1, p2 = measured

    values = settings.load(facemove.GAP_TOOL, facemove.GAP_DEFAULTS)
    units = str(doc.Units)
    default_gap = facemove.convert(values['gap'], values['gap_units'], units)
    view = doc.ActiveView
    first, end = Api.Point3D(p1[0], p1[1], p1[2]), Api.Point3D(p2[0], p2[1], p2[2])
    log.info('measurement %s -> %s, units %s, default gap %s' % (p1, p2, units, default_gap))

    tol = faces.tolerance_for((p1, p2), units_to_meters(doc))
    mover_faces, mover_item = faces.faces_under(view, first, tol, log, 'mover')
    obstacle_faces, obstacle_item = faces.faces_under(view, end, tol, log, 'obstacle')
    obstacle_name = (str(obstacle_item.DisplayName or obstacle_item.ClassDisplayName or '')
                     if obstacle_item is not None else 'the other object')
    if mover_item is not None and not any(mover_item.Equals(m) for m in mover):
        log.warning('first point is on "%s", which is not in the selection'
                    % mover_item.DisplayName)
    body = vertices_of(mover, log)

    # Find the faces before asking for the number, so a refusal (no face,
    # not parallel) never makes the user type a gap that cannot be used.
    situation = facemove.plan_gap(p1, mover_faces, p2, obstacle_faces, body, default_gap)
    if situation['status'] in ('no-face', 'not-parallel'):
        level, title, detail = facemove.describe_gap(situation, units, mover_name, obstacle_name)
        banner.show(level, title, detail)
        return

    banner.clear()
    now = situation['gap']
    state = ('%s is %s into %s' % (mover_name, facemove.format_length(-now, units), obstacle_name)
             if now < 0 else
             '%s is %s from %s' % (mover_name, facemove.format_length(now, units), obstacle_name))
    target = forms.ask_number('%s. Gap to leave between the two faces, in %s:'
                              % (state, units.lower()),
                              default=default_gap, min_value=0, title='Set Gap')
    if target is None:
        return                              # cancelled: say nothing

    result = facemove.plan_gap(p1, mover_faces, p2, obstacle_faces, body, float(target))
    log.info('plan %s' % result)

    if result['status'] == 'moved':
        moved = geometry.translate(mover, result['vector'], doc, name='Set gap')
        log.info('moved %d item(s) by %s' % (moved, result['vector']))
        selection.set_items(mover, doc)

    level, title, detail = facemove.describe_gap(result, units, mover_name, obstacle_name)
    if result['status'] == 'moved':
        # The number is what the user came for and will type elsewhere: it
        # must outlive a glance, so it waits to be dismissed.
        forms.alert(title + chr(10) + chr(10) + detail, title='Set Gap',
                    copy=facemove.format_length(abs(result['move']), units))
    else:
        banner.show(level, title, detail)


if '__commandpath__' in globals():
    run()
