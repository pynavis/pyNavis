"""Copy State / Paste State: carries view state between sessions of a document.

Three kinds of state, each copied and pasted independently:
    section    the active clip plane set (planes or box), stored as JSON
    hidden     which items are hidden, stored as JSON
    overrides  permanent colour and transparency overrides, stored INSIDE the
               document as a reserved saved viewpoint (no API can read them
               item by item; see the appearance overrides section below)

The pure half (kind labels, the per-document store, descriptions) imports
nothing from Navisworks and is unit tested outside it. The API half imports
pynavis._api lazily, inside the functions that need it, so this module stays
importable anywhere.

The hidden payload reuses the memory module's entry schema
({'m': source file, 'i': model index, 'p': PathId}), so resolving it across
sessions shares memory's model-by-name fallback. Same document only: entries
resolve in another federated model exactly as far as memory entries do.
"""

import os

VERSION = 1

KINDS = ('section', 'hidden', 'overrides')

_LABELS = {
    'section': 'Section State',
    'hidden': 'Hidden Items',
    'overrides': 'Appearance Overrides',
}


class Result(object):
    """What a copy or paste did, ready for the button script to toast."""

    def __init__(self, level, message, detail='', count=0):
        self.level = level          # 'success' | 'error' | 'info' | 'warning'
        self.message = message
        self.detail = detail
        self.count = count


# ---- kinds ----------------------------------------------------------------


def label_for(kind):
    """The picker caption for a kind."""
    return _LABELS[kind]


def kind_for(label):
    """The kind behind a picker caption, or None for an unknown caption."""
    for kind in KINDS:
        if _LABELS[kind] == label:
            return kind
    return None


def available(store):
    """The kinds a loaded store actually carries, in canonical order.

    An entry only counts when it is shaped like save_kind wrote it and its
    version is this module's: a malformed or future entry is invisible, never
    an error, so a hand-edited or newer file degrades to 'nothing copied'.
    """
    out = []
    for kind in KINDS:
        entry = store.get(kind)
        if (isinstance(entry, dict) and entry.get('version') == VERSION
                and isinstance(entry.get('data'), dict)):
            out.append(kind)
    return out


def describe(kind, payload):
    """A short toast detail saying what a payload carries. User-facing text:
    sentence case throughout. Only the JSON-backed kinds have anything to
    describe; overrides are an opaque Navisworks snapshot with no counts."""
    if kind == 'section':
        if not payload.get('enabled', True):
            return 'Sectioning off'
        if payload.get('mode') == 'Box':
            return 'Section box'
        count = payload.get('active', len(payload.get('planes') or []))
        return '%d planes' % count if count else 'Sectioning off'
    if kind == 'hidden':
        count = len(payload.get('items') or [])
        if not count:
            return 'Everything visible'
        return '%d hidden %s' % (count, 'item' if count == 1 else 'items')
    return ''


def range_ok(rng):
    """Whether a stored clip range is a usable [[min x,y,z], [max x,y,z]] box.

    Navisworks represents a never-set range as an INVERTED box (min > max) and
    throws when one is assigned back, so an inverted, incomplete or malformed
    range means "carry no range", never an error.
    """
    try:
        low, high = rng[0], rng[1]
        if len(low) != 3 or len(high) != 3:
            return False
        return all(float(low[i]) <= float(high[i]) for i in range(3))
    except Exception:
        return False


# ---- storage --------------------------------------------------------------


def state_root():
    """Where viewstate files live: %APPDATA%\\pyNavis\\viewstate."""
    return os.path.join(os.environ.get('APPDATA', ''), 'pyNavis', 'viewstate')


def store_path(doc_path, root=None):
    from pynavis import _datafiles
    return _datafiles.path_for(root or state_root(), 'viewstate', doc_path)


def load_store(doc_path, root=None):
    """The whole store for a document; missing or corrupt yields {}."""
    from pynavis import _datafiles
    return _datafiles.load_all(store_path(doc_path, root))


