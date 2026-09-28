"""Selects elements by their Revit element ID.

The paste box always opens, with the clipboard's IDs already in it when the
clipboard holds something that looks like IDs (see elementids.clipboard_ids
for what counts). So copying a column of IDs out of a clash report or a
schedule and clicking the button leaves nothing to type - but the IDs are
still shown for approval and editing before anything is searched, because the
search costs seconds per ID and replaces the current selection when it lands.

Both places an element id hides in a federated model are searched, and the
one that worked moves to the front for the remaining ids, so a whole paste
costs one walk of the model per id in a model of either flavour:

  * the "Element ID" property tab an NWC exported from Revit carries
  * the "[123456]" suffix a model published straight through design
    coordination carries in its name instead, having no such tab at all

Only real elements match - nodes the exporter marked with its Element
category. The nested parts under a family instance carry an "Element ID" tab
too, holding the TYPE's id, and without the check a pasted type id resolved
to the first instance of that family (see elementids.ELEMENT_CATEGORY). One
id then selects one item; the every_match setting keeps walking for the same
element exported into more than one file.

Zooming, hiding everything else, whether the clipboard fills the box, and
every_match are settings; Shift+Click opens them. Everything above the guard at the bottom is
pure, so this file doubles as an importable module for tests (see
src/PyNavis.Tests/SelectByIdsScriptTests.cs).
"""

import elementids

from pynavis import forms, script, sets, settings, toast, view

# What a python int compiles to in a search condition is
# VariantData.FromInt32, so a larger id has to be matched as text instead of
# overflowing on the way in.
_INT32_MAX = 2147483647

# The shapes an element id search can take, tried in this order until one
# matches. The property tab is the common case and goes first; the name
# suffix is the design coordination fallback.
#
# Text before number, though both read the same property: the Revit exporter
# stores the id as a DisplayString (field-checked - the variant
# reports IsDisplayString, and an EqualValue against FromInt32 matched nothing
# on a model whose tab plainly held the id). Number stays for a model that
# does store it numerically, but it is the rarer shape and a strategy that
# misses costs a full walk of the model, so it must not be the one tried first.
STRATEGIES = ('text', 'number', 'name')

# Above this many ids the search gets a progress window with a Cancel button.
# One id is a second or so and the window would only flash; two is already
# long enough to want to see which id is being looked for.
_PROGRESS_THRESHOLD = 1

# How many ids a toast detail line names before it starts counting instead.
_DETAIL_LIMIT = 8


def condition_for(strategy, element_id, values):
    """The single search condition for one strategy, or None when it cannot
    apply to this id at all."""
    if strategy == 'name':
        # Both brackets on purpose: "[123456]" cannot match "[1234567]".
        return {'category': 'Item', 'prop': 'Name',
                'op': 'contains', 'value': '[%s]' % element_id}

    category = values['id_category']
    prop = values['id_property']
    if strategy == 'text':
        return {'category': category, 'prop': prop,
                'op': 'equals', 'value': str(element_id)}

    number = int(element_id)
    if number > _INT32_MAX:
        return None
    return {'category': category, 'prop': prop, 'op': 'equals', 'value': number}


def query_for(strategy, element_id, values, elements_only):
    """The whole query for one strategy, or None when the strategy cannot
    apply to this id.

    elements_only ANDs in "carries the exporter's Element category", so a
    property-tab match has to be a real Revit element and not one of the
    nested parts that carry the TYPE's id under the same tab (see
    elementids.ELEMENT_CATEGORY). It is skipped for the name strategy: a
    design coordination model has no Revit categories at all, and there the
    bracketed suffix IS the element.
    """
    condition = condition_for(strategy, element_id, values)
    if condition is None:
        return None
    conditions = [condition]
    if elements_only and strategy != 'name':
        conditions.append({'category': elementids.ELEMENT_CATEGORY,
                           'op': 'has_category', 'by_display': False})
    return {'or': [{'and': conditions}]}


