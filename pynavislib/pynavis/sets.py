# -*- coding: utf-8 -*-
"""Selection sets: read the saved-search tree, compile pynavis._query queries
into Navisworks Search objects, run them against a document, and write
search/selection sets and folders back into the saved-sets tree.

The pure half (query, _variant_kind, _split_path, _dedupe_name, _plan_folders,
_validate_import) has no Navisworks dependency and runs anywhere; the API
half (compile_search, run, find_first, snapshot, find, items_of, select, create, update,
rename, delete, create_folder, to_dict, export_all, import_dict) lazy-imports
pynavis._api / pynavis.doc inside each function, same as viewpoints.py, so
importing this module never needs a live Navisworks session.

WRITE LAYER TRAP: a Navisworks SelectionSet holds exactly ONE
SearchConditionCollection - there is no OR at the SelectionSet level, only
inside compile_search/run where OR groups become several separate Search
objects unioned in python. sets.create/update therefore accept only a
single-OR-group query for a search set (the FIRST and only group's Search)
and raise ValueError, naming the group count, for anything else - split a
multi-group query into one set per group instead.

All mutations (create, update, rename, delete, create_folder, import_dict)
wrap in viewpoints.transaction('pyNavis sets', doc) so a bulk edit lands as
ONE undo step; transaction() itself is a no-op wrapper when called again
inside an already-open transaction (see its docstring), so create_folder and
create nesting inside import_dict's own transaction is safe.

DocumentSelectionSets has no documented EditDisplayName (unlike
SavedViewpoints) or ReplaceWithCopy (unlike DocumentClashTests), so rename
and update go through the closest verified pair instead: CreateCopy the item,
mutate the copy, RemoveAt the old item, InsertCopy the copy back at the same
index (see _replace_saved_item). VERIFY at smoke: InsertCopy's exact
signature was not in the reflected fact list this module was written
against; adjust _replace_saved_item if it does not match (GroupItem, int,
SavedItem) for a nested parent / (int, SavedItem) at root.

COPY-NOT-MOVE: AddCopy and InsertCopy both COPY the SavedItem handed to
them, so the object this module builds (Api.SelectionSet(...), CreateCopy())
never joins the tree itself and has no Parent - feeding it back to
rename/update/delete would raise LookupError inside _index_in. create,
update and rename therefore re-resolve the item that actually landed in the
tree and return THAT (_resolve_added_child's guid-diff after an add,
_replace_saved_item's read-back at the swapped-in index), so the documented
chain s = sets.create(...); sets.rename(s, 'Other') works for a foldered set
as well as a root one.

GUIDS SURVIVE rename() and update(): _replace_saved_item restores the
original item's Guid onto the CreateCopy'd replacement before swapping it
in, since this module (like viewpoints.py) treats guids as the stable
identity that outlives an edit - export_all rows, _resolve_by_guid, and any
guid a caller stashed themselves all keep resolving to the same logical set
after a rename or update, not a fresh one. Best-effort: see
_replace_saved_item's own docstring for the CreateCopy-guid-semantics caveat
this rests on.

OR groups run as SEPARATE searches, not as one search: the .NET API ANDs
every condition inside a single SearchConditionCollection, it has no OR
concept of its own. compile_search therefore emits one Search per OR group
(Query.groups is the outer-ORed / inner-ANDed shape from _query.py), and
run() unions their FindAll results: CopyFrom seeds the union with the first
group's matches, then every later group's items are added only if not
already present (ModelItemCollection does not dedupe, and result.Contains(x)
is fine at the sizes selection sets run at - O(n*m), not O(n)).

SEARCH SCOPE: a fresh Api.Search() is scoped to NOTHING (Selection.IsClear
True), so compile_search calls Selection.SelectAll() on every Search it
builds - without it FindAll returns 0 for every predicate, however broad.
See compile_search's own docstring for the measurement.

Query.locations is a v1 stub (only 'default' is defined by _query.py), so
compile_search leaves Search.Locations at its own default (DescendantsAndSelf,
field-checked) rather than mapping anything onto it. Query.prune
sets PruneBelowMatch on every compiled search, and that assignment is not
redundant: the API's own default is True, so a query that does not ask for
pruning only gets an unpruned search because compile_search writes the False
back. run()'s own prune=True kwarg forces it on regardless of what the query
already carries (it never turns pruning off).

Snapshot rows mirror viewpoints.snapshot: {'guid', 'key' (index path like
'0/2/1'), 'parent_key', 'name', 'depth', 'is_folder', 'has_search'}, walking
doc.SelectionSets.RootItem depth-first, in tree order.

Condition compile table (by_display=True, the Condition default, uses the
*ByDisplayName factories; by_display=False switches to the *ByName ones):

    equals            HasPropertyByDisplayName(cat, prop).EqualValue(variant(value))
    not_equals        HasPropertyByDisplayName(cat, prop).CompareWith(NotEqual, variant(value))
    contains          HasPropertyByDisplayName(cat, prop).DisplayStringContains(str(value))
    wildcard          HasPropertyByDisplayName(cat, prop).DisplayStringWildcard(str(value))
    gt / ge / lt / le HasPropertyByDisplayName(cat, prop).CompareWith(NumericGreaterThan /
                       NumericGreaterThanOrEqual / NumericLessThan / NumericLessThanOrEqual,
                       variant(float(value)))
    has_property      HasPropertyByDisplayName(cat, prop), bare
    not_has_property  HasPropertyByDisplayName(cat, prop).CompareWith(NotHasProperty, VariantData())
    has_category      HasCategoryByDisplayName(cat)

ignore_case appends .IgnoreStringValueCase() to whatever chain the op built.
variant(value): bool -> FromBoolean, int -> FromInt32, float -> FromDouble,
else -> FromDisplayString(str(value)) - see _variant_kind, which decides the
bucket (bool is checked before int since Python's bool subclasses int).
"""

