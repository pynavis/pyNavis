# -*- coding: utf-8 -*-
"""Saved-viewpoint tools: the pure rename engine plus thin API helpers.

The pure half (plan_renames) works on plain snapshot rows and runs anywhere;
the API half (snapshot / apply_renames) touches the Navisworks document and
lazy-imports the API, so importing this module never needs Navisworks.

Snapshot row: {'guid': stable SavedItem identity, 'key': '0/2/1' index path,
'parent_key': '0/2' ('' at root), 'folder': owning folder path, 'name',
'depth', 'is_folder', 'kind' ('folder'|'viewpoint'|'animation'), 'comments'}.
Rows are in tree (depth-first) order; numbering relies on that.

The GUID is what edits address. Index paths describe tree SHAPE only and go
stale the moment anything moves, so resolving an edit by path could rename or
delete a different item than the one the user picked.
"""
import re


def plan_renames(rows, checked_keys, op):
    """The rename plan for an operation over the checked rows.

    Returns {'renames': [{'key','old','new'}...], 'collisions': [message...],
    'problems': [message...]}. Only changed names appear in renames. Collisions
    (two siblings ending up with the same name, or a name renamed to nothing)
    are reported so the caller can block Apply; nothing is ever auto-fixed.
    """
    checked = set(checked_keys)
    kind = op.get('type')

    pattern = None
    if kind == 'replace' and op.get('regex'):
        try:
            pattern = re.compile(op.get('find') or '')
        except re.error as error:
            return {'renames': [], 'collisions': [],
                    'problems': ['invalid pattern: %s' % error]}

    renames = []
    number = op.get('start', 1)
    for row in rows:
        if row['key'] not in checked:
            continue
        old = row['name']
        if kind == 'replace':
            if pattern is not None:
                new = pattern.sub(op.get('replace') or '', old)
            else:
                find = op.get('find') or ''
                new = old.replace(find, op.get('replace') or '') if find else old
        elif kind == 'affix':
            new = (op.get('prefix') or '') + old + (op.get('suffix') or '')
        elif kind == 'number':
            if row['is_folder']:
                continue                      # numbering folders makes no sense
            new = (op.get('pattern') or '{n}').replace(
                '{n}', str(number).zfill(op.get('pad') or 0))
            number += 1
        elif kind == 'case':
            mode = op.get('mode')
            new = (old.title() if mode == 'title'
                   else old.upper() if mode == 'upper' else old.lower())
        else:
            return {'renames': [], 'collisions': [],
                    'problems': ['unknown operation: %r' % kind]}
        if new != old:
            renames.append({'key': row['key'], 'guid': row.get('guid'),
                            'old': old, 'new': new})

    return {'renames': renames,
            'collisions': _collisions(rows, renames),   # also flags each rename
            'problems': []}


def guids_for(rows, keys):
    """The guids of the given keys, in tree order; unknown keys are skipped."""
    wanted = set(keys)
    return [row['guid'] for row in rows if row['key'] in wanted]


def plan_deletes(rows, checked_keys):
    """The minimal delete set for the checked rows.

    A checked folder stands in for its contents ONLY when every descendant is
    checked too; a folder with an unchecked descendant survives and only its
    checked descendants are deleted. Returns {'keys': minimal keys in tree
    order, 'count': total rows that will disappear (folder contents included)}.
    """
    checked = set(checked_keys)
    descendants = {}
    for row in rows:
        if row['is_folder']:
            prefix = row['key'] + '/'
            descendants[row['key']] = [
                r['key'] for r in rows if r['key'].startswith(prefix)]

    keys = []
    for row in rows:                          # tree order: parents come first
        key = row['key']
        if key not in checked:
            continue
        if row['is_folder'] and not all(k in checked for k in descendants[key]):
            continue
        if any(key.startswith(k + '/') for k in keys):
            continue                          # an ancestor already covers this
        keys.append(key)

    removed = set(keys)
    for key in keys:
        prefix = key + '/'
        for row in rows:
            if row['key'].startswith(prefix):
                removed.add(row['key'])
    return {'keys': keys, 'guids': guids_for(rows, keys), 'count': len(removed)}


def sort_ops(entries):
    """Move steps ((from_index, to_index) in sequence) that reorder a sibling
    list to folders-first, then A-Z case-insensitive; ties keep their current
    order. Computed against a simulation, so applying the steps one by one
    through the API lands exactly the simulated order."""
    sim = list(entries)
    desired = sorted(sim, key=lambda e: (not e[1], e[0].lower()))
    ops = []
    for target in range(len(sim)):
        current = sim.index(desired[target], target)
        if current != target:
            sim.insert(target, sim.pop(current))
            ops.append((current, target))
    return ops