def save_kind(doc_path, kind, payload, root=None):
    """Writes one kind's payload into the document's store, stamping the time."""
    from pynavis import _datafiles
    path = store_path(doc_path, root)
    _datafiles.store(path, kind, {
        'version': VERSION,
        'saved': _now(),
        'data': payload,
    })
    return path


def _now():
    import datetime
    return datetime.datetime.now().strftime('%Y-%m-%dT%H:%M:%S')


# ---- Navisworks-facing half ----------------------------------------------
#
# _api and _com are imported inside the functions so this module stays
# importable (and testable) outside a Navisworks session.


def _doc(doc=None):
    from pynavis.doc import get_doc
    return doc if doc is not None else get_doc()


def _doc_path(document):
    from pynavis.doc import get_filename
    return get_filename(document)


def available_kinds(doc=None):
    """The kinds copied for the active document, in canonical order. Section
    and hidden live in the JSON store; overrides live inside the document as
    the reserved saved viewpoint, so they travel with the file itself."""
    document = _doc(doc)
    stored = available(load_store(_doc_path(document)))
    out = []
    for kind in KINDS:
        if kind == 'overrides':
            if _overrides_viewpoint(document) is not None:
                out.append(kind)
        elif kind in stored:
            out.append(kind)
    return out


def copy_state(kind, doc=None):
    """Copies one kind of view state: section and hidden into the document's
    store on disk, overrides into the document itself (see _copy_overrides)."""
    document = _doc(doc)
    if kind == 'section':
        payload = _read_section(document)
        # An off section is nothing worth keeping; refusing beats storing a
        # copy whose only effect on paste is switching sectioning off.
        if not payload['enabled']:
            return Result('info', 'Nothing to copy', 'Sectioning is off.')
    elif kind == 'hidden':
        payload = _read_hidden(document)
    elif kind == 'overrides':
        return _copy_overrides(document)
    else:
        return Result('error', 'Unknown state kind', str(kind))

    save_kind(_doc_path(document), kind, payload)
    return Result('success', 'Copied ' + label_for(kind), describe(kind, payload))


def paste_state(kind, doc=None):
    """Applies one kind of copied view state back onto the active document."""
    document = _doc(doc)
    if kind == 'overrides':
        return _paste_overrides(document)

    store = load_store(_doc_path(document))
    if kind not in available(store):
        return Result('error', 'Nothing copied for ' + _LABELS.get(kind, str(kind)),
                      'Use Copy State first.')
    payload = store[kind]['data']

    if kind == 'section':
        _apply_section(document, payload)
        return Result('success', 'Pasted Section State', describe(kind, payload))
    applied, missing, missing_models = _apply_hidden(document, payload)
    return _item_result('Pasted Hidden Items', describe(kind, payload),
                        payload.get('items'), applied, missing, missing_models)


def _item_result(message, detail, entries, applied, missing, missing_models):
    """The shared toast shape for the two item-carrying pastes."""
    if entries and not applied:
        return Result('error', message.replace('Pasted', 'Could not paste'),
                      'None of the %d copied items are in this model.' % len(entries))
    if missing:
        return Result('info', message,
                      '%d not found in %s.' % (missing, ', '.join(missing_models)),
                      count=applied)
    return Result('success', message, detail, count=applied)


# ---- section state --------------------------------------------------------


def _xyz(value):
    return [value.X, value.Y, value.Z]


def _read_section(document):
    from pynavis._api import Api

    planes = document.CurrentViewpoint.CreateCopy().ClipPlanes
    payload = {
        'enabled': bool(planes.Enabled),
        'mode': 'Box' if planes.Mode == Api.ClipPlaneSetMode.Box else 'Planes',
        'linked': bool(planes.Linked),
        'planes': [],
    }
    try:
        rng = planes.Range
        recorded = [_xyz(rng.Min), _xyz(rng.Max)]
        if range_ok(recorded):
            payload['range'] = recorded
    except Exception:
        pass

    for index in range(planes.Size()):
        plane = planes.Get(index)
        payload['planes'].append({
            'index': index,
            'state': str(plane.State),
            'enabled': bool(plane.Enabled),
            'origin': _xyz(plane.Origin),
            'normal': _xyz(plane.Normal),
        })
    payload['active'] = len([p for p in payload['planes'] if p['enabled']])

    if payload['mode'] == 'Box':
        payload['box'] = _read_box(planes)
    return payload