from pynavis import _query


def query():
    """A new fluent query builder - see _query.Builder."""
    return _query.Builder()


def _variant_kind(value):
    """'bool' / 'int' / 'double' / 'string': which VariantData factory a plain
    Python value should be built with (variant() below).

    bool is checked FIRST - Python's bool is a subclass of int
    (isinstance(True, int) is True), so testing int before bool would send
    every True/False value down the wrong (FromInt32) factory.
    """
    if isinstance(value, bool):
        return 'bool'
    if isinstance(value, int):
        return 'int'
    if isinstance(value, float):
        return 'double'
    return 'string'


def _split_path(path):
    """Splits a 'Folder/Sub/Name' path on unescaped '/'.

    A literal slash inside one segment's name is written '\\/' (backslash
    then slash); _split_path('A\\/B') is the single segment ['A/B'], not two
    segments ['A', 'B']. Any other '/' is a path separator.
    """
    parts = []
    current = []
    i = 0
    n = len(path)
    while i < n:
        ch = path[i]
        if ch == '\\' and i + 1 < n and path[i + 1] == '/':
            current.append('/')
            i += 2
        elif ch == '/':
            parts.append(''.join(current))
            current = []
            i += 1
        else:
            current.append(ch)
            i += 1
    parts.append(''.join(current))
    return parts


def _dedupe_name(existing, name):
    """A name guaranteed not to collide with anything in existing (an
    iterable of display names), appending ' (2)', ' (3)', ... exactly like
    clashgroup._unique_names - but against a fixed existing set rather than a
    batch of names being generated together, since sets.create only ever
    needs to avoid ONE already-used name at a time."""
    existing_set = set(existing)
    if name not in existing_set:
        return name
    n = 2
    candidate = '%s (%d)' % (name, n)
    while candidate in existing_set:
        n += 1
        candidate = '%s (%d)' % (name, n)
    return candidate


def _plan_folders(existing_rows, path):
    """The folder-creation plan for a nested path like 'A/B/C' against
    existing_rows (snapshot()-shaped rows; only 'is_folder', 'parent_key',
    'name', 'key' are read).

    PURE PREVIEW ONLY: create_folder does not consult this - it re-checks the
    live tree per segment instead (see its docstring for why a plan made up
    front cannot be trusted at write time). This is here for callers that
    want to show "will create A/B, reuse A" before committing to a write.

    One step per path segment, in order: ('reuse', full_path_so_far) when a
    folder with that name already sits directly under the previous segment,
    ('create', full_path_so_far) otherwise. The moment one segment is
    missing, every segment below it is necessarily also a create - a folder
    that does not exist yet cannot already have a matching child, however
    similarly-named a folder sits elsewhere in the tree (a sibling reuse
    never applies once the plan has switched to create; see the test that
    pins this down).
    """
    segments = _split_path(path)
    steps = []
    parent_key = ''
    reusing = True
    full = ''
    for segment in segments:
        full = segment if not full else full + '/' + segment
        found_key = None
        if reusing:
            for row in existing_rows:
                if (row.get('is_folder') and row.get('parent_key') == parent_key
                        and row.get('name') == segment):
                    found_key = row.get('key')
                    break
        if found_key is not None:
            steps.append(('reuse', full))
            parent_key = found_key
        else:
            steps.append(('create', full))
            reusing = False
    return steps