def strategies_for(kind):
    """STRATEGIES minus the property shape the model provably does not store.

    kind is what stored_kind read off the model: 'text' (a DisplayString, the
    Revit exporter's shape), 'number' (an Int32), or None when it could not
    tell. A property strategy that cannot match is not free to keep: every
    strategy tried on an id that misses costs a full walk of the model, so on
    the 1.75M-item federation dropping the impossible one takes a miss - and
    a type id is now a miss by design - from ~9s to ~6s.
    """
    if kind == 'text':
        return ('text', 'name')
    if kind == 'number':
        return ('number', 'name')
    return STRATEGIES


def promote(order, winner):
    """order with winner moved to the front, so the next id tries what just
    worked first. A winner that is not in order leaves it unchanged."""
    if winner not in order:
        return list(order)
    rest = [strategy for strategy in order if strategy != winner]
    return [winner] + rest


def short_list(ids):
    """ids as one line for a toast detail, counting the tail once it gets long
    enough that naming every id would push the toast off the screen."""
    if len(ids) <= _DETAIL_LIMIT:
        return elementids.format_id_list(ids)
    head = elementids.format_id_list(ids[:_DETAIL_LIMIT])
    return '%s and %d more' % (head, len(ids) - _DETAIL_LIMIT)


# --- API half (Navisworks only) ---------------------------------------------


def find_by_ids(ids, values, report=None):
    """(items, matched, missing) for a list of id strings.

    items is a ModelItemCollection with duplicates removed: under every_match
    one id can match several items, and two ids can land on the same item in a
    model whose name carries an id that a child repeats.
    """
    from pynavis._api import Api

    found = Api.ModelItemCollection()
    matched = []
    missing = []
    order = list(strategies_for(stored_kind(values)))
    elements_only = model_marks_elements()

    for index, element_id in enumerate(ids):
        if report is not None:
            report(index, len(ids), element_id)
        hits, winner = _first_hit(element_id, order, values, elements_only)
        if hits is None:
            missing.append(element_id)
            continue
        matched.append(element_id)
        for item in hits:
            if not found.Contains(item):
                found.Add(item)
        order = promote(order, winner)

    return found, matched, missing


def stored_kind(values):
    """'text', 'number', or None: the variant kind the configured id property
    is stored in, read off the first item in the walk that carries it.

    One cheap find_first per run - the first wall or fixture answers it. The
    Revit exporter writes a DisplayString (field-checked), so the
    answer is nearly always 'text'; the point of asking rather than assuming
    is a model that stores it as an Int32, where assuming would make every
    id a miss. None (no item carries the property at all, or an unfamiliar
    kind) keeps every strategy in play.
    """
    probe = {'or': [{'and': [{'category': values['id_category'],
                              'prop': values['id_property'],
                              'op': 'has_property'}]}]}
    item = sets.find_first(probe)
    if item is None:
        return None
    found = item.PropertyCategories.FindPropertyByDisplayName(
        values['id_category'], values['id_property'])
    if found is None:
        return None
    return kind_of(found.Value)


def kind_of(variant):
    """'text' for a DisplayString variant, 'number' for an Int32, else None.
    Pure: probes the same Is* flags pynavis.props does."""
    if getattr(variant, 'IsDisplayString', False):
        return 'text'
    if getattr(variant, 'IsInt32', False):
        return 'number'
    return None


def model_marks_elements():
    """Does this model carry the exporter's Element category anywhere?

    One cheap find_first per run (the first wall or instance in the walk
    answers it, milliseconds). When it does, every property-tab match has to
    be a real element, so a type id - which every nested part of every
    instance of that type carries under "Element ID" - finds nothing instead
    of finding some arbitrary instance. When it does not, the model came
    through an exporter that never wrote the category, and requiring it
    would turn every id into a miss.
    """
    probe = {'or': [{'and': [{'category': elementids.ELEMENT_CATEGORY,
                              'op': 'has_category', 'by_display': False}]}]}
    return sets.find_first(probe) is not None


