"""Moves the selected object so the face you point at sits exactly a chosen
gap from the face it must clear, and tells you how far, so the fix can be
typed straight into the authoring tool.

Select the object that should move and click. The native Point to Point
measure opens and the banner along the bottom of the window asks for two
faces: one on the object, then the one it must clear. Both faces are found
from the items' own triangles (pynavis.faces); two flat faces must be
parallel, a pipe or conduit is measured to its real surface from a flat
face, and two straight pipes outside to outside (see facemove). Anything
that cannot be measured is refused on the banner before a number is asked
for. A prompt then shows the gap as it is now ("Pipe 1 is 3in into Beam 7",
"Tray 42 is 350 mm from Duct 7") and asks for the gap to leave, prefilled
with the one used last, so Enter accepts it and a clash review stays at two
clicks and Enter. Before any gap has been given, the clearance older
versions set with Shift+Click is the starting value. The prompt reads
lengths the way Revit does (1' 6 1/2", 3/4", 1 6, 25mm; forms.ask_length)
and asks again on anything it cannot read. The object slides by exactly the
difference, tighter or apart, clash or no clash. The move is the same
permanent transform Item Tools > Transform makes; Ctrl+Z reverses it. The
distance goes in a dialog that stays until dismissed, ready to copy;
refusals and "already there" stay on the banner. Nothing is drawn in the
view. There is no Shift+Click: the prompt is where the gap is set, every
time.

Everything above the guard at the bottom is importable without Navisworks;
see lib/facemove.py for the maths and the words.
"""

import facemove

from pynavis import app, banner, faces, forms, geometry, pick, script, selection, settings
from pynavis.clash import units_to_meters


def names_of(items):
    return facemove.label([str(item.DisplayName or item.ClassDisplayName or '')
                           for item in items])


def triangles_of(items, log):
    """Every triangle of what moves: the whole selection, which is what must
    end up clear. Every item is read; what fails is logged."""
    triangles = []
    for item in items:
        notes = []
        triangles.extend(geometry.world_triangles(item, note=notes.append))
        if notes:
            log.info('mover "%s": %s' % (item.DisplayName, notes[0]))
    return triangles


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

    values = settings.load(facemove.TOOL, facemove.DEFAULTS)
    units = str(doc.Units)
    last_gap = facemove.starting_gap(values, units)
    view = doc.ActiveView
    first, end = Api.Point3D(p1[0], p1[1], p1[2]), Api.Point3D(p2[0], p2[1], p2[2])
    log.info('measurement %s -> %s, units %s, last gap %s' % (p1, p2, units, last_gap))

    meters = units_to_meters(doc)
    tol = faces.tolerance_for((p1, p2), meters)
    floor = faces.tolerance_for((), meters)
    mover_side = faces.surface_under(view, first, tol, log, 'mover', floor=floor)
    obstacle_side = faces.surface_under(view, end, tol, log, 'obstacle', floor=floor)
    mover_item, obstacle_item = mover_side['item'], obstacle_side['item']
    obstacle_name = (str(obstacle_item.DisplayName or obstacle_item.ClassDisplayName or '')
                     if obstacle_item is not None else 'the obstacle')
    if mover_item is not None and not any(mover_item.Equals(m) for m in mover):
        log.warning('first point is on "%s", which is not in the selection'
                    % mover_item.DisplayName)
    body = triangles_of(mover, log)
    shapes = {'mover_triangles': mover_side['triangles'],
              'obstacle_triangles': obstacle_side['triangles'],
              'mover_body': body, 'tol': max(mover_side['tol'], obstacle_side['tol'])}

    def plan(target):
        return facemove.plan(p1, mover_side['faces'], p2, obstacle_side['faces'],
                             [tri[0] for tri in body], target, **shapes)

    # Find the faces before asking for the number, so a refusal (no face,
    # not parallel, nothing in front) never makes the user type a gap that
    # cannot be used.
    situation = plan(last_gap)
    log.info('situation %s' % situation)
    if situation['status'] in facemove.REFUSALS:
        level, title, detail = facemove.describe(situation, units, mover_name, obstacle_name)
        banner.show(level, title, detail)
        return

    banner.clear()
    state = facemove.gap_state(situation['gap'], units, mover_name, obstacle_name)
    target = forms.ask_length(facemove.gap_prompt(state, units), units,
                              default=last_gap, title='Clear Clash')
    if target is None:
        return                              # cancelled: say nothing

    # The gap just given is the one the next prompt opens on.
    settings.save(facemove.TOOL, facemove.remember(target, units))

    result = plan(float(target))
    log.info('plan %s' % result)

    if result['status'] == 'moved':
        moved = geometry.translate(mover, result['vector'], doc, name='Resolve clash')
        log.info('moved %d item(s) by %s' % (moved, result['vector']))
        selection.set_items(mover, doc)

    level, title, detail = facemove.describe(result, units, mover_name, obstacle_name)
    if result['status'] == 'moved':
        # The number is what the user came for and will type elsewhere: it
        # must outlive a glance, so it waits to be dismissed.
        forms.alert(title + chr(10) + chr(10) + detail, title='Clear Clash',
                    copy=facemove.format_length(abs(result['move']), units))
    else:
        banner.show(level, title, detail)


if '__commandpath__' in globals():
    run()