def _validate_import(data):
    """Validation problems for an import_dict payload, checked entirely
    against the dict - nothing here touches the document, so import_dict can
    reject a bad payload before creating anything (a half-imported batch is
    worse than none). data must be {'sets': [{'name', 'folder' (optional),
    'query'}, ...]}.

    Rejections beyond plain _query.validate() errors: a missing/empty name;
    a missing 'query' (an 'items' marker with no query is to_dict's own
    {'items': N} output for a static/explicit set - not portable, nothing to
    reconstruct it from); any condition using the 'raw' op (to_dict's marker
    for a SearchCondition that could not be reverse-mapped - it carries only
    display text, not a value that could round-trip); and a query with
    anything other than exactly one OR group (a search set holds ONE
    condition list, the same rule sets.create() enforces at write time).

    An EMPTY AND group is rejected too, by _query.validate itself ('Group N
    has no conditions'): a group with nothing in it compiles to a Search
    with no conditions, i.e. a set that matches the WHOLE model. That is
    what a blank spreadsheet row would otherwise turn into - a
    match-everything search set written straight into the user's document -
    so it is a hard reject here rather than a silent import.
    """
    problems = []
    entries = data.get('sets')
    if not isinstance(entries, list):
        return ["'sets' must be a list"]

    for index, entry in enumerate(entries):
        label = entry.get('name') or 'entry %d' % index
        if not entry.get('name'):
            problems.append('entry %d: name is required' % index)

        if 'query' not in entry:
            if 'items' in entry:
                problems.append(
                    "'%s': a static/explicit set ({'items': N}) cannot be "
                    "imported - it has no query to reconstruct it from" % label)
            else:
                problems.append("'%s': query is required" % label)
            continue

        query_dict = entry.get('query')
        if not isinstance(query_dict, dict):
            problems.append("'%s': query must be a dict" % label)
            continue

        raw_texts = [cond.get('text', '') for group in query_dict.get('or', [])
                     for cond in group.get('and', []) if cond.get('op') == 'raw']
        for text in raw_texts:
            problems.append(
                "'%s': a condition uses the non-portable 'raw' op and cannot "
                "be imported (%s)" % (label, text))
        if raw_texts:
            continue  # from_dict/validate below would just repeat 'unknown op: raw'

        q = _query.Query.from_dict(query_dict)
        query_problems = _query.validate(q)
        if query_problems:
            problems.extend('%s: %s' % (label, p) for p in query_problems)
            continue

        if len(q.groups) != 1:
            problems.append(
                "'%s': search sets hold one condition list (one AND group) - "
                "this query has %d OR groups" % (label, len(q.groups)))

    return problems


# --- API half (Navisworks only; lazy imports keep the module pure) -----------


def compile_search(query_or_dict, doc=None, scope=None):
    """Compiles a query into a list of Autodesk.Navisworks.Api.Search, ONE per
    OR group (see the module docstring for why OR cannot collapse to a single
    Search). doc is accepted for signature symmetry with the rest of this
    module but is not currently needed to compile a Search.

    scope, if given, is an iterable of ModelItem (or a ModelItemCollection)
    the search is confined to instead of the whole model: the compiled
    Search's Selection is CopyFrom'd from it and Locations left at
    DescendantsAndSelf, so the walk covers those items and everything beneath
    them. On a large federated model this is the difference between a search
    that costs seconds and one that costs microseconds - see find_first for
    what that is good for. A scoped Search is NOT suitable for storing as a
    saved search set: it pins today's items rather than re-resolving, which is
    why create/update never pass a scope.

    Every compiled Search gets Selection.SelectAll(): a fresh Api.Search()
    carries an EMPTY selection (Selection.IsClear True), which scopes the
    search to nothing at all, so FindAll returns zero matches for any
    predicate - even one every item in the model satisfies. Field-measured:
    'Item'/'Name' has_property matched 0 without the SelectAll and
    the whole model with it. SelectAll() sets a
    selection SOURCE meaning "everything" (HasSelectionSources True) rather
    than baking in a snapshot of today's items, which is what Navisworks' own
    Find Items window scopes to and what a saved search set has to store to
    keep resolving as the model changes.
    """
    from pynavis._api import Api

    q = _coerce_query(query_or_dict)
    scope_items = None if scope is None else _to_model_item_collection(scope, Api)

    searches = []
    for group in q.groups:
        search = Api.Search()
        if scope_items is None:
            search.Selection.SelectAll()
        else:
            search.Selection.CopyFrom(scope_items)
        search.PruneBelowMatch = q.prune
        for condition in group:
            search.SearchConditions.Add(_compile_condition(condition, Api))
        searches.append(search)
    return searches


def run(query_or_dict, doc=None, prune=False, scope=None):
    """Runs a query and returns the union of every OR group's FindAll matches
    as one ModelItemCollection.

    prune=True sets PruneBelowMatch on every compiled search in addition to
    whatever the query itself already carries (Query.prune); it only ever
    turns pruning on, never off. scope confines the search to an iterable of
    ModelItem and their descendants instead of the whole model (see
    compile_search).
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    searches = compile_search(query_or_dict, d, scope)

    result = Api.ModelItemCollection()
    if not searches:
        return result

    if prune:
        for search in searches:
            search.PruneBelowMatch = True

    result.CopyFrom(searches[0].FindAll(d, False))
    for search in searches[1:]:
        for item in search.FindAll(d, False):
            if not result.Contains(item):
                result.Add(item)
    return result


def find_first(query_or_dict, doc=None, scope=None):
    """The FIRST ModelItem a query matches, or None - FindAll's short-circuiting
    counterpart, and much cheaper than run() when one hit is all a caller needs.

    FindAll always walks the whole model, because it cannot know it has seen
    the last match; FindFirst stops the walk at the first hit. Field-measured
    on a federated model of 1.75M id-carrying items: 3.0s per
    FindAll against a mean of 0.80s (worst 1.12s) per FindFirst, a ~3.7x
    saving that grows with how early in the walk the item sits.

    OR groups are tried in order and the first group that hits wins, so this
    is "the first match of the first group that matches", not "the earliest
    match across all groups" - with one group (the usual case) the two are the
    same thing. scope confines the walk (see compile_search).
    """
    from pynavis import doc as _doc

    d = doc if doc is not None else _doc.get_doc()
    for search in compile_search(query_or_dict, d, scope):
        found = search.FindFirst(d, False)
        if found is not None:
            return found
    return None


def snapshot(doc=None):
    """Snapshot rows for the document's selection-sets tree, in tree order.

    Row: {'guid', 'key' (index path e.g. '0/2/1'), 'parent_key' ('' at root),
    'name', 'depth', 'is_folder', 'has_search' (True for a search-based
    SelectionSet, False for an explicit-items set or a folder)}.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    rows = []

    def walk(children, parent_key, depth):
        for index, item in enumerate(children):
            key = parent_key + '/' + str(index) if parent_key else str(index)
            is_folder = isinstance(item, Api.FolderItem)
            has_search = (not is_folder) and bool(getattr(item, 'HasSearch', False))
            rows.append({
                'guid': str(item.Guid),
                'key': key,
                'parent_key': parent_key,
                'name': item.DisplayName,
                'depth': depth,
                'is_folder': is_folder,
                'has_search': has_search,
            })
            if is_folder:
                walk(item.Children, key, depth + 1)

    walk(d.SelectionSets.RootItem.Children, '', 0)
    return rows


