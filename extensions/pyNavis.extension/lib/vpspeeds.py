# -*- coding: utf-8 -*-
"""Writes the stored speeds and field of view into saved viewpoints.

Apply Speeds does this to the view on screen. This does it to saved views in the
document, without loading a single one of them.

Getting there took two dead ends worth recording, because both look like they
should work:

  * `saved.Viewpoint.LinearSpeed = x` changes nothing. `Viewpoint` carries an
    `IsReadOnly` flag, the copy a document-owned item hands back is read-only,
    and the property hands back a FRESH copy on every call, so the assignment
    lands on an object nobody ever reads again. It fails silently.
  * `ReplaceFromCurrentView` does work, but only by making each viewpoint
    current first, which walks the user through every view on screen and takes
    as long as loading them all. It also re-captures whatever is hidden or
    overridden right now, so it can only be used safely after a recall.

What actually works is to copy the saved ITEM, edit the copy (a detached item is
not read-only), and swap it back in with `ReplaceWithCopy`. Nothing is loaded,
the camera never moves, and the copy carries the name, comments, redlines and
overrides across with it. `Guid` is settable, so the replacement keeps the
original's identity and anything referencing it still resolves.

`REBUILT` is the fallback for a document that refuses to hand back an editable
copy. It rebuilds the item from the camera alone, which keeps the name, the guid
and the comments but loses redlines and appearance overrides, so the caller says
so rather than quietly dropping them.

No f-strings: this runs on IronPython 3.4 as well as CPython.
"""

import speeds

from pynavis import viewpoints
from pynavis.view import aspect_ratio

# How the replacement item was made, for the log line and the toast detail.
FULL_COPY = 'keeping comments and overrides'
REBUILT = 'rebuilt from the camera'


def targets(snapshot, guids):
    """The snapshot rows for the given guids, animations and folders dropped.

    A folder has no camera of its own, and an animation's cameras live on its
    cuts rather than on the animation, so neither is something this can write to.
    Pure, so the filtering is testable without a document.
    """
    wanted = set(guids)
    return [row for row in snapshot
            if row['guid'] in wanted and not row['is_folder']
            and row['kind'] == 'viewpoint']


def dropped_kinds(snapshot, guids):
    """How many of the picked guids were skipped, by kind: {'folder': n, 'animation': n}.

    The caller reports this rather than silently writing to fewer viewpoints than
    the user ticked.
    """
    wanted = set(guids)
    counts = {'folder': 0, 'animation': 0}
    for row in snapshot:
        if row['guid'] not in wanted:
            continue
        if row['is_folder']:
            counts['folder'] += 1
        elif row['kind'] == 'animation':
            counts['animation'] += 1
    return counts


def _write(view, values, unit, height):
    """Applies the settings to an editable Viewpoint. Returns the fields written."""
    from pynavis._api import Api

    written = []
    if values['change_linear']:
        view.LinearSpeed = speeds.convert_linear(
            values['linear'], values['linear_unit'], unit)
        written.append('linear')
    if values['change_angular']:
        view.AngularSpeed = speeds.angular_radians(values['angular'])
        written.append('angular')
    if values['change_fov'] and height is not None:
        if view.Projection == Api.ViewpointProjection.Perspective:
            view.HeightField = height
            written.append('fov')
    return written


def _editable_view_of(item):
    """The Viewpoint of a detached item copy, if that copy will accept a write.

    Returns None when it will not, which sends the caller to REBUILT. The test is
    a real write and read-back rather than trusting IsReadOnly, because the flag
    says what the object claims and the read-back says what the object did.
    """
    try:
        view = item.Viewpoint
        if view.IsReadOnly:
            return None
        before = view.LinearSpeed
        view.LinearSpeed = before + 7.0
        stuck = abs(item.Viewpoint.LinearSpeed - (before + 7.0)) < 1e-9
        view.LinearSpeed = before
        return item.Viewpoint if stuck else None
    except Exception:
        return None


def pick_route(saved_items, guid):
    """FULL_COPY when a copied item accepts a camera write, else REBUILT.

    Decided once per run on a DETACHED copy, so the probe cannot touch the
    document however it turns out.
    """
    item = viewpoints.resolve(saved_items, guid)
    if item is None:
        return REBUILT
    try:
        return FULL_COPY if _editable_view_of(item.CreateCopy()) is not None else REBUILT
    except Exception:
        return REBUILT