def _read_box(planes):
    """The section box, oriented when the API will say how it is turned."""
    from pynavis._api import Api

    out = {'min': _xyz(planes.Box.Min), 'max': _xyz(planes.Box.Max), 'rotation': None}
    try:
        box = Api.BoundingBox3D()
        rotation = Api.Rotation3D()
        planes.GetOrientedBox(box, rotation)
        out['min'] = _xyz(box.Min)
        out['max'] = _xyz(box.Max)
        out['rotation'] = [rotation.A, rotation.B, rotation.C, rotation.D]
    except Exception:
        pass
    return out


def _apply_section(document, payload):
    from pynavis._api import Api

    # CreateCopy hands back a detached viewpoint; nothing reaches the view
    # until CopyFrom, which is also what makes this one undo step.
    viewpoint = document.CurrentViewpoint.CreateCopy()
    planes = viewpoint.ClipPlanes
    box_mode = payload.get('mode') == 'Box'
    planes.Mode = Api.ClipPlaneSetMode.Box if box_mode else Api.ClipPlaneSetMode.Planes
    planes.Linked = bool(payload.get('linked'))

    # range_ok also guards stores written before it existed.
    rng = payload.get('range')
    if range_ok(rng):
        planes.Range = Api.BoundingBox3D(Api.Point3D(*rng[0]), Api.Point3D(*rng[1]))

    if box_mode:
        _apply_box(planes, payload.get('box') or {})
    else:
        _apply_planes(planes, payload.get('planes') or [])

    planes.Enabled = bool(payload.get('enabled'))
    document.CurrentViewpoint.CopyFrom(viewpoint)


def _apply_planes(planes, records):
    from pynavis._api import Api

    size = planes.Size()
    for record in records:
        index = record.get('index', -1)
        if not (0 <= index < size):
            continue
        plane = planes.Get(index)
        # Custom alignment restores the exact recorded geometry; a named
        # alignment would recompute the normal from the view instead.
        plane.AlignToPick(Api.ClipPlaneAlignment.Custom,
                          Api.Point3D(*record['origin']),
                          Api.UnitVector3D(*record['normal']))
        plane.State = getattr(Api.ClipPlaneState, record.get('state', 'Default'),
                              Api.ClipPlaneState.Default)
        plane.Enabled = bool(record.get('enabled'))


def _apply_box(planes, box):
    from pynavis._api import Api

    corners = [box.get('min'), box.get('max')]
    if not range_ok(corners):
        return
    api_box = Api.BoundingBox3D(Api.Point3D(*corners[0]), Api.Point3D(*corners[1]))
    rotation = box.get('rotation')
    if rotation:
        try:
            planes.SetOrientedBox(api_box, Api.Rotation3D(*rotation))
            return
        except Exception:
            pass
    planes.Box = api_box


# ---- hidden items ---------------------------------------------------------


def _read_hidden(document):
    from pynavis import memory as _memory

    # Topmost hidden items only: a hidden parent already hides its subtree,
    # and SetHidden on the parent restores the subtree in one call.
    hidden = []
    models = document.Models
    stack = []
    for index in range(models.Count):
        stack.append(models[index].RootItem)
    while stack:
        item = stack.pop()
        if item.IsHidden:
            hidden.append(item)
        else:
            for child in item.Children:
                stack.append(child)
    return {'items': _memory.encode_items(hidden, document)}