def validate_move(rows, checked_keys, target_key):
    """None when the move is legal, else the reason: the target must be the
    root ('') or a folder, and a folder can never move into its own subtree."""
    by_key = dict((row['key'], row) for row in rows)
    if target_key != '':
        target = by_key.get(target_key)
        if target is None or not target['is_folder']:
            return 'The target is not a folder.'
    for key in checked_keys:
        row = by_key.get(key)
        if row is None or not row['is_folder']:
            continue
        if target_key == key or target_key.startswith(key + '/'):
            return "Cannot move folder '%s' into itself." % row['name']
    return None


def delete_order(keys):
    """Keys sorted so deleting front-to-back never shifts a later index path:
    numerically descending, deepest siblings first."""
    return sorted(keys, reverse=True,
                  key=lambda k: tuple(int(part) for part in k.split('/')))


# --- API half (Navisworks only; lazy imports keep the module pure) -----------

from contextlib import contextmanager


@contextmanager
def transaction(name, doc=None):
    """One Navisworks transaction around a bulk edit, so thousands of changes
    land as a SINGLE undo entry instead of thousands. Commits on a clean exit;
    on an error it disposes without committing and lets the error travel.

    A document already inside a transaction is left alone (nested transactions
    are not a thing here), and a document that cannot start one still runs the
    body, just unbatched.
    """
    from pynavis import doc as _doc
    d = doc if doc is not None else _doc.get_doc()

    trans = None
    try:
        if not d.IsActiveTransaction:
            trans = d.BeginTransaction(name)
    except Exception:
        trans = None

    try:
        yield trans
    except Exception:
        if trans is not None:
            try: trans.Dispose()
            except Exception: pass
        raise
    else:
        if trans is not None:
            try:
                trans.Commit()
            finally:
                try: trans.Dispose()
                except Exception: pass

def apply_deletes(guids, doc=None, progress=None, cancelled=None):
    """Deletes the planned items by GUID; returns (removed_count, errors).

    GUIDs stay valid however the tree shifts underneath, so unlike index paths
    the order of removal cannot make a later entry address the wrong item.
    """
    from pynavis import doc as _doc
    d = doc if doc is not None else _doc.get_doc()
    saved = d.SavedViewpoints

    removed = 0
    errors = []
    with transaction('Delete viewpoints', d):
        for index, guid in enumerate(guids):
            if cancelled is not None and cancelled():
                break
            try:
                item = resolve(saved, guid)
                if item is None:
                    errors.append('an item is no longer there')
                    continue
                saved.Remove(item)
                removed += 1
            except Exception as error:
                errors.append(str(error))
            if progress is not None and (index + 1) % 200 == 0:
                progress(index + 1, len(guids))
    if progress is not None:
        progress(len(guids), len(guids))
    return removed, errors



def snapshot(doc=None):
    """Snapshot rows for the document's saved-viewpoints tree, in tree order.

    Descends ONLY folders: animations are GroupItems too, but their cuts must
    come out as one animation row, never as children (same trap as
    _util.flatten_with_path).
    """
    from pynavis import doc as _doc
    from pynavis._api import Api
    d = doc if doc is not None else _doc.get_doc()

    rows = []

    def walk(children, parent_key, folder, depth):
        for index, item in enumerate(children):
            key = parent_key + '/' + str(index) if parent_key else str(index)
            is_folder = isinstance(item, Api.FolderItem)
            is_animation = isinstance(item, Api.SavedViewpointAnimation)
            try:
                comments = item.Comments.Count
            except Exception:
                comments = 0
            rows.append({
                'guid': str(item.Guid),
                'key': key,
                'parent_key': parent_key,
                'folder': folder,
                'name': item.DisplayName,
                'depth': depth,
                'is_folder': is_folder,
                'kind': ('folder' if is_folder
                         else 'animation' if is_animation else 'viewpoint'),
                'comments': comments,
            })
            if is_folder:
                child_folder = folder + '/' + item.DisplayName if folder else item.DisplayName
                walk(item.Children, key, child_folder, depth + 1)

    walk(d.SavedViewpoints.RootItem.Children, '', '', 0)
    return rows


def resolve(saved, guid_text):
    """The live SavedItem for a snapshot guid, or None when it is gone."""
    from System import Guid
    try:
        return saved.ResolveGuid(Guid(guid_text))
    except Exception:
        return None


def apply_renames(renames, doc=None, progress=None, cancelled=None):
    """Applies a plan from plan_renames; returns (applied_count, errors).

    Every entry is resolved by GUID, never by index path: the document can
    change while the dialog is open, and a stale path would rename whatever
    now sits at that position.
    """
    from pynavis import doc as _doc
    d = doc if doc is not None else _doc.get_doc()
    saved = d.SavedViewpoints

    applied = 0
    errors = []
    with transaction('Rename viewpoints', d):
        for index, entry in enumerate(renames):
            if cancelled is not None and cancelled():
                break
            try:
                item = resolve(saved, entry['guid'])
                if item is None:
                    errors.append("'%s' is no longer there" % entry['old'])
                    continue
                saved.EditDisplayName(item, entry['new'])
                applied += 1
            except Exception as error:
                errors.append("'%s': %s" % (entry['old'], error))
            if progress is not None and (index + 1) % 200 == 0:
                progress(index + 1, len(renames))
    if progress is not None:
        progress(len(renames), len(renames))
    return applied, errors


