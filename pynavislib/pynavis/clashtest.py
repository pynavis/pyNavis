# -*- coding: utf-8 -*-
"""Clash test authoring: create, edit, run, and delete ClashTests, plus a
batch result-status writer.

The pure half (_tolerance_model_units, _map_lookup, _TYPE_MAP, _STATUS_MAP,
_plan_edit) has no Navisworks dependency and runs anywhere; the API half
(create, edit, run, run_all, clear_results, delete, set_status, summary)
lazy-imports pynavis.clash / pynavis.sets / pynavis.viewpoints / pynavis.doc
inside each function, so importing this module never needs a live
Navisworks session - pynavis.clash itself cannot be imported outside one
(see its module docstring: it calls _api.add_reference at import time), so
this module must never import it at module scope either.

REUSE, never duplicate: get_clash/units_to_meters/summarize all come from
pynavis.clash, and the Autodesk.Navisworks.Api.Clash namespace binding
(ClashApi, with its extension methods already imported via
clr.ImportExtensions) is reused as clash.ClashApi rather than re-adding the
Clash assembly reference here.

TWO TRAPS carried over from clash.py, both apply here:

TestsEditTestFromCopy is DEFINITION-only and silently ignores anything under
a test's results tree - exactly right for edit() (name/tolerance are
definition fields), but never use it to restructure results. Result-tree
work (grouping, ungrouping) belongs to clash.apply_plan/clash.ungroup, which
rebuild on a detached copy and commit with TestsReplaceWithCopy instead -
edit() only ever calls TestsEditTestFromCopy and only ever touches
DisplayName/Tolerance.

TestsAddCopy(ClashTest) copies the passed-in test's definition into the
tests tree rather than taking ownership of the object itself (same
copy-not-move contract as TestsMove/TestsRemove - see clash.py's module
docstring). create() therefore hands back a FRESH reference resolved from
the tree after adding - by GUID DIFF (the one entry in TestsData.Tests
whose Guid was not there before the add), not by assuming TestsAddCopy
appends at the end, since the root Tests collection can also hold test
FOLDERS (clash.walk_tests flattens GroupItems) and a positional guess would
be wrong the moment one exists - see _resolve_added_test. Every other
function here (edit/run/clear_results/delete) expects a live, tree-resident
ClashTest - the same object clash.walk_tests() yields, or create()'s return
value - never the detached CreateCopy() used internally to build an edit.

Tolerance is always METERS at this module's boundary (matching every other
pynavis distance parameter) and is converted to the document's model units
via clash.units_to_meters(doc) before being written to ClashTest.Tolerance,
which the live API stores in model units - see _tolerance_model_units.

test_type / status strings are validated by _map_lookup BEFORE any
Navisworks import (same "guard first" shape as props.set_custom's tab_name
check), so a typo raises ValueError without needing a live document. The
enum member names in _TYPE_MAP/_STATUS_MAP were verified by reflection
against Navisworks 2026 (Autodesk.Navisworks.Clash.dll,
Autodesk.Navisworks.Api.Clash.ClashTestType / ClashResultStatus) - note the
live ClashTestType enum also has HardConservative and Custom members this
module does not expose in v1, and that the member is 'Duplicate' (singular),
not 'Duplicates'.

set_status batches TestsEditResultStatus, one call per result, all inside a
single transaction; it reads back each result's current AssignedTo and
passes it straight through so a status-only edit never clobbers an existing
assignment (TestsEditResultStatus takes an Assignee alongside the status -
there is no overload that edits status alone).
"""


def _tolerance_model_units(tolerance_m, meters_per_unit):
    """Tolerance in METERS converted to the document's model units:
    tolerance_m / meters_per_unit, the same division direction as any other
    pynavis distance conversion (meters_per_unit comes from
    clash.units_to_meters(doc), meters per ONE model unit - dividing a
    meters value by that yields model units)."""
    return tolerance_m / meters_per_unit


# Input string -> live ClashTestType enum member name. Verified by
# reflection against Navisworks 2026's Autodesk.Navisworks.Clash.dll: the
# live enum is Hard, HardConservative, Clearance, Duplicate, Custom - only
# the three most commonly authored kinds are exposed here in v1.
_TYPE_MAP = {
    'hard': 'Hard',
    'clearance': 'Clearance',
    'duplicate': 'Duplicate',
}

# Input string -> live ClashResultStatus enum member name. Verified by
# reflection against Navisworks 2026's Autodesk.Navisworks.Clash.dll: New,
# Active, Reviewed, Approved, Resolved (this is the complete enum).
_STATUS_MAP = {
    'new': 'New',
    'active': 'Active',
    'reviewed': 'Reviewed',
    'approved': 'Approved',
    'resolved': 'Resolved',
}


def _map_lookup(mapping, key, label):
    """The mapped enum member name for key, or ValueError naming every valid
    option (sorted, for a deterministic message) when key is not one of
    them. Pure: mapping is a plain dict of str -> str, so this never touches
    Navisworks - the API half resolves the returned name onto the live enum
    type itself with getattr()."""
    try:
        return mapping[key]
    except KeyError:
        raise ValueError(
            '%s must be one of %s, not %r' %
            (label, ', '.join(sorted(mapping)), key))


