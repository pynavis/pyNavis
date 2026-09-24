"""Moves one object the shortest axis-aligned distance that takes it clear
of another, and tells you how far, so the fix can be typed straight into
the authoring tool.

Two clicks. Select the object that should move and click: the selection is
remembered. Select what it must clear and click again: the two meshes are
read from the model, the move is worked out exactly for the six axis
directions (or the one axis chosen in the options), the shortest is
applied as the same permanent transform Item Tools > Transform makes, and
the toast reports it. Ctrl+Z reverses the move. Shift+Click sets a
clearance and a forced direction.

Everything above the guard at the bottom is importable without Navisworks;
see resolveclash.py for the words and pynavis.separation for the maths.
"""

import resolveclash

from pynavis import app, geometry, script, selection, separation, settings, toast

# Seconds the solver may spend before giving up on very detailed objects.
BUDGET_SECONDS = 30


def names_of(items):
    return resolveclash.label([str(item.DisplayName or item.ClassDisplayName or '')
                               for item in items])


def triangles_of(items, log, label):
    """World triangles for every selected item together, with a note on why
    when none could be read."""
    triangles = []
    notes = []
    for item in items:
        found = geometry.world_triangles(item, note=notes.append)
        log.info('%s "%s": %d triangle(s)%s'
                 % (label, item.DisplayName, len(found), ('; ' + notes[-1]) if notes else ''))
        triangles.extend(found)
    return triangles, (notes[0] if notes else 'the selection has no faces')


def same_items(first, second):
    if len(first) != len(second):
        return False
    return all(any(a.Equals(b) for b in second) for a in first)


def remember(doc, items):
    script.set_envvar(resolveclash.PENDING, {
        'file': str(doc.FileName or ''), 'title': str(doc.Title or ''), 'items': list(items)})


def pending_for(doc):
    """The first click's items when they belong to this document, else None."""
    pending = script.get_envvar(resolveclash.PENDING)
    if not pending:
        return None
    if pending['file'] != str(doc.FileName or '') or pending['title'] != str(doc.Title or ''):
        script.set_envvar(resolveclash.PENDING, None)
        return None
    return pending['items']


def run():
    doc = app.get_doc()
    log = script.get_logger()
    items = selection.get_items()
    mover = pending_for(doc)

    if not items:
        script.set_envvar(resolveclash.PENDING, None)
        toast.info('Nothing selected', 'Select the object that should move, then click.')
        return

    if mover is None:
        remember(doc, items)
        toast.info('Now select what it must clear',
                   '%s will move. Select the obstacle and click again.' % names_of(items))
        return

    if same_items(mover, items):
        toast.warning('Same object twice',
                      'Select the object %s must clear, then click again.' % names_of(mover))
        return

    script.set_envvar(resolveclash.PENDING, None)
    values = settings.load(resolveclash.TOOL, resolveclash.DEFAULTS)
    units = str(doc.Units)
    mover_name, obstacle_name = names_of(mover), names_of(items)

    mover_triangles, why = triangles_of(mover, log, 'mover')
    if not mover_triangles:
        toast.error('Could not read %s' % mover_name, why)
        return
    obstacle_triangles, why = triangles_of(items, log, 'obstacle')
    if not obstacle_triangles:
        toast.error('Could not read %s' % obstacle_name, why)
        return

    clearance = resolveclash.convert(values['clearance'], values['clearance_units'], units)
    axes = resolveclash.axes_for(values['direction'])
    log.info('solving %d x %d triangle(s), direction %s, clearance %s %s'
             % (len(mover_triangles), len(obstacle_triangles), values['direction'],
                clearance, units))
    try:
        result = separation.resolve(mover_triangles, obstacle_triangles, axes=axes,
                                    clearance=clearance, budget_seconds=BUDGET_SECONDS)
    except separation.TooDetailed as error:
        log.warning(str(error))
        toast.error('Too detailed to solve',
                    'Gave up after %d seconds. Try a smaller part of either object.'
                    % BUDGET_SECONDS)
        return
    log.info('result %s' % result)

    if result['status'] != 'clear':
        moved = geometry.translate(mover, result['vector'], doc, name='Resolve clash')
        log.info('moved %d item(s) by %s' % (moved, result['vector']))
        selection.set_items(mover, doc)

    level, title, detail = resolveclash.describe(result, units, mover_name, obstacle_name,
                                                 clearance)
    toast.show(level, title, detail)


if '__commandpath__' in globals():
    run()