def apply_sort(rows, folder_keys=None, doc=None):
    """Sorts the root and every folder (or only the given folders) with
    sort_ops; returns (moved_count, errors). Deepest folders sort first so the
    index paths of parents still to be resolved never shift under them."""
    from pynavis import doc as _doc
    from pynavis._api import Api
    d = doc if doc is not None else _doc.get_doc()
    saved = d.SavedViewpoints

    if folder_keys:
        parents = list(folder_keys)
    else:
        parents = [row['key'] for row in rows if row['is_folder']] + ['']
    parents.sort(key=lambda k: k.count('/') if k else -1, reverse=True)

    moved = 0
    errors = []
    for parent_key in parents:
        try:
            parent = (saved.RootItem if parent_key == ''
                      else saved.ResolveIndexPath(_index_path(parent_key)))
            if parent is None:
                errors.append('folder %s is no longer there' % parent_key)
                continue
            entries = [(child.DisplayName, isinstance(child, Api.FolderItem))
                       for child in parent.Children]
            for source, target in sort_ops(entries):
                saved.Move(parent, source, parent, target)
                moved += 1
        except Exception as error:
            errors.append('%s: %s' % (parent_key or 'root', error))
    return moved, errors


def apply_moves(rows, checked_keys, target_key, doc=None):
    """Moves the minimal checked set (plan_deletes' folder-collapse rule) into
    the target folder ('' = top level); returns (moved_count, errors).

    Every step re-resolves by Guid: each Move shifts index paths, so keys can
    only be trusted before the first mutation. Items already directly in the
    target are skipped.
    """
    from pynavis import doc as _doc
    d = doc if doc is not None else _doc.get_doc()
    saved = d.SavedViewpoints

    plan = plan_deletes(rows, checked_keys)          # same folder-collapse rule
    by_key = dict((row['key'], row) for row in rows)
    sources = [(k, g) for k, g in zip(plan['keys'], plan['guids'])
               if by_key[k]['parent_key'] != target_key]
    if not sources:
        return 0, []

    target_row = by_key.get(target_key) if target_key else None
    target_guid = target_row['guid'] if target_row is not None else None

    moved = 0
    errors = []
    with transaction('Move viewpoints', d):
        for _, guid in sources:
            try:
                item = resolve(saved, guid)
                if item is None:
                    errors.append('an item is no longer there')
                    continue
                target = (saved.RootItem if target_guid is None
                          else resolve(saved, target_guid))
                if target is None:
                    return moved, ['the target folder is no longer there']
                parent = item.Parent if item.Parent is not None else saved.RootItem
                saved.Move(parent, _index_in(parent, item), target,
                           target.Children.Count)
                moved += 1
            except Exception as error:
                errors.append(str(error))
    return moved, errors


def create_folder(name, parent_key='', doc=None):
    """Adds an empty viewpoint folder under the parent ('' = top level)."""
    from pynavis import doc as _doc
    from pynavis._api import Api
    d = doc if doc is not None else _doc.get_doc()
    saved = d.SavedViewpoints

    folder = Api.FolderItem()
    folder.DisplayName = name
    if parent_key == '':
        saved.AddCopy(folder)
    else:
        parent = saved.ResolveIndexPath(_index_path(parent_key))
        saved.AddCopy(parent, folder)


def _index_path(key):
    from System import Int32
    from System.Collections.Generic import List
    return List[Int32]([int(part) for part in key.split('/')])


def _index_in(parent, item):
    for index, child in enumerate(parent.Children):
        if child.Guid == item.Guid:
            return index
    raise LookupError('item is not under its recorded parent')


def _collisions(rows, renames):
    """Duplicate-sibling and empty-name reports for a computed plan."""
    planned = dict((r['key'], r['new']) for r in renames)
    parent_of = dict((row['key'], row['parent_key']) for row in rows)

    names = {}                                # parent_key -> final name -> count
    for row in rows:
        final = planned.get(row['key'], row['name'])
        siblings = names.setdefault(row['parent_key'], {})
        siblings[final] = siblings.get(final, 0) + 1

    collisions = []
    for r in renames:
        r['collision'] = False
        if not r['new'].strip():
            r['collision'] = True
            collisions.append("'%s' would have no name" % r['old'])
        elif names[parent_of[r['key']]][r['new']] > 1:
            r['collision'] = True
            collisions.append(
                "'%s' -> '%s' collides with a sibling" % (r['old'], r['new']))
    return collisions