def _replacement(item, values, unit, height, route):
    """The item to swap in, and the fields it changed. (None, []) when nothing changed."""
    from pynavis._api import Api

    if route == FULL_COPY:
        copy = item.CreateCopy()
        view = _editable_view_of(copy)
        if view is not None:
            fields = _write(view, values, unit, height)
            return (copy, fields) if fields else (None, [])

    # Fallback: build a fresh saved viewpoint around an edited camera. Redlines
    # and appearance overrides do not survive this, which the caller reports.
    view = item.Viewpoint.CreateCopy()
    fields = _write(view, values, unit, height)
    if not fields:
        return None, []
    return Api.SavedViewpoint(view), fields


def _swap(saved_items, item, replacement):
    """Puts replacement where item is, keeping its name, place and identity."""
    replacement.DisplayName = item.DisplayName
    # Guid is settable, so the replacement inherits the original's identity and
    # a snapshot taken before this run still resolves afterwards.
    replacement.Guid = item.Guid

    parent = item.Parent
    if parent is None:
        saved_items.ReplaceWithCopy(_root_index(saved_items, item), replacement)
    else:
        saved_items.ReplaceWithCopy(parent, viewpoints._index_in(parent, item), replacement)


def _root_index(saved_items, item):
    for index, child in enumerate(saved_items.RootItem.Children):
        if child.Guid == item.Guid:
            return index
    raise LookupError('viewpoint is not at the root')


def _carry_comments(saved_items, guid, comments):
    """Puts the original's comments back on the replacement, for the REBUILT route.

    FULL_COPY brings them along already; a rebuilt item starts with none.
    """
    if not comments:
        return
    fresh = viewpoints.resolve(saved_items, guid)
    if fresh is None:
        return
    try:
        saved_items.EditComments(fresh, comments)
    except Exception:
        pass                # a lost comment must not fail the speed write


def apply(snapshot, guids, values, doc=None, progress=None, cancelled=None):
    """Writes values into every picked saved viewpoint, as ONE undo step.

    progress(done, total) is called per item; cancelled() is polled per item and
    stops the run cleanly, keeping what was already written.

    Returns (written, unchanged, errors, route, stopped).
    """
    from pynavis import doc as _doc

    d = doc if doc is not None else _doc.get_doc()
    saved_items = d.SavedViewpoints
    unit = str(d.Units)

    rows = targets(snapshot, guids)
    if not rows:
        return 0, 0, [], FULL_COPY, False

    # The field of view is an angle on the screen, so it converts through the
    # viewport ratio, and it has to be the SAME ratio Apply Speeds uses or the
    # two buttons write subtly different lenses for the same setting. That is why
    # the refresh lives in pynavis.view rather than in either script: reading the
    # ratio cold here is exactly how they drifted apart. Without a usable ratio
    # the field of view is skipped and the two speeds still go in.
    height = None
    if values['change_fov']:
        ratio = aspect_ratio(d)
        if ratio is not None:
            height = speeds.vertical_fov(values['fov'], ratio)

    route = pick_route(saved_items, rows[0]['guid'])

    written = 0
    unchanged = 0
    errors = []
    stopped = False

    with viewpoints.transaction('Reset viewpoint speeds', d):
        for index, row in enumerate(rows):
            if cancelled is not None and cancelled():
                stopped = True
                break
            try:
                item = viewpoints.resolve(saved_items, row['guid'])
                if item is None:
                    errors.append("'%s' is no longer there" % row['name'])
                    continue

                replacement, fields = _replacement(item, values, unit, height, route)
                if not fields:
                    unchanged += 1
                    continue

                comments = item.Comments if route == REBUILT else None
                _swap(saved_items, item, replacement)
                if comments is not None:
                    _carry_comments(saved_items, row['guid'], comments)
                written += 1
            except Exception as error:
                errors.append("'%s': %s" % (row['name'], error))

            if progress is not None:
                progress(index + 1, len(rows))

    return written, unchanged, errors, route, stopped
