"""Calculator-style selection memory: one register per document, kept on disk.

The pure half (state, file naming, set math, the cursor) imports nothing from
Navisworks and is unit tested outside it. The API half imports pynavis._api
lazily, inside the functions that need it, so this module stays importable
anywhere.

An entry identifies one ModelItem as a ModelItemPathId:
    {'m': source file name, 'i': model index, 'p': PathId string}
"""

import hashlib
import json
import os

VERSION = 1


class MemoryFormatError(Exception):
    """A memory file could not be understood; the caller treats it as empty."""


class Result(object):
    """What a memory action did, ready for the button script to toast."""

    def __init__(self, level, message, detail='', count=0):
        self.level = level          # 'success' | 'error' | 'info' | 'warning'
        self.message = message
        self.detail = detail
        self.count = count


# ---- state ---------------------------------------------------------------


def new_state(document='', items=None, saved=''):
    return {
        'version': VERSION,
        'document': document or '',
        'saved': saved or '',
        'cursor': -1,
        'items': list(items or []),
    }


def serialize(state):
    return json.dumps(state, indent=2, sort_keys=True)


def deserialize(text):
    try:
        state = json.loads(text)
    except Exception:
        raise MemoryFormatError('memory file is not valid JSON')

    if not isinstance(state, dict):
        raise MemoryFormatError('memory file is not an object')
    if state.get('version') != VERSION:
        raise MemoryFormatError(
            'memory file version %s is not supported' % state.get('version'))
    if not isinstance(state.get('items'), list):
        raise MemoryFormatError('memory file has no item list')

    state['document'] = state.get('document') or ''
    state['saved'] = state.get('saved') or ''
    cursor = state.get('cursor', -1)
    state['cursor'] = cursor if isinstance(cursor, int) else -1
    return state


# ---- file naming ---------------------------------------------------------


def path_for(document_path, root):
    """Memory file for a document. The hash separates same-named files in
    different folders; unsaved documents share the 'untitled' register.

    Everything is derived from the lowercased path: Windows paths are
    case-insensitive, so the same document reached by a differently cased path
    has to land on the same memory file, name included.
    """
    text = (document_path or '').lower()
    digest = hashlib.sha1(text.encode('utf-8')).hexdigest()[:8]
    stem = os.path.splitext(os.path.basename(text))[0]
    name = _sanitize(stem)[:40] or 'untitled'
    return os.path.join(root, '%s-%s.json' % (name, digest))


def _sanitize(name):
    return ''.join(c if (c.isalnum() or c in '-_') else '_' for c in name)


# ---- set math ------------------------------------------------------------


def key(entry):
    """Identity of an entry: model name (case-insensitive) plus path id."""
    return (str(entry.get('m', '')).lower(), str(entry.get('p', '')))


def union(a, b):
    """Everything in a then everything new in b, order preserved, deduped."""
    out, seen = [], set()
    for entry in list(a) + list(b):
        k = key(entry)
        if k in seen:
            continue
        seen.add(k)
        out.append(entry)
    return out


def difference(a, b):
    """Entries of a that b does not contain."""
    drop = set(key(e) for e in b)
    out, seen = [], set()
    for entry in a:
        k = key(entry)
        if k in drop or k in seen:
            continue
        seen.add(k)
        out.append(entry)
    return out


def intersection(a, b):
    """Entries present in both, in a's order."""
    keep = set(key(e) for e in b)
    out, seen = [], set()
    for entry in a:
        k = key(entry)
        if k not in keep or k in seen:
            continue
        seen.add(k)
        out.append(entry)
    return out


# ---- cursor --------------------------------------------------------------


def step(cursor, count, delta):
    """Next/previous index, wrapping at both ends. -1 when there is nothing."""
    if count <= 0:
        return -1
    if cursor < 0 or cursor >= count:
        return 0 if delta > 0 else count - 1
    return (cursor + delta) % count