def _plan_edit(kwargs):
    """[(field, value), ...] for edit()'s CreateCopy + mutate step, from
    kwargs = {'name': str or None, 'tolerance_model_units': float or None}
    (tolerance already converted to model units by the caller - see
    _tolerance_model_units). A None entry means "leave that field alone"
    and is omitted; calling edit() with neither name nor tolerance_m yields
    []. Field names ('DisplayName', 'Tolerance') match the live ClashTest
    properties this maps onto, so the API half can setattr(copy, field,
    value) directly without its own field-name table."""
    fields = []
    if kwargs.get('name') is not None:
        fields.append(('DisplayName', kwargs['name']))
    if kwargs.get('tolerance_model_units') is not None:
        fields.append(('Tolerance', kwargs['tolerance_model_units']))
    return fields


# --- API half (Navisworks only; lazy imports keep the module pure) -----------


def create(name, a, b, tolerance_m=0.001, test_type='hard', doc=None):
    """Creates a new ClashTest named name, comparing selections a and b, and
    adds it at the ROOT of the tests tree (TestsAddCopy(ClashTest) - the
    overload with no GroupItem; v1 scope does not place new tests in
    folders). Returns the tree-resident ClashTest - see the module
    docstring for why that is a freshly resolved reference, not the local
    instance built here.

    a and b each accept: a ModelItemCollection or any iterable of ModelItem
    (Selection.CopyFrom(items) directly), or a set NAME/path string resolved
    via sets.find + sets.items_of (raising ValueError if no such set exists,
    or if the name resolves to a folder - see sets.items_of).

    tolerance_m is in METERS and is converted to the document's model units
    via clash.units_to_meters(doc) (see _tolerance_model_units) before being
    written to ClashTest.Tolerance.

    test_type is one of 'hard' / 'clearance' / 'duplicate' (see _TYPE_MAP);
    an unknown value raises ValueError naming the valid options, checked
    BEFORE any Navisworks import so a typo fails without needing a live
    document.
    """
    type_name = _map_lookup(_TYPE_MAP, test_type, 'test_type')

    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    d = doc if doc is not None else _doc.get_doc()
    ClashApi = clash.ClashApi

    test = ClashApi.ClashTest()
    test.DisplayName = name
    test.TestType = getattr(ClashApi.ClashTestType, type_name)
    test.Tolerance = _tolerance_model_units(tolerance_m, clash.units_to_meters(d))
    _fill_selection(test.SelectionA, a, d)
    _fill_selection(test.SelectionB, b, d)

    data = clash.get_clash(d).TestsData
    before = set(str(item.Guid) for item in data.Tests)
    with viewpoints.transaction('pyNavis clash test', d):
        data.TestsAddCopy(test)
    return _resolve_added_test(data.Tests, before)


def _resolve_added_test(tests, before_guids):
    """The one entry in tests (TestsData.Tests, just after an add) whose
    Guid was not in before_guids - i.e. the test TestsAddCopy just created.
    Falls back to the last entry (VERIFY at smoke) only if every guid was
    already present, which should not happen after a successful add."""
    for item in tests:
        if str(item.Guid) not in before_guids:
            return item
    return tests[tests.Count - 1]


def edit(test, name=None, tolerance_m=None, doc=None):
    """Edits a test's DEFINITION (name and/or tolerance). test must be a
    live, tree-resident ClashTest (e.g. from clash.walk_tests or
    clashtest.create's return value).

    tolerance_m is in METERS, converted the same way as create()'s. Passing
    neither name nor tolerance_m is a no-op that returns test unchanged
    (see _plan_edit).

    A name-only edit goes straight through TestsEditDisplayName (the same
    single O(1) call viewpoints.apply_renames uses for SavedViewpoints) -
    the CreateCopy + TestsEditTestFromCopy route below costs roughly
    0.003ms per RESULT (see clash.py's module docstring), so skipping it for
    a plain rename avoids paying that cost across a large results tree just
    to change a label. Any edit that touches tolerance still goes through
    CreateCopy + TestsEditTestFromCopy - correct here because Tolerance is
    definition, not results-tree, state (see the module docstring's trap),
    and there is no single-call Tolerance setter to match
    TestsEditDisplayName's.

    Returns test either way. TestsEditDisplayName is documented (by its own
    name, alongside viewpoints.apply_renames' identical use of
    SavedViewpoints.EditDisplayName) to mutate the live item in place.
    TestsEditTestFromCopy is assumed to do the same for test - its very name
    contrasts it with TestsReplaceWithCopy, which clash.py's module
    docstring documents as swapping in a new object and leaving the old
    wrapper dead - but that assumption is VERIFY AT SMOKE: confirm test's
    own DisplayName/Tolerance reflect the edit afterwards, e.g. by comparing
    against a fresh clash.walk_tests() read of the same test.
    """
    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    d = doc if doc is not None else _doc.get_doc()

    tolerance_model_units = (
        _tolerance_model_units(tolerance_m, clash.units_to_meters(d))
        if tolerance_m is not None else None)
    plan = _plan_edit({'name': name, 'tolerance_model_units': tolerance_model_units})
    if not plan:
        return test

    data = clash.get_clash(d).TestsData

    if tolerance_model_units is None:
        with viewpoints.transaction('pyNavis clash test', d):
            data.TestsEditDisplayName(test, name)
        return test

    copy = test.CreateCopy()
    for field, value in plan:
        setattr(copy, field, value)
    with viewpoints.transaction('pyNavis clash test', d):
        data.TestsEditTestFromCopy(test, copy)
    return test


