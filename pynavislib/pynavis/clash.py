"""Clash Detective data: tests, results, summaries, and result grouping.

Reading (walk_tests/walk_results/summarize/snapshot_results) never touches the
document. Writing (apply_plan/ungroup) rebuilds the test on a detached copy and
commits it in a SINGLE TestsReplaceWithCopy.

Two traps are baked into that shape, both paid for in the field:

TestsEditTestFromCopy applies a test's DEFINITION only and silently ignores
results-tree changes - a restructured copy committed without error and nothing
happened. TestsReplaceWithCopy does carry them (verified: leaf count preserved,
every pre-existing group survived).

The per-edit methods (TestsAddCopy/TestsMove/TestsRemove) cost the same however
small the change, and that cost is ruinous. Measured on a 350,000-clash model:
TestsAddCopy ~170ms, TestsMove ~1130ms in and ~3020ms out. One edit per group
plus one per clash is roughly 4.6 days of frozen UI, which is exactly how
"Apply grouping hangs Navisworks" was reported. The rebuild is ~12 seconds.
See pynavis._clashapply for the numbers and the layout rules.
"""

import clr

from pynavis import _api, _clashapply, _clashsnap, _util
from pynavis._api import Api, Application

_api.add_reference('Autodesk.Navisworks.Clash')

import Autodesk.Navisworks.Api.Clash as ClashApi

# GetClash()/GetClearance() are C# extension methods; this binds them onto Document.
clr.ImportExtensions(ClashApi)


def get_clash(doc=None):
    """The DocumentClash part of the document."""
    d = doc if doc is not None else Application.ActiveDocument
    return d.GetClash()


def walk_tests(doc=None):
    """Yields every ClashTest in the document, flattening test folders.

    ClashTest itself derives from GroupItem (its Children are the results), so
    the test-check must run before the folder-check - see _util.flatten.
    """
    return _util.flatten(
        get_clash(doc).TestsData.Tests,
        is_leaf=lambda item: isinstance(item, ClashApi.ClashTest),
        is_group=lambda item: isinstance(item, Api.GroupItem))


def walk_results(test):
    """Yields every ClashResult of a test, flattening result groups."""
    return _util.flatten(
        test.Children,
        is_leaf=lambda item: not isinstance(item, ClashApi.ClashResultGroup),
        is_group=lambda item: isinstance(item, ClashApi.ClashResultGroup))


# Marker comment stamped on groups this module creates, so ungroup() can tell
# them apart from hand-made groups and leave the latter alone.
GROUP_MARKER = 'Created by pyNavis Smart Clash Grouper'

_EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

# Meters per model unit, keyed by the Units enum name (str(doc.Units)).
_UNITS_TO_METERS = {
    'Meters': 1.0, 'Centimeters': 0.01, 'Millimeters': 0.001,
    'Kilometers': 1000.0, 'Feet': 0.3048, 'Inches': 0.0254,
    'Yards': 0.9144, 'Miles': 1609.344, 'Micrometers': 1e-6,
    'Microinches': 2.54e-8, 'Mils': 2.54e-5,
}


class Cancelled(Exception):
    """Raised when a progress callback asks a long read to stop.

    A progress callback opts out by returning exactly False. Returning None
    (which a callback written for side effects does implicitly) means carry
    on, so the same convention is safe for apply_plan-style tick functions.
    """


def units_to_meters(doc=None):
    """Meters per model unit for the document (1.0 for unknown units)."""
    d = doc if doc is not None else Application.ActiveDocument
    return _UNITS_TO_METERS.get(str(d.Units), 1.0)


def has_grid(doc=None):
    """True when the document has an active grid system (level/grid rules work)."""
    d = doc if doc is not None else Application.ActiveDocument
    return d.Grids.ActiveSystem is not None


# One progress report per this many results. Small enough that the bar moves
# and Cancel feels instant, large enough that the callback is not the cost.
_PROGRESS_EVERY = 500


