"""Hide Tabs: takes chosen tabs off the Navisworks ribbon and puts them back.

Field-measured: Navisworks resets every add-in tab's IsVisible on each idle
(NWRibbonUtil.UpdateVisibleProps, from MainWindow.OnIdle), which is why its
own right-click Show Tabs cannot keep an add-in tab hidden, and why hiding
one from here flipped back 200 times in a few seconds. It walks the ribbon's
tab list to do that, so a tab taken out of the list is left alone: that is
what this does, remembering the ribbon's order so each tab goes back where
it was.

The pure half (above the API line) takes plain dicts, {'id', 'title',
'contextual'} per tab in ribbon order, and runs anywhere; it is tested from
src/PyNavis.Tests/TabHiderTests.cs. The toggle, its Shift+Click and the
extension's app-closing hook share the API half. It lives in the
extension's lib so all three can import it.
"""

TOOL = 'hide_tabs'
DEFAULTS = {
    # Ribbon tab ids to take off, in no particular order. An id whose add-in
    # is not loaded is kept and skipped, so uninstalling one costs nothing.
    'tabs': [],
}

# Tabs Navisworks opens when they are needed (a selection, a section, a
# recording). Taking one out would stop it opening, so none is offered.
ON_DEMAND = ('ID_RibbonTab_ItemTools', 'ID_RibbonTab_SectioningTools', 'RibbonTab_RecordingTab')

# Every tab pyNavis builds; one of them carries the button that brings the
# rest back, so none is offered.
OWN_PREFIX = 'PYNAVIS_TAB_'


def offerable(tabs):
    """The tabs the user may take off, in ribbon order."""
    return [t for t in tabs
            if not t['contextual'] and t['id'] not in ON_DEMAND
            and not t['id'].startswith(OWN_PREFIX)]


def to_hide(tabs, chosen):
    """Ids of the chosen tabs that are on the ribbon now and may be taken
    off, in ribbon order."""
    wanted = set(chosen)
    return [t['id'] for t in offerable(tabs) if t['id'] in wanted]


def restore_plan(original, hidden, current):
    """[(id, index), ...]: where each hidden tab goes back, in the order to
    insert them. original is the ribbon's ids when they were taken out,
    current its ids now. Each goes back straight after the nearest tab that
    preceded it then and is on the ribbon now (first, when none is), so tabs
    that arrived in the meantime keep their place and a pyNavis tab rebuilt
    by Reload does not drag the others along."""
    now = list(current)
    plan = []
    for position, tab_id in enumerate(original):
        if tab_id not in hidden:
            continue
        index = 0
        for before in reversed(original[:position]):
            if before in now:
                index = now.index(before) + 1
                break
        now.insert(index, tab_id)
        plan.append((tab_id, index))
    return plan


def title_of(tab):
    """A tab's name as the ribbon shows it; add-ins pad theirs with spaces."""
    return (tab['title'] or '').strip() or tab['id']


def hidden_message(titles):
    """(title, detail) for the toast after tabs come off."""
    if len(titles) == 1:
        return ('1 tab hidden', '%s. Click again to bring it back.' % titles[0])
    return ('%d tabs hidden' % len(titles),
            '%s. Click again to bring them back.' % ', '.join(titles))


def shown_message(count):
    """The toast after tabs go back."""
    return '1 tab is back' if count == 1 else '%d tabs are back' % count


# The picker's prompt; it opens with last time's choice ticked.
PROMPT = 'Tick the tabs to take off the ribbon.'


# ---- API half --------------------------------------------------------------

# Where the taken-out tabs are kept: an AppDomain slot, because Reload drops
# this module and a tab nobody holds any more could never go back.
_SLOT = 'pyNavis.HideTabs'


def _ribbon():
    import clr
    clr.AddReference('AdWindows')
    from Autodesk.Windows import ComponentManager
    return ComponentManager.Ribbon


def _describe(tab):
    return {'id': str(tab.Id or ''), 'title': str(tab.Title or ''),
            'contextual': bool(getattr(tab, 'IsContextualTab', False))}


def _slot(value=None, clear=False):
    from System import AppDomain
    if clear or value is not None:
        AppDomain.CurrentDomain.SetData(_SLOT, value)
        return value
    return AppDomain.CurrentDomain.GetData(_SLOT)


def tab_list():
    """The ribbon's tabs as the pure half takes them, in ribbon order."""
    return [_describe(t) for t in _ribbon().Tabs]


def hidden():
    """True while tabs are off the ribbon."""
    kept = _slot()
    return kept is not None and len(kept['tabs']) > 0


def hide(chosen):
    """Takes the chosen tabs off the ribbon; returns their names. Does
    nothing, and returns [], while tabs are already off."""
    if hidden():
        return []
    ribbon = _ribbon()
    present = [(t, _describe(t)) for t in ribbon.Tabs]
    ids = set(to_hide([d for _t, d in present], chosen))
    out = [(t, d) for t, d in present if d['id'] in ids]
    if not out:
        return []
    if any(ribbon.ActiveTab is t for t, _d in out):
        staying = [t for t, d in present if d['id'] not in ids and d['id'] not in ON_DEMAND]
        if staying:
            ribbon.ActiveTab = staying[0]
    for tab, _d in out:
        ribbon.Tabs.Remove(tab)
    _slot({'order': [d['id'] for _t, d in present], 'tabs': [t for t, _d in out]})
    return [title_of(d) for _t, d in out]


def show():
    """Puts back what hide() took off, each where it was; returns how many."""
    kept = _slot()
    if kept is None:
        return 0
    ribbon = _ribbon()
    by_id = dict((str(t.Id or ''), t) for t in kept['tabs'])
    current = [str(t.Id or '') for t in ribbon.Tabs]
    missing = [i for i in kept['order'] if i in by_id and i not in current]
    plan = restore_plan(kept['order'], missing, current)
    for tab_id, index in plan:
        ribbon.Tabs.Insert(min(index, ribbon.Tabs.Count), by_id[tab_id])
    _slot(clear=True)
    return len(plan)


def choose():
    """The picker: every tab that may be taken off, last time's choice
    ticked. Saves and returns the ids ticked, or None on cancel. Tabs that
    are off the ribbon are not on it to pick, so callers put them back
    first."""
    from pynavis import forms, settings
    values = settings.load(TOOL, DEFAULTS)
    tabs = tab_list()
    offer = offerable(tabs)
    if not offer:
        forms.alert('There are no tabs to hide. Tabs Navisworks opens on its own and '
                    'the pyNavis tabs are never offered.', title='Hide Tabs')
        return None
    picked = forms.select_from_list([(title_of(t), t['id']) for t in offer],
                                    title='Hide Tabs', multiselect=True, prompt=PROMPT,
                                    checked=values['tabs'])
    if picked is None:
        return None
    values['tabs'] = list(picked)
    settings.save(TOOL, values)
    return values['tabs']
