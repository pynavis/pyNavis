"""Pure tree-layout planning for pynavis.clash.apply_plan and ungroup.

No Navisworks imports, so this unit-tests anywhere - the same split as
pynavis._clashsnap. The caller reads the live results tree into the plain
description below, asks layout() what the tree should become, and builds
exactly that on a detached copy.

Why a rebuild instead of live edits: every DocumentClashTests edit costs the
same regardless of tree size, and that cost is enormous. Measured on a
350,000-clash model (86,651 children in one test):

    TestsAddCopy    ~170 ms      35,000 group shells  =  99 minutes
    TestsMove      ~1130 ms in  350,000 clash moves   = 110 hours
                   ~3020 ms out

So one edit per group plus one per clash is about 4.6 days of frozen UI, which
is what "Apply grouping hangs Navisworks" actually was. The same restructure
built offline costs ~12 seconds: CreateCopy is 0.003 ms per result, and the
whole thing commits in a single ~150 ms TestsReplaceWithCopy.

A tree node, in live child order:

    ('group', handle, [leaf ids])  an existing ClashResultGroup
    ('leaf', id)                   a ClashResult directly under the test

`handle` is opaque here and never interpreted: the caller uses it to find the
live group again. It cannot be the display name, because sibling groups are
routinely named identically.

A group node carries every leaf id at or below it, however deeply nested. That
is deliberate: a group that survives untouched is copied whole, which preserves
nesting, comments and attributes for free, and the only paths that take a group
apart are the ones that dissolve it completely.

layout() returns nodes in final order:

    ('group', handle, [leaf ids])   a surviving existing group
    ('new', name, [leaf ids])       a group the plan asked for
    ('leaf', id)                    a result directly under the test
"""


def layout(tree, plan, keep_existing=True):
    """The tree `plan` asks for, as nodes in final child order.

    Every leaf id in `tree` comes back exactly once, which is the invariant
    that keeps a rebuild from losing or duplicating a clash.

    keep_existing True leaves existing groups alone; a plan built with
    keep_existing never claims an already-grouped clash, so they pass through
    whole. keep_existing False is flatten-and-regroup: every existing group
    dissolves, and members the plan did not claim fall back to the test root.
    """
    # id -> index of the plan group that claims it. A well-formed plan claims
    # each id at most once; the equality check below keeps a malformed one from
    # duplicating a result into two groups rather than trusting that.
    claimed = {}
    for k, group in enumerate(plan['groups']):
        for rid in group['ids']:
            if rid not in claimed:
                claimed[rid] = k

    kept = []
    loose = []      # ids that lost their group and fall back to the test root
    for node in tree:
        if node[0] == 'leaf':
            if node[1] not in claimed:
                kept.append(('leaf', node[1]))
            continue
        _, handle, ids = node
        survivors = [rid for rid in ids if rid not in claimed]
        if not keep_existing:
            # The group goes away whether or not anything survived in it.
            loose.extend(survivors)
        elif survivors:
            kept.append(('group', handle, survivors))

    new_groups = []
    for k, group in enumerate(plan['groups']):
        ids = [rid for rid in group['ids'] if claimed.get(rid) == k]
        if ids:
            new_groups.append(('new', group['name'], ids))

    # New groups land after the surviving children, matching where the old
    # live-edit path appended them; dissolved leftovers go last.
    return kept + new_groups + [('leaf', rid) for rid in loose]


def dissolve(tree, dissolving):
    """Layout for ungroup: nodes with the given handles are taken apart.

    `dissolving` is a set (or any `in`-testable) of group handles to dissolve.
    Members surface as root-level leaves where their group used to sit, so the
    tree keeps its reading order instead of piling every freed clash at the end.
    """
    nodes = []
    for node in tree:
        if node[0] == 'leaf':
            nodes.append(node)
        elif node[1] in dissolving:
            nodes.extend(('leaf', rid) for rid in node[2])
        else:
            nodes.append(node)
    return nodes