def find(name_or_path, doc=None):
    """The SavedItem (SelectionSet or FolderItem) at a path, or None.

    A path with no '/' (after unescaping - see _split_path) is a bare name:
    the first match in depth-first tree order, wherever it sits. A path with
    '/' walks each named folder in turn ('Folder/Sub/Name') and matches the
    last segment among that folder's direct children only.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    parts = _split_path(name_or_path)
    root = d.SelectionSets.RootItem

    if len(parts) == 1:
        return _find_by_name(root.Children, parts[0], Api)

    node = root
    for part in parts[:-1]:
        node = _child_folder(node.Children, part, Api)
        if node is None:
            return None
    for item in node.Children:
        if item.DisplayName == parts[-1]:
            return item
    return None


def items_of(saved_set, doc=None):
    """The ModelItemCollection a saved set resolves to.

    Raises ValueError for a folder item - a folder has no selected items of
    its own, only children (walk snapshot()/the tree to enumerate those).
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    if isinstance(saved_set, Api.FolderItem):
        raise ValueError('cannot get items of a folder: %r' % saved_set.DisplayName)
    d = doc if doc is not None else _doc.get_doc()
    return saved_set.GetSelectedItems(d)


def select(saved_set_or_items, doc=None):
    """Sets the document's current selection to a saved set or a
    ModelItemCollection (e.g. from run())."""
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    items = (items_of(saved_set_or_items, d)
             if isinstance(saved_set_or_items, Api.SavedItem)
             else saved_set_or_items)
    d.CurrentSelection.CopyFrom(items)