def _apply_hidden(document, payload):
    from pynavis import memory as _memory
    from pynavis._api import Api
    from pynavis.viewpoints import transaction

    entries = payload.get('items') or []
    items, missing, missing_models = _memory.resolve_entries(entries, document)
    if entries and not items:
        return 0, missing, missing_models

    with transaction('Paste Hidden Items', document):
        document.Models.ResetAllHidden()
        if items:
            collection = Api.ModelItemCollection()
            for item in items:
                collection.Add(item)
            document.Models.SetHidden(collection, True)
    return len(items), missing, missing_models


# ---- appearance overrides -------------------------------------------------
#
# No API, COM included, exposes applied overrides item by item (measured:
# a live OverridePermanentColor leaves the COM node's
# IsOverrideMaterial False and adds no attribute). The only readable form is
# Navisworks' own snapshot: SavedViewpoints.CaptureRuntimeOverrides() bundles
# everything currently overridden into a SavedViewpoint. Copy stores that
# snapshot INSIDE the document under a reserved name, so it travels with the
# file (and needs the file saved to survive the session); paste applies it
# and restores the camera and section around the apply.

OVERRIDES_VIEWPOINT = '.pyNavis Copied Overrides'


def _overrides_viewpoint(document):
    """The reserved saved viewpoint holding the copied overrides, or None.
    Found by name anywhere in the tree, so a user filing it away is fine."""
    from pynavis.doc import walk_saved_viewpoints
    for _folders, item in walk_saved_viewpoints(document):
        try:
            if item.DisplayName == OVERRIDES_VIEWPOINT:
                return item
        except Exception:
            continue
    return None


def _copy_overrides(document):
    from pynavis.viewpoints import transaction

    snapshot = document.SavedViewpoints.CaptureRuntimeOverrides()
    if not snapshot.ContainsAppearanceOverrides:
        return Result('info', 'Nothing to copy',
                      'No appearance overrides are applied.')

    snapshot.DisplayName = OVERRIDES_VIEWPOINT
    with transaction('Copy Appearance Overrides', document):
        _remove_saved_viewpoint(document, _overrides_viewpoint(document))
        document.SavedViewpoints.AddCopy(snapshot)

    flagged = _set_materials_only(OVERRIDES_VIEWPOINT)
    detail = 'Kept as the saved viewpoint "%s". Save the file to keep it.' \
             % OVERRIDES_VIEWPOINT
    if not flagged:
        # Applying it will then also restore copy-time hidden state; say so
        # rather than pretend the copy is clean.
        return Result('warning', 'Copied Appearance Overrides',
                      detail + ' Pasting may also restore hidden items.')
    return Result('success', 'Copied Appearance Overrides', detail)


def _paste_overrides(document):
    from pynavis.viewpoints import transaction

    viewpoint = _overrides_viewpoint(document)
    if viewpoint is None:
        return Result('error', 'Nothing copied for Appearance Overrides',
                      'Use Copy State first.')

    # Applying a saved viewpoint also moves the camera and section planes;
    # snapshotting the current viewpoint first lets both be put straight back.
    held = document.CurrentViewpoint.CreateCopy()
    with transaction('Paste Appearance Overrides', document):
        document.SavedViewpoints.CurrentSavedViewpoint = viewpoint
        document.CurrentViewpoint.CopyFrom(held)
    return Result('success', 'Pasted Appearance Overrides',
                  'Colors and transparency restored.')


def _remove_saved_viewpoint(document, item):
    """Removes a saved viewpoint wherever it sits; a missing item is a no-op."""
    if item is None:
        return
    viewpoints = document.SavedViewpoints
    try:
        viewpoints.Remove(item)
    except Exception:
        viewpoints.Remove(item.Parent, item)


def _set_materials_only(name):
    """Marks the named root-level COM saved view as materials-only, so
    applying it never touches hidden state. Returns False when the view could
    not be found or the COM flags refused; the caller downgrades its toast."""
    try:
        from pynavis import _com
        for view in _com.get_state().SavedViews():
            if view.name == name:
                view.ApplyMaterialAttribs = True
                view.ApplyHideAttribs = False
                return True
    except Exception:
        pass
    return False