# ---- storage -------------------------------------------------------------


def memory_root():
    """Where memory files live: %APPDATA%\\pyNavis\\memory."""
    return os.path.join(os.environ.get('APPDATA', ''), 'pyNavis', 'memory')


def load(document_path, root=None):
    """The stored state for a document, or a fresh empty one."""
    path = path_for(document_path, root or memory_root())
    if not os.path.exists(path):
        return new_state(document_path)
    handle = open(path, 'r')
    try:
        return deserialize(handle.read())
    finally:
        handle.close()


def save(state, root=None):
    """Writes the state atomically: temp file first, then replace."""
    root = root or memory_root()
    if not os.path.isdir(root):
        os.makedirs(root)

    path = path_for(state.get('document', ''), root)
    temp = path + '.tmp'
    handle = open(temp, 'w')
    try:
        handle.write(serialize(state))
    finally:
        handle.close()

    try:
        os.replace(temp, path)
    except AttributeError:      # very old runtimes have no os.replace
        if os.path.exists(path):
            os.remove(path)
        os.rename(temp, path)
    return path


def _now():
    import datetime
    return datetime.datetime.now().strftime('%Y-%m-%dT%H:%M:%S')


def apply_entries(state, entries):
    """Replaces the memory contents, stamps the time and resets the stepper."""
    state['items'] = list(entries)
    state['saved'] = _now()
    state['cursor'] = -1
    return state


# ---- Navisworks-facing half ----------------------------------------------
#
# _api is imported inside the functions so this module stays importable (and
# testable) outside a Navisworks session.


def _doc(doc=None):
    from pynavis.doc import get_doc
    return doc if doc is not None else get_doc()


def _state(doc):
    from pynavis.doc import get_filename
    return load(get_filename(doc))


def _source_name(models, index):
    try:
        return os.path.basename(models[index].SourceFileName or '')
    except Exception:
        return ''


def encode_items(items, doc):
    """ModelItems -> entries."""
    models = doc.Models
    out = []
    for item in items:
        path_id = models.CreatePathId(item)
        index = path_id.ModelIndex
        out.append({'m': _source_name(models, index), 'i': index, 'p': path_id.PathId})
    return out


def resolve_entries(entries, doc):
    """entries -> (ModelItems, missing count, missing model names).

    The stored model index is only trusted when the model at that index still
    has the recorded source file name; otherwise the model is found by name, so
    appending or reordering models does not break a memory.
    """
    from pynavis import _api  # loads the API with a clear error outside Navisworks
    from Autodesk.Navisworks.Api.DocumentParts import ModelItemPathId

    models = doc.Models
    by_name = {}
    for i in range(models.Count):
        by_name.setdefault(_source_name(models, i).lower(), []).append(i)

    items, missing, missing_models = [], 0, []
    for entry in entries:
        item = None
        index = _index_for(entry, models, by_name)
        if index is not None:
            path_id = ModelItemPathId()
            path_id.ModelIndex = index
            path_id.PathId = str(entry.get('p', ''))
            try:
                item = models.ResolvePathId(path_id)
            except Exception:
                item = None

        if item is None:
            missing += 1
            name = entry.get('m', '')
            if name and name not in missing_models:
                missing_models.append(name)
        else:
            items.append(item)
    return items, missing, missing_models


def _index_for(entry, models, by_name):
    name = str(entry.get('m', '')).lower()
    index = entry.get('i', -1)
    if isinstance(index, int) and 0 <= index < models.Count:
        if _source_name(models, index).lower() == name:
            return index
    candidates = by_name.get(name) or []
    return candidates[0] if candidates else None


def _selected_entries(doc):
    from pynavis.selection import get_items
    return encode_items(get_items(doc), doc)


def _plural(count):
    return 'item' if count == 1 else 'items'