def _first_hit(element_id, order, values, elements_only):
    """(hits, strategy) for the first strategy that matches anything.

    ONE node by default, every copy under every_match - see elementids for why
    one id is not one item. Matches are real elements only when the model
    marks them (see query_for and model_marks_elements).

    The default stops the walk at its first hit (sets.find_first, ~0.80s on the
    1.75M-item federation this was measured against), which is what "take me to
    this element" means. every_match pays a full FindAll per strategy instead,
    ~3.0s per id, and nothing cheaper reaches the whole set: an OR search costs
    a full walk PER TERM (20 ids measured at 56s, exactly what 20 separate
    searches cost), PruneBelowMatch changes neither cost nor result, and a
    follow-up sweep scoped to any ancestor short of the document root misses
    the copies living in sibling exports (measured level by level: 1 -> 6 ->
    16). So these two modes really are the whole menu.
    """
    every = values.get('every_match')

    for strategy in order:
        query = query_for(strategy, element_id, values, elements_only)
        if query is None:
            continue

        if every:
            hits = sets.run(query)
            if hits.Count:
                return hits, strategy
            continue

        first = sets.find_first(query)
        if first is not None:
            return _just(first), strategy
    return None, None


def _just(item):
    """One item as a ModelItemCollection, so both search modes hand back the
    same shape to find_by_ids."""
    from pynavis._api import Api

    collection = Api.ModelItemCollection()
    collection.Add(item)
    return collection


def search(ids, values):
    """find_by_ids with a progress window once there are enough ids to be
    worth cancelling. Raises forms.Cancelled if the user cancels."""
    if len(ids) <= _PROGRESS_THRESHOLD:
        return find_by_ids(ids, values)

    with forms.progress('Select by IDs', 'Searching') as p:
        def report(done, total, element_id):
            p.check()
            p.update(float(done) / total,
                     'Searching %d of %d: %s' % (done + 1, total, element_id))
        return find_by_ids(ids, values, report)


def ask(prefill=''):
    """The paste box; returns its text, or None on cancel."""
    win = forms.WPFWindow('layout.xaml')
    box = win['IdsBox']
    box.Text = prefill
    box.SelectAll()

    def _ok(sender, args):
        win.close(True)

    win['OkButton'].Click += _ok
    if not win.show_dialog():
        return None
    return win['IdsBox'].Text


def clipboard_prefill(values):
    """The text the box opens with: the clipboard's ids when it holds
    something that looks like ids, otherwise empty. Pure apart from the
    clipboard read itself, which is why the parsing lives here rather than
    inline in run()."""
    if not values['use_clipboard']:
        return ''
    ids = elementids.clipboard_ids(script.clipboard_text())
    return elementids.format_id_list(ids) if ids else ''


def run():
    values = settings.load(elementids.SETTINGS_KEY, elementids.DEFAULTS)

    # The box always opens, even when the clipboard parses cleanly: a search
    # that costs seconds per id and then replaces the selection is not
    # something to start from a stray copy, so the ids are shown for approval
    # (and editing) first. The clipboard only fills the box in.
    typed = ask(clipboard_prefill(values))
    if typed is None:
        return                                      # cancelled: say nothing

    ids, rejected = elementids.parse_ids(typed)
    if not ids:
        toast.warning('No element IDs found', _rejected_detail(rejected))
        return

    try:
        found, matched, missing = search(ids, values)
    except forms.Cancelled:                         # cancelled: say nothing
        return

    if not found.Count:
        toast.warning('No elements matched',
                      'Searched %d ID(s): %s' % (len(ids), short_list(ids)))
        return

    _apply(found, values)
    _report(found, matched, missing)


def _apply(found, values):
    from pynavis import selection

    selection.set_items(found)
    if values['isolate']:
        view.isolate(found)
    if values['zoom']:
        view.zoom_selected()


def _report(found, matched, missing):
    """The closing toast. No note about where the ids came from any more: the
    user typed or approved them in the box either way."""
    if missing:
        toast.warning('Selected %d element(s), %d ID(s) not found'
                      % (found.Count, len(missing)),
                      'Not found: %s' % short_list(missing))
        return
    toast.success('Selected %d element(s) from %d ID(s)'
                  % (found.Count, len(matched)))


def _rejected_detail(rejected):
    if not rejected:
        return 'Nothing in the box looked like an element ID.'
    return 'Ignored: %s' % short_list(rejected)


if '__commandpath__' in globals():
    run()