def run(test, doc=None):
    """Runs one clash test (TestsRunTest). test must be tree-resident, same
    caveat as edit()."""
    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    d = doc if doc is not None else _doc.get_doc()
    data = clash.get_clash(d).TestsData
    with viewpoints.transaction('pyNavis clash run', d):
        data.TestsRunTest(test)


def run_all(doc=None):
    """Runs every clash test in the document (TestsRunAllTests)."""
    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    d = doc if doc is not None else _doc.get_doc()
    data = clash.get_clash(d).TestsData
    with viewpoints.transaction('pyNavis clash run', d):
        data.TestsRunAllTests()


def clear_results(test, doc=None):
    """Clears a test's results (TestsClearResults) without deleting the
    test itself. test must be tree-resident, same caveat as edit()."""
    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    d = doc if doc is not None else _doc.get_doc()
    data = clash.get_clash(d).TestsData
    with viewpoints.transaction('pyNavis clash results', d):
        data.TestsClearResults(test)


def delete(test, doc=None):
    """Deletes a top-level clash test (TestsRemove(ClashTest)) - v1 scope
    only removes root-level tests, matching create()'s root-only placement;
    a test filed inside a folder needs the (GroupItem, SavedItem) overload
    this module does not expose, so a test with a Parent raises ValueError
    up front rather than letting TestsRemove(ClashTest) fail with an opaque
    .NET error. test must be tree-resident, same caveat as edit()."""
    if getattr(test, 'Parent', None) is not None:
        raise ValueError(
            'delete() only removes root-level tests: %r is inside a folder'
            % test.DisplayName)

    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    d = doc if doc is not None else _doc.get_doc()
    data = clash.get_clash(d).TestsData
    with viewpoints.transaction('pyNavis clash test', d):
        data.TestsRemove(test)


def set_status(results, status, doc=None):
    """Batch-sets every result's Status via TestsEditResultStatus, one call
    per result inside a single transaction, preserving each result's
    existing AssignedTo (see the module docstring - the API call takes an
    Assignee argument alongside the status, so this reads each result's own
    current assignment back rather than clobbering it with a blank one).

    status is one of 'new' / 'active' / 'reviewed' / 'approved' / 'resolved'
    (see _STATUS_MAP); an unknown value raises ValueError naming the valid
    options, checked BEFORE any Navisworks import.

    Returns how many results were edited.
    """
    status_name = _map_lookup(_STATUS_MAP, status, 'status')

    from pynavis import doc as _doc
    from pynavis import clash
    from pynavis import viewpoints
    from pynavis._api import Api
    d = doc if doc is not None else _doc.get_doc()
    ClashApi = clash.ClashApi
    status_value = getattr(ClashApi.ClashResultStatus, status_name)

    data = clash.get_clash(d).TestsData
    results = list(results)
    with viewpoints.transaction('pyNavis clash status', d):
        for result in results:
            assignee = getattr(result, 'AssignedTo', None)
            if assignee is None:
                assignee = Api.Assignee()
            data.TestsEditResultStatus(result, status_value, assignee)
    return len(results)


def summary(doc=None):
    """Every clash test's summary - reuses clash.summarize directly, never
    duplicated here."""
    from pynavis import clash
    return clash.summarize(doc)


def _fill_selection(clash_selection, source, doc):
    """Populates one side (SelectionA/SelectionB) of a ClashTest from a
    create() argument: source is either a ModelItemCollection/iterable of
    ModelItem, or a str set name/path resolved via sets.find + sets.items_of
    first (raising ValueError if the name does not resolve to a usable
    set).

    Either way the result is normalized to an actual ModelItemCollection via
    sets._to_model_item_collection (reused, not duplicated here) before
    Selection.CopyFrom - Selection.CopyFrom has several overloads
    (ModelItemCollection / IEnumerable<ModelItem> / Selection /
    SelectionSourceCollection), and handing it an explicit
    ModelItemCollection sidesteps relying on the .NET interop layer picking
    the right one for a plain Python list.
    """
    from pynavis import sets
    from pynavis._api import Api

    if isinstance(source, str):
        found = sets.find(source, doc)
        if found is None:
            raise ValueError('no such selection set: %r' % source)
        items = sets.items_of(found, doc)
    else:
        items = sets._to_model_item_collection(source, Api)
    clash_selection.Selection.CopyFrom(items)