def snapshot_results(test, doc=None, progress=None):
    """Plain-dict snapshot of a test's results for pynavis.clashgroup.

    One dict per leaf ClashResult, in walk order (the id is the walk index -
    apply_plan relies on CreateCopy preserving that order). Read-only.

    progress (optional) is called every few hundred results with how many have
    been read so far; returning False raises Cancelled, leaving the caller to
    discard the partial read.

    Two memos carry the weight on big tests. Elements repeat by definition
    (grouping only works because many clashes share one element), so their
    name and model are read once per element identity. Grid intersections are
    the single most expensive call here, and neighbouring clashes resolve to
    the same one, so they are shared per small cell. A document with no grid
    system skips that lookup entirely.
    """
    d = doc if doc is not None else Application.ActiveDocument
    grid_system = d.Grids.ActiveSystem
    # A quarter-metre cell, expressed in model units. Every centre inside one
    # cell shares a single ClosestIntersection answer, so a clash within
    # 0.25m of a level or grid boundary can be attributed to the neighbouring
    # level/grid instead of the one an exact per-result lookup would name.
    # That is a deliberate accuracy-for-speed trade: the lookup dominates the
    # read on a large test, and 0.25m is well below the spacing at which a
    # level or grid label is meaningful. It moves both the level/grid rules
    # and Smart cluster names (clashgroup._area_label names a cluster from its
    # dominant grid, then level), so it is not a display-only shortcut.
    cell_size = 0.25 / units_to_meters(d)

    elements = _clashsnap.Memo(_element_cache_key, _read_element)
    grids = _clashsnap.Memo(
        lambda center: _clashsnap.cell_key(center, cell_size),
        lambda center: _read_grid(grid_system, center))

    snapshots = []

    def walk(children, group_name):
        for child in children:
            if isinstance(child, ClashApi.ClashResultGroup):
                walk(child.Children, child.DisplayName)
            elif isinstance(child, ClashApi.ClashResult):
                snapshots.append(_snapshot_one(child, len(snapshots), group_name,
                                               grid_system, elements, grids))
                # == False, not "not x": a callback written for side effects
                # returns None, and None == False is False, so it carries on.
                # Interop bools compare equal but are not always identical to
                # the Python False singleton, which rules out "is False".
                if (progress is not None
                        and len(snapshots) % _PROGRESS_EVERY == 0
                        and progress(len(snapshots)) == False):
                    raise Cancelled()

    walk(test.Children, None)
    if progress is not None and progress(len(snapshots)) == False:
        raise Cancelled()
    return snapshots


def _snapshot_one(result, index, group_name, grid_system, elements, grids):
    center = result.Center
    point = (center.X, center.Y, center.Z) if center is not None else None

    level = grid = None
    if grid_system is not None and point is not None:
        level, grid = grids.get(point)

    key_a, name_a, model_a = elements.get(_element_of(result, 'a'))
    key_b, name_b, model_b = elements.get(_element_of(result, 'b'))
    assigned = result.AssignedTo
    return {
        'id': index,
        'name': result.DisplayName,
        'center': point,
        'item_a': key_a,
        'item_b': key_b,
        'item_a_name': name_a,
        'item_b_name': name_b,
        'level': level,
        'grid': grid,
        'model_a': model_a,
        'model_b': model_b,
        'status': str(result.Status),
        'assigned': assigned.DisplayName if assigned is not None and assigned.Assigned else '',
        'group': group_name,
    }


def _read_grid(grid_system, point):
    """(level name, grid name) nearest to a point; either may be None."""
    intersection = grid_system.ClosestIntersection(
        Api.Point3D(point[0], point[1], point[2]))
    if intersection is None:
        return None, None
    level = (intersection.Level.DisplayName
             if intersection.Level is not None else None)
    return level, (intersection.DisplayName or None)


def _element_cache_key(item):
    """Cache key for an element, or None when it cannot be shared safely.

    The same guid-first identity the snapshot groups by (_item_key), not the
    raw InstanceHashCode: that is a documented 32-bit hash, and at this scale
    collisions are likely rather than theoretical (roughly 5% odds across
    20,000 distinct elements, 25% across 50,000). A collision here would hand
    the second element the first's grouping key, name and model, silently
    merging two unrelated root causes into one group - and Apply then moves
    the wrong clashes into it. One extra InstanceGuid read per side buys that
    back; the memo still saves the expensive name/model/SourceFileName work,
    which is what it exists for.
    """
    return None if item is None else _item_key(item)


def _read_element(item):
    """(stable key, display name, model file name) for one element."""
    return _item_key(item), _item_name(item), _model_name(item)


