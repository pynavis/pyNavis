"""Pure clash-grouping engine: rules, chaining, clustering, naming.

No Navisworks imports - operates on plain-dict snapshots built by
pynavis.clash.snapshot_results, so everything here unit-tests anywhere.

A snapshot result is a dict:
    id           int - stable index in the test's walk order
    name         str - clash display name
    center       (x, y, z) in model units, or None
    item_a       str - stable key of the element on side A ('' if unknown)
    item_b       str - same for side B
    item_a_name  str - display name of the side-A element
    item_b_name  str - same for side B
    level        str or None - nearest level display name
    grid         str or None - nearest grid intersection display name
    model_a      str - source file of the side-A element ('' if unknown)
    model_b      str - same for side B
    status       str
    assigned     str ('' when unassigned)
    group        str or None - name of the existing group holding it

plan() and smart_plan() return a dict:
    groups            [{'name': str, 'ids': [int]}] largest first
    ungrouped_ids     [int] - candidates that ended up in no group
    skipped_existing  int - results left alone because they were already grouped
    explanation       str - one line saying what was done
"""

# (id, label) pairs for the rule dropdown, in display order.
RULES = [
    ('item', 'Root-cause element (auto side)'),
    ('item_a', 'Element (side A)'),
    ('item_b', 'Element (side B)'),
    ('proximity', 'Proximity cluster'),
    ('level', 'Nearest level'),
    ('grid', 'Grid intersection'),
    ('model', 'Source model'),
    ('status', 'Status'),
    ('assigned', 'Assigned to'),
]


def plan(results, rule_ids, keep_existing=True, tolerance=6.0, min_size=1):
    """Chained-rule grouping: each rule subdivides the previous rule's groups.

    tolerance is in model units (only proximity rules use it). Groups smaller
    than min_size are left ungrouped. Every candidate lands in exactly one
    group or in ungrouped_ids - rules never drop a clash.
    """
    candidates, skipped = _candidates(results, keep_existing)
    side = _pick_side(candidates)
    partitions = [((), candidates)] if candidates else []
    for rule_id in rule_ids:
        refined = []
        for labels, subset in partitions:
            for label, bucket in _split(subset, rule_id, side, tolerance):
                refined.append((labels + (label,), bucket))
        partitions = refined

    groups, ungrouped = [], []
    for labels, subset in partitions:
        if len(subset) >= min_size and rule_ids:
            # Collapse adjacent repeats: a cluster inside "L1" may itself be
            # labeled "L1", and "L1 / L1" helps nobody.
            parts = [p for i, p in enumerate(labels)
                     if i == 0 or p != labels[i - 1]]
            groups.append((' / '.join(parts), subset))
        else:
            ungrouped.extend(subset)
    return _finish(groups, ungrouped, skipped,
                   'Grouped by %s.' % ' + '.join(_label_of(r) for r in rule_ids)
                   if rule_ids else 'No rules selected.')


def smart_plan(results, keep_existing=True, tolerance=6.0):
    """Zero-config grouping: root-cause elements, then proximity-cluster the
    leftovers. Picks the side (A/B) that collapses the most clashes into the
    fewest elements; single-clash elements fall through to area clusters;
    anything still alone stays individual.
    """
    candidates, skipped = _candidates(results, keep_existing)
    side = _pick_side(candidates)
    groups, singles = [], []
    for label, bucket in _split(candidates, 'item_' + side, side, tolerance):
        if len(bucket) >= 2:
            groups.append((label, bucket))
        else:
            singles.extend(bucket)

    clusters = 0
    ungrouped = []
    for label, bucket in _cluster(singles, tolerance):
        if len(bucket) >= 2:
            groups.append((label, bucket))
            clusters += 1
        else:
            ungrouped.extend(bucket)

    explanation = ('Grouped by the %s side: %d element group(s), '
                   '%d area cluster(s), %d left individual.'
                   % (_side_label(candidates, side), len(groups) - clusters,
                      clusters, len(ungrouped)))
    return _finish(groups, ungrouped, skipped, explanation)


# ---- shared internals -------------------------------------------------------

def _candidates(results, keep_existing):
    """(results to group, count left alone because already grouped)."""
    if not keep_existing:
        return list(results), 0
    fresh = [r for r in results if not r.get('group')]
    return fresh, len(results) - len(fresh)


def _label_of(rule_id):
    for rid, label in RULES:
        if rid == rule_id:
            return label
    return rule_id


def _pick_side(results):
    """'a' or 'b': the side whose elements collapse the clashes harder
    (fewer distinct elements = better root-cause compression)."""
    a = len(set(_item_key(r, 'a') for r in results))
    b = len(set(_item_key(r, 'b') for r in results))
    return 'b' if b < a else 'a'