def create(name, source, folder=None, doc=None):
    """Creates a new SelectionSet under folder ('/'-separated, missing
    segments created - see create_folder) or the root (folder=None), and
    returns the LIVE, TREE-RESIDENT item, not the detached copy that was
    handed to AddCopy.

    That distinction matters: AddCopy copies rather than moves, so the
    SelectionSet object built here never joins the tree itself and has no
    Parent - passing it straight back to rename()/update()/delete() would
    raise LookupError inside _index_in. The new item is therefore re-resolved
    out of the target container after the add (guid-diff, exactly like
    clashtest._resolve_added_test, falling back to a name match) so the
    documented chain s = sets.create(...); sets.rename(s, ...) works.

    source is either a query (Query, Builder, or dict) - compiled to a
    search set backed by the query's single OR group's Search, raising
    ValueError for anything but exactly one group (see the module
    docstring's WRITE LAYER TRAP) - or an iterable of ModelItem / a
    ModelItemCollection, which becomes a static/explicit set instead.

    name is deduped against every existing set/folder name in the document
    (not just the target folder's siblings: find()'s bare-name lookup
    matches the first hit anywhere in the tree, so a name that collides
    ANYWHERE is ambiguous for later lookups) - see _dedupe_name.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    container = d.SelectionSets
    final_name = _dedupe_name([row['name'] for row in snapshot(d)], name)

    if _is_query_like(source):
        saved_item = Api.SelectionSet(_single_group_search(source, d))
    else:
        saved_item = Api.SelectionSet(_to_model_item_collection(source, Api))
    saved_item.DisplayName = final_name

    from pynavis import viewpoints
    with viewpoints.transaction('pyNavis sets', d):
        group = create_folder(folder, d) if folder else None
        parent = group if group is not None else container.RootItem
        before = set(str(child.Guid) for child in parent.Children)
        if group is None:
            container.AddCopy(saved_item)
        else:
            container.AddCopy(group, saved_item)
        added = _resolve_added_child(parent, before, final_name)
    return added if added is not None else saved_item


def update(name_or_item, source, doc=None):
    """Replaces an existing set's content in place (same name, same
    position, same Guid - see _replace_saved_item): CreateCopy the item,
    CopyFrom the new search/items onto the copy, then swap it in via
    _replace_saved_item - mirrors viewpoints.apply_renames' resolve-then-edit
    shape, but through a CreateCopy since DocumentSelectionSets has no
    single-call edit method (see the module docstring).

    Returns the LIVE, TREE-RESIDENT item that ended up at the old item's
    position, not the detached CreateCopy that InsertCopy copied from (see
    create()'s docstring for why that distinction matters), so the returned
    value can be passed straight on to rename()/update()/delete().

    source follows create()'s rule: a query (single OR group only) or an
    iterable of ModelItem/a ModelItemCollection.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    item = _resolve_saved_item(name_or_item, d)
    if item is None:
        raise ValueError('no such set: %r' % (name_or_item,))
    if isinstance(item, Api.FolderItem):
        raise ValueError('cannot update a folder: %r' % item.DisplayName)

    new_copy = item.CreateCopy()
    if _is_query_like(source):
        new_copy.CopyFrom(_single_group_search(source, d))
    else:
        new_copy.CopyFrom(_to_model_item_collection(source, Api))

    from pynavis import viewpoints
    with viewpoints.transaction('pyNavis sets', d):
        resident = _replace_saved_item(d.SelectionSets, item, new_copy)
    return resident if resident is not None else new_copy


def rename(name_or_item, new_name, doc=None):
    """Renames a set or folder in place (same position in the tree, same
    Guid - see _replace_saved_item), returning the LIVE, TREE-RESIDENT
    renamed item (same reasoning as create()/update())."""
    from pynavis import doc as _doc

    d = doc if doc is not None else _doc.get_doc()
    item = _resolve_saved_item(name_or_item, d)
    if item is None:
        raise ValueError('no such set or folder: %r' % (name_or_item,))

    new_copy = item.CreateCopy()
    new_copy.DisplayName = new_name

    from pynavis import viewpoints
    with viewpoints.transaction('pyNavis sets', d):
        resident = _replace_saved_item(d.SelectionSets, item, new_copy)
    return resident if resident is not None else new_copy


def delete(name_or_item, doc=None):
    """Deletes a set or folder (and, for a folder, everything under it)."""
    from pynavis import doc as _doc

    d = doc if doc is not None else _doc.get_doc()
    item = _resolve_saved_item(name_or_item, d)
    if item is None:
        raise ValueError('no such set or folder: %r' % (name_or_item,))

    container = d.SelectionSets
    parent = getattr(item, 'Parent', None)
    group = parent if parent is not None else container.RootItem

    from pynavis import viewpoints
    with viewpoints.transaction('pyNavis sets', d):
        container.Remove(group, item)


def create_folder(path, doc=None):
    """Creates (or reuses) every folder along a '/'-separated path, nested,
    returning the deepest (leaf) FolderItem.

    Every segment is looked up against the LIVE tree (_child_folder on the
    current parent's Children) immediately before it would be created, with
    no plan consulted at all: a stale plan is exactly how duplicate folders
    appear when something else created the same folder in between - another
    create_folder call earlier inside the same import_dict transaction, say,
    whose additions a snapshot taken up front would not know about.
    _plan_folders stays as the pure preview of the same walk (what a caller
    can show a user before writing), but it is not what decides here.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    container = d.SelectionSets
    segments = _split_path(path)

    from pynavis import viewpoints
    with viewpoints.transaction('pyNavis sets', d):
        at_root = True
        parent_group = container.RootItem
        for segment in segments:
            found = _child_folder(parent_group.Children, segment, Api)
            if found is None:
                new_folder = Api.FolderItem()
                new_folder.DisplayName = segment
                if at_root:
                    container.AddCopy(new_folder)
                else:
                    container.AddCopy(parent_group, new_folder)
                found = _child_folder(parent_group.Children, segment, Api)
            parent_group = found
            at_root = False
    return parent_group


def to_dict(saved_item, doc=None):
    """A search set's query as a dict ({'or': [{'and': [condition, ...]}]},
    the _query.Query.to_dict() shape - always exactly one group, since a
    SelectionSet's Search holds one AND'd SearchConditionCollection); a
    static/explicit set's dict is {'items': N} instead - a count, not a
    query, and NOT portable (import_dict rejects it - see _validate_import).

    Conditions that cannot be reverse-mapped (see _condition_to_dict) become
    {'op': 'raw', 'text': condition.ToString()} inside the query - visible
    in an export, but import_dict rejects them too.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api

    d = doc if doc is not None else _doc.get_doc()
    if isinstance(saved_item, Api.FolderItem):
        raise ValueError('cannot convert a folder to a dict: %r' % saved_item.DisplayName)

    if getattr(saved_item, 'HasSearch', False):
        conditions = [_condition_to_dict(c) for c in saved_item.Search.SearchConditions]
        return {'or': [{'and': conditions}]}

    try:
        count = len(saved_item.GetSelectedItems(d))
    except Exception:
        count = 0
    return {'items': count}