def memorize(doc=None):
    """memory := current selection."""
    doc = _doc(doc)
    entries = _selected_entries(doc)
    if not entries:
        return Result('error', 'Nothing selected', 'Select something to memorize first.')

    state = _state(doc)
    save(apply_entries(state, entries))
    return Result('success', 'Memorized %d %s' % (len(entries), _plural(len(entries))),
                  count=len(entries))


def recall(doc=None):
    """current selection := memory."""
    from pynavis.selection import set_items
    doc = _doc(doc)
    state = _state(doc)
    entries = state['items']
    if not entries:
        return Result('error', 'Memory is empty', 'Press Remember to fill it.')

    items, missing, missing_models = resolve_entries(entries, doc)
    if not items:
        return Result('error',
                      'None of the %d memorized %s are in this model'
                      % (len(entries), _plural(len(entries))),
                      'Memorized from %s.' % (state.get('document') or 'another document'))

    set_items(items, doc)
    if missing:
        return Result('info', 'Recalled %d of %d' % (len(items), len(entries)),
                      '%d not found in %s.' % (missing, ', '.join(missing_models)),
                      count=len(items))
    return Result('success', 'Recalled %d %s' % (len(items), _plural(len(items))),
                  count=len(items))


def _combine(doc, operation, verb):
    doc = _doc(doc)
    selected = _selected_entries(doc)
    if not selected:
        return Result('error', 'Nothing selected', 'Select something to %s.' % verb)

    state = _state(doc)
    before = len(state['items'])
    combined = operation(state['items'], selected)
    save(apply_entries(state, combined))

    delta = len(combined) - before
    sign = '+' if delta >= 0 else ''
    if not combined:
        return Result('warning', 'Memory is now empty',
                      'That %s left nothing behind.' % verb)
    return Result('success',
                  'Memory: %d %s (%s%d)' % (len(combined), _plural(len(combined)), sign, delta),
                  count=len(combined))


def add(doc=None):
    """memory := memory + selection."""
    return _combine(doc, union, 'add')


def subtract(doc=None):
    """memory := memory - selection."""
    return _combine(doc, difference, 'subtract')


def intersect(doc=None):
    """memory := memory and selection."""
    return _combine(doc, intersection, 'intersect')


def clear(doc=None):
    """memory := empty."""
    doc = _doc(doc)
    state = _state(doc)
    count = len(state['items'])
    save(apply_entries(state, []))
    return Result('success', 'Memory cleared',
                  '%d %s dropped.' % (count, _plural(count)) if count else '')


# ---- stepping through the memory one item at a time ----------------------

MAX_CONTENTS_ROWS = 500


def _step(doc, delta):
    from pynavis.selection import set_items
    doc = _doc(doc)
    state = _state(doc)
    entries = state['items']
    if not entries:
        return Result('error', 'Memory is empty', 'Press Remember to fill it.')

    # Only entries that actually resolve are worth stepping onto.
    live = []
    for entry in entries:
        items, _missing, _models = resolve_entries([entry], doc)
        live.append(items[0] if items else None)

    found = [i for i, item in enumerate(live) if item is not None]
    if not found:
        return Result('error',
                      'None of the %d memorized %s are in this model'
                      % (len(entries), _plural(len(entries))),
                      'Memorized from %s.' % (state.get('document') or 'another document'))

    cursor = state.get('cursor', -1)
    # Walk in the requested direction until the next resolvable entry.
    for _ in range(len(entries)):
        cursor = step(cursor, len(entries), delta)
        if live[cursor] is not None:
            break

    item = live[cursor]
    set_items([item], doc)
    _zoom_to_selection()

    state['cursor'] = cursor
    save(state)

    position = found.index(cursor) + 1
    return Result('info', 'Item %d of %d' % (position, len(found)),
                  _display_name(item), count=len(found))


def _display_name(item):
    try:
        return item.DisplayName or '(unnamed)'
    except Exception:
        return ''