def _side_label(results, side):
    """Human phrase for the picked side, e.g. "B (Structure.rvt)"."""
    models = {}
    for r in results:
        name = r.get('model_' + side) or ''
        if name:
            models[name] = models.get(name, 0) + 1
    dominant = max(models, key=lambda m: models[m]) if models else ''
    return side.upper() + (' (%s)' % dominant if dominant else '')


def _item_key(result, side):
    return (result.get('item_' + side)
            or 'name:%s' % (result.get('item_%s_name' % side) or ''))


def _split(results, rule_id, side, tolerance):
    """Applies one rule to one partition: [(label, results)], largest first."""
    if rule_id == 'proximity':
        return _cluster(results, tolerance)
    if rule_id == 'item':
        rule_id = 'item_' + side

    def keyed(r):
        if rule_id in ('item_a', 'item_b'):
            s = rule_id[-1]
            return _item_key(r, s), (r.get('item_%s_name' % s) or 'Unknown element')
        if rule_id == 'level':
            label = r.get('level') or 'No level'
        elif rule_id == 'grid':
            label = r.get('grid') or 'No grid'
        elif rule_id == 'model':
            names = sorted(set(n for n in (r.get('model_a'), r.get('model_b')) if n))
            label = ' vs '.join(names) or 'Unknown model'
        elif rule_id == 'status':
            label = r.get('status') or 'No status'
        elif rule_id == 'assigned':
            label = r.get('assigned') or 'Unassigned'
        else:
            raise ValueError('unknown rule: %r' % rule_id)
        return label, label

    buckets = {}
    for r in results:
        key, label = keyed(r)
        buckets.setdefault(key, (label, []))[1].append(r)
    return _ordered(list(buckets.values()))


def _cluster(results, tolerance):
    """Single-linkage proximity clusters via spatial hash + union-find:
    [(label, results)], largest first. Results without a center form one
    'Unlocated' bucket; labels come from the dominant grid/level."""
    located = [r for r in results if r.get('center')]
    unlocated = [r for r in results if not r.get('center')]

    parent = list(range(len(located)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(i, j):
        ri, rj = find(i), find(j)
        if ri != rj:
            parent[rj] = ri

    cell_of = {}
    tol = float(tolerance) if tolerance and tolerance > 0 else 1.0
    for i, r in enumerate(located):
        x, y, z = r['center']
        cell_of.setdefault(
            (int(x // tol), int(y // tol), int(z // tol)), []).append(i)

    tol2 = tol * tol
    for (cx, cy, cz), members in cell_of.items():
        for dx in (0, 1):
            for dy in (-1, 0, 1) if dx else (0, 1):
                for dz in (-1, 0, 1) if (dx or dy) else (0, 1):
                    others = cell_of.get((cx + dx, cy + dy, cz + dz))
                    if not others:
                        continue
                    for i in members:
                        ax, ay, az = located[i]['center']
                        for j in others:
                            if i == j:
                                continue
                            bx, by, bz = located[j]['center']
                            d2 = ((ax - bx) ** 2 + (ay - by) ** 2
                                  + (az - bz) ** 2)
                            if d2 <= tol2:
                                union(i, j)

    clusters = {}
    for i, r in enumerate(located):
        clusters.setdefault(find(i), []).append(r)
    ordered = _ordered([(None, c) for c in clusters.values()])
    out = [(_area_label(c, n), c) for n, (_, c) in enumerate(ordered)]
    if unlocated:
        out.append(('Unlocated', unlocated))
    return out


def _area_label(cluster, index):
    """Names a spatial cluster by its dominant grid, else level, else number."""
    for field, prefix in (('grid', 'Area '), ('level', '')):
        counts = {}
        for r in cluster:
            value = r.get(field)
            if value:
                counts[value] = counts.get(value, 0) + 1
        if counts:
            return prefix + max(counts, key=lambda v: counts[v])
    return 'Cluster %d' % (index + 1)


def _ordered(labeled_buckets):
    """Deterministic order: largest bucket first, earliest id breaks ties."""
    return sorted(labeled_buckets,
                  key=lambda pair: (-len(pair[1]), min(r['id'] for r in pair[1])))


def _finish(groups, ungrouped, skipped, explanation):
    ordered = _ordered(groups)
    names = _unique_names([label for label, _ in ordered])
    return {
        'groups': [{'name': name, 'ids': [r['id'] for r in subset]}
                   for name, (_, subset) in zip(names, ordered)],
        'ungrouped_ids': sorted(r['id'] for r in ungrouped),
        'skipped_existing': skipped,
        'explanation': explanation,
    }


def _unique_names(names):
    """Dedupes display names by appending ' (2)', ' (3)', ..."""
    seen, out = {}, []
    for name in names:
        n = seen.get(name, 0) + 1
        seen[name] = n
        out.append(name if n == 1 else '%s (%d)' % (name, n))
    return out