def export_all(doc=None, include_static=False):
    """Every SEARCH set in the document as {'sets': [{'name', 'folder',
    'query'}, ...]} - directly re-importable, since export_all(...) feeding
    import_dict(...) is the whole point of the pair. folder is a
    '/'-separated path, or None at the root.

    Static/explicit sets are LEFT OUT by default: to_dict renders one as
    {'items': N}, a count with nothing to rebuild the set from, and
    import_dict rejects exactly that (see _validate_import). Passing
    include_static=True adds those {'name', 'folder', 'items'} entries back
    for callers that want to SHOW every set - a spreadsheet export listing
    the whole tree, say - accepting that the result can no longer be handed
    straight back to import_dict.
    """
    from pynavis import doc as _doc

    d = doc if doc is not None else _doc.get_doc()
    container = d.SelectionSets
    rows = snapshot(d)
    folder_names = dict((row['key'], row['name']) for row in rows if row['is_folder'])
    parent_of = dict((row['key'], row['parent_key']) for row in rows)

    def folder_path(row):
        parts = []
        key = row['parent_key']
        while key:
            parts.append(folder_names.get(key, ''))
            key = parent_of.get(key, '')
        parts.reverse()
        return '/'.join(parts) if parts else None

    entries = []
    for row in rows:
        if row['is_folder']:
            continue
        item = _resolve_by_guid(container, row['guid'])
        if item is None:
            continue
        entry = {'name': row['name'], 'folder': folder_path(row)}
        data = to_dict(item, d)
        if 'items' in data:
            if not include_static:
                continue
            entry['items'] = data['items']
        else:
            entry['query'] = data
        entries.append(entry)
    return {'sets': entries}


def import_dict(data, doc=None, replace=False, progress=None):
    """Batch-creates sets from export_all's {'sets': [...]} shape, all
    inside ONE transaction. Validated with _validate_import BEFORE anything
    touches the document - a bad entry anywhere aborts the whole import,
    never leaving a half-applied batch. progress(done, total), if given, is
    called after each entry.

    replace=True updates an existing set with the same name in place
    (via update()) instead of creating a deduped-name sibling; replace=False
    (the default) always creates, letting create()'s _dedupe_name keep the
    original around under a '(2)' name.

    Returns (created_count, errors) - entries that raise inside the
    transaction (e.g. a set removed from under the import between validation
    and apply) are recorded in errors and skipped, not fatal to the rest of
    the batch.
    """
    from pynavis import doc as _doc

    problems = _validate_import(data)
    if problems:
        raise ValueError('invalid import data: ' + '; '.join(problems))

    d = doc if doc is not None else _doc.get_doc()
    entries = data.get('sets', [])

    from pynavis import viewpoints
    created = 0
    errors = []
    with viewpoints.transaction('pyNavis sets', d):
        for index, entry in enumerate(entries):
            name = entry['name']
            folder = entry.get('folder')
            query = entry['query']
            try:
                if replace:
                    path = (folder + '/' + name) if folder else name
                    existing = find(path, d)
                    if existing is not None:
                        update(existing, query, d)
                    else:
                        create(name, query, folder, d)
                else:
                    create(name, query, folder, d)
                created += 1
            except Exception as error:
                errors.append("'%s': %s" % (name, error))
            if progress is not None:
                progress(index + 1, len(entries))
    return created, errors


# --- internal helpers ---------------------------------------------------------


def _coerce_query(query_or_dict):
    """A validated _query.Query from a Query, a Builder, or a dict."""
    if isinstance(query_or_dict, _query.Query):
        return query_or_dict
    if isinstance(query_or_dict, _query.Builder):
        return query_or_dict.build()
    if isinstance(query_or_dict, dict):
        q = _query.Query.from_dict(query_or_dict)
        problems = _query.validate(q)
        if problems:
            raise ValueError('invalid query: ' + '; '.join(problems))
        return q
    raise TypeError('query_or_dict must be a Query, Builder, or dict')


def _single_group_search(source, doc):
    """The ONE compiled Search a SelectionSet can hold, from a query source
    (Query, Builder, or dict) - the shared half of create() and update(),
    which apply exactly the same rule.

    Raises ValueError naming the group count for anything but exactly one OR
    group: a SelectionSet has a single SearchConditionCollection and no OR of
    its own (see the module docstring's WRITE LAYER TRAP), so there is no
    honest way to store a wider query as one set.
    """
    q = _coerce_query(source)
    if len(q.groups) != 1:
        raise ValueError(
            'search sets hold one condition list (one AND group): this '
            'query has %d OR groups - create one set per group or narrow '
            'the query to a single group' % len(q.groups))
    return compile_search(q, doc)[0]


def _is_query_like(source):
    """True for anything create()/update() should compile as a search
    (Query, Builder, dict) rather than treat as an items collection."""
    return isinstance(source, (_query.Query, _query.Builder, dict))


def _to_model_item_collection(source, Api):
    """A ModelItemCollection from a ModelItemCollection (passed through) or
    any other iterable of ModelItem."""
    if isinstance(source, Api.ModelItemCollection):
        return source
    collection = Api.ModelItemCollection()
    for item in source:
        collection.Add(item)
    return collection


def _property_condition(condition, Api):
    if condition.by_display:
        return Api.SearchCondition.HasPropertyByDisplayName(condition.category, condition.prop)
    return Api.SearchCondition.HasPropertyByName(condition.category, condition.prop)