def _element_of(result, side):
    """The meaningful element on one side: the composite item when the test
    merged composites, else the first object ancestor of the geometry leaf."""
    composite = result.CompositeItem1 if side == 'a' else result.CompositeItem2
    if composite is not None:
        return composite
    leaf = result.Item1 if side == 'a' else result.Item2
    if leaf is None:
        return None
    ancestor = leaf.FindFirstObjectAncestor()
    return ancestor if ancestor is not None else leaf


def _item_key(item):
    """Stable identity for grouping: instance guid, else instance hash."""
    if item is None:
        return ''
    guid = str(item.InstanceGuid)
    return guid if guid != _EMPTY_GUID else 'h:%d' % item.InstanceHashCode


def _item_name(item):
    if item is None:
        return ''
    name = item.DisplayName
    if name:
        return name
    ancestor = item.FindFirstObjectAncestor()
    return ancestor.DisplayName if ancestor is not None and ancestor.DisplayName else ''


def _model_name(item):
    """Source file name (no path) of the model an item came from, or ''."""
    if item is None or not item.HasModel:
        return ''
    filename = item.Model.SourceFileName or item.Model.FileName or ''
    return filename.replace('\\', '/').split('/')[-1]


def count_results(test):
    """(total leaf results, how many of them sit inside a group)."""
    total = [0]
    grouped = [0]

    def walk(children, in_group):
        for child in children:
            if isinstance(child, ClashApi.ClashResultGroup):
                walk(child.Children, True)
            elif isinstance(child, ClashApi.ClashResult):
                total[0] += 1
                if in_group:
                    grouped[0] += 1

    walk(test.Children, False)
    return total[0], grouped[0]


def apply_plan(test, plan, doc=None, keep_existing=True, progress=None):
    """Applies a clashgroup plan to a test in a single document edit.

    plan ids refer to snapshot_results walk order of THIS test. With
    keep_existing False, planned leftovers land back at the test root and
    pre-existing groups dissolve. Individual clashes are never renamed; new
    groups carry GROUP_MARKER as a comment. progress (optional) is called
    with 0..1.

    The test is rebuilt on a detached copy and committed once, rather than
    edited in place - see the module docstring for why the per-edit methods
    cannot be used at any real scale.
    """
    d = doc if doc is not None else Application.ActiveDocument
    data = d.GetClash().TestsData

    tree, leaves, groups = _read_tree(test)
    nodes = _clashapply.layout(tree, plan, keep_existing)
    _commit(data, test, _build(test, nodes, leaves, groups, tree, progress))
    if progress is not None:
        progress(1.0)


def ungroup(test, doc=None, ours_only=True, progress=None):
    """Dissolves result groups; returns how many were removed.

    ours_only limits it to groups carrying GROUP_MARKER (hand-made groups
    survive); pass False to flatten every group. Nothing is renamed.
    progress (optional) is called with 0..1.

    One commit, same as apply_plan: the old per-member TestsMove loop cost
    about a second per clash, so ungrouping a large test was as unusable as
    grouping one. Only top-level groups are considered, as before; a dissolved
    group's nested subgroups dissolve with it rather than surfacing as groups.
    """
    d = doc if doc is not None else Application.ActiveDocument
    data = d.GetClash().TestsData

    tree, leaves, groups = _read_tree(test)
    dissolving = set(handle for handle in range(len(groups))
                     if not ours_only or _is_ours(groups[handle]))
    if not dissolving:
        if progress is not None:
            progress(1.0)
        return 0
    nodes = _clashapply.dissolve(tree, dissolving)
    _commit(data, test, _build(test, nodes, leaves, groups, tree, progress))
    if progress is not None:
        progress(1.0)
    return len(dissolving)


def _read_tree(test):
    """(tree, leaves, groups) describing a test's live results tree.

    tree is what _clashapply consumes, leaves[id] is the live ClashResult for
    that id, and groups[handle] is the live ClashResultGroup a ('group',
    handle, ...) node stands for. Handles are indexes, never display names:
    sibling groups are routinely named identically.

    Leaf ids are assigned in the same depth-first order snapshot_results uses,
    which is what makes a plan's ids mean the same thing here as there.
    """
    leaves = []
    groups = []
    tree = []

    def collect(node, ids):
        """Every leaf at or below node, however deeply nested."""
        for child in node.Children:
            if isinstance(child, ClashApi.ClashResultGroup):
                collect(child, ids)
            elif isinstance(child, ClashApi.ClashResult):
                ids.append(len(leaves))
                leaves.append(child)

    for child in test.Children:
        if isinstance(child, ClashApi.ClashResultGroup):
            ids = []
            collect(child, ids)
            tree.append(('group', len(groups), ids))
            groups.append(child)
        elif isinstance(child, ClashApi.ClashResult):
            tree.append(('leaf', len(leaves)))
            leaves.append(child)
    return tree, leaves, groups


