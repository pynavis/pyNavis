"""Copies the Revit element IDs of the current selection to the clipboard.

The Revit command of the same name, for a federated model. Each selected item
resolves its id the two ways an id survives federation: the "Element ID"
property tab an NWC exported from Revit carries, and the "[123456]" suffix a
design coordination model carries in its name instead.

The list lands on the clipboard ready to paste back into Select by IDs, into
Revit's Select by ID, or into a report. Settings are shared with Select by
IDs; Shift+Click opens them.

Everything above the guard at the bottom is pure, so this file doubles as an
importable module for tests (src/PyNavis.Tests/IdsOfSelectionScriptTests.cs).
"""

import elementids

from pynavis import props, script, settings, toast

# How far up the tree to look for an id. A click in the view selects whichever
# node the current selection resolution lands on, often a geometry leaf below
# the Revit element, so the walk has to go up; the cap keeps it from reaching
# a model root whose own name happens to carry a bracketed number.
_MAX_ANCESTORS = 8


def summary(ids, without):
    """(level, message, detail) for the toast. One id is named rather than
    counted, and items that carry no id at all are always reported, so the
    tool never finishes looking like it did nothing."""
    if not ids:
        return ('warning', 'No element IDs found',
                'None of the %d selected item(s) carry an element ID.' % without)

    if len(ids) == 1:
        message = 'Element ID %s copied' % ids[0]
    else:
        message = '%d element IDs copied' % len(ids)

    if without:
        return ('warning', message,
                '%d selected item(s) carry no element ID.' % without)
    return ('success', message, None)


# --- API half (Navisworks only) ---------------------------------------------


def id_of(item, values):
    """The element id for one selected ModelItem, or None.

    The nearest ancestor-or-self that is a real element wins - the one the
    exporter marked with its Element category (elementids.ELEMENT_CATEGORY).
    That is not always the nearest node with an "Element ID" tab: the nested
    parts under a family instance carry the tab too, holding the TYPE's id,
    and a click in the view usually lands on one of those. Reading the first
    tab found copied the type id out, and Select by IDs then resolved it to
    the first instance of that family - the round trip through the clipboard
    landed on a different fixture. Field-checked (12345678 was the
    type, 12345679 the instance).

    A model whose exporter never wrote the category falls back to the old
    rule: the first ancestor-or-self carrying an id at all. Descendants are
    deliberately not searched either way: a group node would then report one
    arbitrary child's id as if it were its own.
    """
    node = item
    fallback = None
    for _depth in range(_MAX_ANCESTORS + 1):
        if node is None:
            break
        found = _id_on(node, values)
        if found and elementids.is_element(props.categories(node)):
            return found
        if found and fallback is None:
            fallback = found
        node = node.Parent
    return fallback


def _id_on(item, values):
    value = props.get(item, values['id_category'], values['id_property'])
    if value is not None:
        text = str(value).strip()
        if text:
            return text
    return elementids.id_from_name(getattr(item, 'DisplayName', None))


def run():
    from pynavis import selection

    items = selection.get_items()
    if not items:
        toast.info('Nothing selected', 'Select some elements first.')
        return

    values = settings.load(elementids.SETTINGS_KEY, elementids.DEFAULTS)

    ids = []
    seen = {}
    without = 0
    for item in items:
        found = id_of(item, values)
        if not found:
            without += 1
            continue
        if found not in seen:
            seen[found] = True
            ids.append(found)

    if ids:
        script.clipboard_copy(elementids.format_id_list(ids))
    toast.show(*summary(ids, without))


if '__commandpath__' in globals():
    run()