def _category_condition(condition, Api):
    if condition.by_display:
        return Api.SearchCondition.HasCategoryByDisplayName(condition.category)
    return Api.SearchCondition.HasCategoryByName(condition.category)


def _variant(value, Api):
    kind = _variant_kind(value)
    if kind == 'bool':
        return Api.VariantData.FromBoolean(value)
    if kind == 'int':
        return Api.VariantData.FromInt32(value)
    if kind == 'double':
        return Api.VariantData.FromDouble(value)
    return Api.VariantData.FromDisplayString(str(value))


# See the module docstring's condition compile table (op -> API chain).
def _compile_condition(condition, Api):
    if condition.op == 'has_category':
        result = _category_condition(condition, Api)
    else:
        base = _property_condition(condition, Api)
        if condition.op == 'equals':
            result = base.EqualValue(_variant(condition.value, Api))
        elif condition.op == 'not_equals':
            result = base.CompareWith(
                Api.SearchConditionComparison.NotEqual, _variant(condition.value, Api))
        elif condition.op == 'contains':
            result = base.DisplayStringContains(str(condition.value))
        elif condition.op == 'wildcard':
            result = base.DisplayStringWildcard(str(condition.value))
        elif condition.op == 'gt':
            result = base.CompareWith(
                Api.SearchConditionComparison.NumericGreaterThan,
                _variant(float(condition.value), Api))
        elif condition.op == 'ge':
            result = base.CompareWith(
                Api.SearchConditionComparison.NumericGreaterThanOrEqual,
                _variant(float(condition.value), Api))
        elif condition.op == 'lt':
            result = base.CompareWith(
                Api.SearchConditionComparison.NumericLessThan,
                _variant(float(condition.value), Api))
        elif condition.op == 'le':
            result = base.CompareWith(
                Api.SearchConditionComparison.NumericLessThanOrEqual,
                _variant(float(condition.value), Api))
        elif condition.op == 'has_property':
            result = base
        elif condition.op == 'not_has_property':
            # VERIFY at smoke: if SearchConditionComparison.NotHasProperty does
            # not behave via CompareWith on a HasPropertyBy* base, build via the
            # 5-arg SearchCondition ctor with that comparison instead.
            result = base.CompareWith(
                Api.SearchConditionComparison.NotHasProperty, Api.VariantData())
        else:
            raise ValueError('unknown op: %r' % condition.op)

    if condition.ignore_case:
        result = result.IgnoreStringValueCase()
    return result


def _find_by_name(children, name, Api):
    """First match in depth-first pre-order, same walk order as
    _util.flatten/viewpoints.snapshot: each item is checked, and if it is a
    folder its whole subtree is searched before moving on to the next
    sibling."""
    for item in children:
        if item.DisplayName == name:
            return item
        if isinstance(item, Api.FolderItem):
            found = _find_by_name(item.Children, name, Api)
            if found is not None:
                return found
    return None


def _child_folder(children, name, Api):
    for item in children:
        if isinstance(item, Api.FolderItem) and item.DisplayName == name:
            return item
    return None


def _resolve_added_child(parent, before_guids, name):
    """The tree-resident child AddCopy just created under parent: the one
    direct child whose Guid was not in before_guids, exactly the guid-diff
    clashtest._resolve_added_test does for its own copy-not-move add.

    Falls back to a direct child with the expected display name (the name
    create() already deduped, so it is unique) if no guid is new - which
    would mean AddCopy kept the copy's guid rather than minting a fresh one -
    and to None if even that misses, leaving the caller to decide (create()
    hands back the detached copy in that case rather than raising: a set that
    was created but cannot be re-resolved is still created).
    """
    for child in parent.Children:
        if str(child.Guid) not in before_guids:
            return child
    for child in parent.Children:
        if child.DisplayName == name:
            return child
    return None


def _resolve_saved_item(name_or_item, doc):
    """A live SavedItem from either a name/path string (via find()) or a
    SavedItem passed straight through - update/rename/delete accept either,
    same convenience the rest of pynavis gives its "a saved thing or its
    name" parameters."""
    from pynavis._api import Api

    if isinstance(name_or_item, Api.SavedItem):
        return name_or_item
    return find(name_or_item, doc)


def _index_in(parent, item):
    for index, child in enumerate(parent.Children):
        if child.Guid == item.Guid:
            return index
    raise LookupError('item is not under its recorded parent')