def _build(test, nodes, leaves, groups, tree, progress=None):
    """A detached copy of test restructured to `nodes`. No document edits.

    Everything is copied, never moved: a SavedItemCollection owns its children,
    so Remove disposes the item it just removed ("Object has been Disposed
    (WeakRef) ... NativeHandle") and it cannot be added anywhere afterwards.
    CreateCopy costs about 0.003ms per result, so copying the whole tree is
    thousands of times cheaper than moving one clash.
    """
    sizes = dict((node[1], len(node[2])) for node in tree if node[0] == 'group')

    new_test = test.CreateCopy()
    new_test.Children.Clear()

    # Progress counts results, not nodes: one node can be a group of 5,000.
    total = 0
    for node in nodes:
        total += 1 if node[0] == 'leaf' else len(node[2])
    total = total or 1
    done = 0
    # A threshold, not `done % every == 0`: done jumps by a whole group at a
    # time, so a modulo test would skip most reports and fire irregularly.
    next_at = _PROGRESS_EVERY

    for node in nodes:
        if node[0] == 'leaf':
            new_test.Children.Add(leaves[node[1]].CreateCopy())
            done += 1
        elif node[0] == 'group':
            _, handle, ids = node
            source = groups[handle]
            if len(ids) == sizes[handle]:
                # Untouched: copying it whole keeps the nesting, comments and
                # attributes that rebuilding it from leaves would drop.
                new_test.Children.Add(source.CreateCopy())
            else:
                shell = source.CreateCopy()
                shell.Children.Clear()
                for rid in ids:
                    shell.Children.Add(leaves[rid].CreateCopy())
                new_test.Children.Add(shell)
            done += len(ids)
        else:
            _, name, ids = node
            shell = ClashApi.ClashResultGroup()
            shell.DisplayName = name
            # Body and status only: the three-argument form took the author as a
            # string until Navisworks 2026 and an Assignee from 2027 on, while
            # this overload is the same in every supported release.
            shell.Comments.Add(Api.Comment(GROUP_MARKER, Api.CommentStatus.New))
            for rid in ids:
                shell.Children.Add(leaves[rid].CreateCopy())
            new_test.Children.Add(shell)
            done += len(ids)
        if progress is not None and done >= next_at:
            progress(float(done) / total)
            next_at = done + _PROGRESS_EVERY
    return new_test


def _commit(data, test, new_test):
    """Swaps a live test for a restructured copy in one edit.

    TestsReplaceWithCopy, never TestsEditTestFromCopy: see the module
    docstring. The old `test` wrapper is dead afterwards, so read anything you
    still need from it (DisplayName included) before calling this.
    """
    parent = test.Parent
    collection = data.Tests if parent is None else parent.Children
    index = -1
    for i in range(collection.Count):
        if collection[i] is test or collection[i].Equals(test):
            index = i
            break
    if index < 0:
        raise ValueError('clash test %r is not in the tests tree'
                         % test.DisplayName)
    if parent is None:
        data.TestsReplaceWithCopy(index, new_test)
    else:
        data.TestsReplaceWithCopy(parent, index, new_test)


def _is_ours(group):
    for comment in group.Comments:
        if comment.Body == GROUP_MARKER:
            return True
    return False


def summarize(doc=None):
    """One dict per clash test: name, total result count, and counts by result
    status name (e.g. 'New', 'Active', 'Resolved').
    """
    summaries = []
    for test in walk_tests(doc):
        by_status = {}
        total = 0
        for result in walk_results(test):
            status = str(result.Status)
            by_status[status] = by_status.get(status, 0) + 1
            total += 1
        summaries.append({
            'name': test.DisplayName,
            'total': total,
            'by_status': by_status,
        })
    return summaries