def _zoom_to_selection():
    """Flies the camera to the current selection. Best effort: a failed zoom
    must never lose the step."""
    try:
        from pynavis import _com
        _com.get_state().ZoomInCurViewOnCurSel()
    except Exception:
        pass


def step_next(doc=None):
    """Selects and zooms to the next item in memory, wrapping at the end."""
    return _step(doc, 1)


def step_prev(doc=None):
    """Selects and zooms to the previous item in memory, wrapping at the start."""
    return _step(doc, -1)


# ---- inspecting, promoting, purging --------------------------------------


def contents(doc=None):
    """Rows for the Show tool: (model name, display name, link html or None)."""
    doc = _doc(doc)
    state = _state(doc)
    rows = []
    for entry in state['items'][:MAX_CONTENTS_ROWS]:
        items, _missing, _models = resolve_entries([entry], doc)
        if items:
            from pynavis.output import element_link
            name = _display_name(items[0])
            rows.append((entry.get('m', ''), name, element_link(items[0], name)))
        else:
            rows.append((entry.get('m', ''), '', None))
    return state, rows


def contents_html(rows, state):
    """The Show table. Hand-built because output.table_html escapes link markup."""
    from pynavis._markdown import escape

    header = ('<p class="muted">Memorized from %s at %s</p>'
              % (escape(state.get('document') or 'an unsaved document'),
                 escape(state.get('saved') or 'an unknown time')))

    body = []
    for model, name, link in rows:
        if link:
            body.append('<tr><td>%s</td><td>%s</td></tr>' % (escape(model), link))
        else:
            body.append('<tr><td class="muted">%s</td>'
                        '<td class="muted">not in this model</td></tr>' % escape(model))

    total = len(state['items'])
    footer = ''
    if total > len(rows):
        footer = '<p class="muted">showing %d of %d</p>' % (len(rows), total)

    return ('%s<table class="pynavis"><thead><tr><th>Model</th><th>Item</th></tr></thead>'
            '<tbody>%s</tbody></table>%s' % (header, ''.join(body), footer))


def save_as_set(name, doc=None):
    """Promotes the memory to a native Navisworks selection set."""
    from pynavis._api import Api
    doc = _doc(doc)
    state = _state(doc)
    if not state['items']:
        return Result('error', 'Memory is empty', 'Nothing to save as a set.')

    items, missing, _models = resolve_entries(state['items'], doc)
    if not items:
        return Result('error', 'None of the memorized items are in this model')

    collection = Api.ModelItemCollection()
    for item in items:
        collection.Add(item)

    selection_set = Api.SelectionSet(collection)
    selection_set.DisplayName = name
    doc.SelectionSets.AddCopy(selection_set)

    if missing:
        detail = '%d of %d items; %d not found.' % (len(items), len(state['items']), missing)
    else:
        detail = '%d %s.' % (len(items), _plural(len(items)))
    return Result('success', 'Saved set "%s"' % name, detail, count=len(items))


def purge_targets(root=None):
    """Memory files directly in the root. Never recursive, never anything else."""
    import re
    root = root or memory_root()
    if not os.path.isdir(root):
        return []

    pattern = re.compile(r'^.{1,40}-[0-9a-f]{8}\.json$')
    out = []
    for name in sorted(os.listdir(root)):
        path = os.path.join(root, name)
        if os.path.isfile(path) and pattern.match(name):
            out.append(path)
    return out


def purge(root=None):
    """Deletes every memory file, for every document."""
    targets = purge_targets(root)
    if not targets:
        return Result('info', 'No memory files to purge')

    removed, failed = 0, []
    for path in targets:
        try:
            os.remove(path)
            removed += 1
        except Exception:
            failed.append(os.path.basename(path))

    if failed:
        return Result('warning', 'Purged %d of %d memory files' % (removed, len(targets)),
                      'Could not delete: %s' % ', '.join(failed), count=removed)
    return Result('success', 'Purged %d memory %s'
                  % (removed, 'file' if removed == 1 else 'files'), count=removed)