def _replace_saved_item(container, old_item, new_item):
    """Swaps old_item for new_item in the tree at the SAME position and
    returns the TREE-RESIDENT item now sitting there (None if it cannot be
    read back) - InsertCopy copies rather than moves, so new_item itself
    never joins the tree and would raise LookupError in _index_in if a
    caller passed it back in. See the module docstring's write-layer trap:
    DocumentSelectionSets has
    neither SavedViewpoints.EditDisplayName nor
    DocumentClashTests.TestsReplaceWithCopy, so this uses the two verified
    members closest to a single-call swap (RemoveAt then InsertCopy) instead.

    Also restores old_item's Guid onto new_item before the swap, so this
    module's (and viewpoints.py's) guid-is-identity model holds across a
    rename/update: any guid held elsewhere (export_all rows,
    _resolve_by_guid, a caller's own bookmark) still resolves to the same
    logical set afterwards. CreateCopy is assumed to hand the copy a NEW
    Guid - never verified live, hence the reassignment - and SelectionSet's
    Guid property is documented settable for exactly this. The assignment
    happens BEFORE RemoveAt: old_item is unusable (disposed) once removed,
    the same trap clash.py's _commit docstring calls out for its own tree,
    so its Guid has to be read while it is still live.

    # VERIFY at smoke: CreateCopy guid semantics, and whether a folder's own
    # children keep or lose their guids when the folder itself is
    # CreateCopy'd (this module never does that, but a future caller might).
    Guid preservation is best-effort only: if the API refuses the
    reassignment the failure is swallowed (there is no logging facility in
    this codebase to route a warning through - see the same silent-except
    convention in viewpoints.transaction's Dispose() cleanup) rather than
    aborting the rename/update, since a set that renamed correctly but under
    a fresh guid is still far better than one that did not rename at all.
    """
    parent = getattr(old_item, 'Parent', None)
    at_root = parent is None
    group = container.RootItem if at_root else parent
    index = _index_in(group, old_item)

    try:
        new_item.Guid = old_item.Guid
    except Exception:
        pass

    container.RemoveAt(group, index)
    if at_root:
        container.InsertCopy(index, new_item)
    else:
        container.InsertCopy(group, index, new_item)
    return _child_at(group, index)


def _child_at(group, index):
    """group's direct child at index, or None if it cannot be read back
    (best-effort: the callers fall back to the detached copy)."""
    try:
        return group.Children[index]
    except Exception:
        return None


def _resolve_by_guid(container, guid_text):
    from System import Guid
    try:
        return container.ResolveGuid(Guid(guid_text))
    except Exception:
        return None


# Reverse of the module docstring's condition compile table (Comparison name
# -> op string); anything not in here (None, SameType, NotHasCategory,
# DateTimeWithinDay, DateTimeWithinWeek, or an unrecognized value) falls
# through _condition_to_dict's except clause to the {'op': 'raw', ...} marker.
_COMPARISON_TO_OP = {
    'Equal': 'equals',
    'NotEqual': 'not_equals',
    'DisplayStringContains': 'contains',
    'DisplayStringWildcard': 'wildcard',
    'NumericGreaterThan': 'gt',
    'NumericGreaterThanOrEqual': 'ge',
    'NumericLessThan': 'lt',
    'NumericLessThanOrEqual': 'le',
    'HasProperty': 'has_property',
    'NotHasProperty': 'not_has_property',
    'HasCategory': 'has_category',
}

_VALUE_OPS = ('equals', 'not_equals', 'contains', 'wildcard', 'gt', 'ge', 'lt', 'le')


def _condition_to_dict(condition):
    """Reverse-maps ONE compiled SearchCondition back into the dict shape
    _query.Condition.to_dict() produces - _COMPARISON_TO_OP mirrors the
    module docstring's compile table backwards, and category/prop come off
    CategoryCombinedName.DisplayName / PropertyCombinedName.DisplayName.

    ANY failure here (an unmapped Comparison, or a missing attribute - the
    exact value-getter name on SearchCondition was not in the reflected fact
    list this module was written against; VERIFY at smoke) falls back to
    {'op': 'raw', 'text': condition.ToString()} rather than raising, so one
    unreadable condition never breaks export_all/to_dict for the rest of the
    document.
    """
    try:
        op = _COMPARISON_TO_OP.get(str(condition.Comparison))
        if op is None:
            raise ValueError('unmapped comparison: %s' % condition.Comparison)
        result = {'category': condition.CategoryCombinedName.DisplayName, 'op': op}
        if op != 'has_category':
            result['prop'] = condition.PropertyCombinedName.DisplayName
        if op in _VALUE_OPS:
            result['value'] = _condition_value(condition)
        return result
    except Exception:
        return {'op': 'raw', 'text': condition.ToString()}


def _condition_value(condition):
    """Best-effort python value out of a compiled condition's stored
    VariantData. VERIFY at smoke: the getter name was not in the reflected
    fact list this module was written against (only the fluent setters -
    EqualValue/CompareWith - were); this tries the plausible property names
    in turn and lets _condition_to_dict's except clause fall back to the raw
    marker if none of them exist."""
    for attr in ('Value', 'TestValue', 'CompareValue'):
        variant = getattr(condition, attr, None)
        if variant is not None:
            return _variant_to_python(variant)
    raise AttributeError('no known value getter on SearchCondition')


def _variant_to_python(variant):
    """A plain python value from a VariantData, matching what this module's
    OWN _variant()/_variant_kind() writes: bool, int, double, else string
    (props.py's Task 5 _variant_to_python is the general-purpose reader with
    datetime/Point3D handling this module does not need)."""
    if getattr(variant, 'IsBoolean', False):
        return bool(variant.ToBoolean())
    if getattr(variant, 'IsInt32', False):
        return int(variant.ToInt32())
    if getattr(variant, 'IsDouble', False) or getattr(variant, 'IsDoubleLength', False):
        return float(variant.ToDouble())
    return variant.ToString()
