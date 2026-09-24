"""Show, hide and count pyNavis dock panels.

A panel is a *.dockpane bundle: pane.xaml for content, an optional script.py that
receives the panel as __pane__, and a ribbon toggle that follows its visibility.
Slots are registered by the loader at Navisworks startup, so adding slots needs a
restart; claiming one does not.

WARNING: from inside a panel's own event handler (a Click handler wired up in
script.py, a timer callback, anything that fires after script.py has already
returned), always pass bundle_key explicitly, or use __pane__.Visible instead of
show()/hide()/toggle(). The default bundle_key resolves to whichever pyNavis
command most recently ran - not to the panel whose handler is calling - because it
reads PyNavisHost.CommandBundleKey, which the runtime sets before every run and
never un-sets. An event handler fires long after its own script.py run finished,
by which time the user may have clicked other buttons, so the default silently
targets the wrong panel.
"""

import clr

clr.AddReference('PyNavis.Runtime')

from PyNavis.Runtime.Panes import PaneRegistry
from pynavis.script import get_host


def _key(bundle_key):
    """The caller's own panel when no key is given.

    The runtime records the running command's bundle key on the PyNavisHost
    singleton before every run (dockpane scripts included), so that is the
    reliable place to read it: scope globals like __pane__ are not visible
    from an imported module.
    """
    if bundle_key:
        return str(bundle_key)
    key = get_host().CommandBundleKey
    if not key:
        raise ValueError('No bundle key given and no pyNavis command is running.')
    return key


def show(bundle_key=None):
    """Opens the panel and brings it to the front of its dock tab. False when it has no slot.

    From inside a panel's own event handler, pass bundle_key explicitly (or use
    __pane__.Visible = True) - the default resolves to the most recently run pyNavis
    command, not to this panel. See the module docstring.
    """
    return PaneRegistry.SetVisible(_key(bundle_key), True)


def hide(bundle_key=None):
    """Closes the panel. False when it has no slot.

    From inside a panel's own event handler, pass bundle_key explicitly (or use
    __pane__.Visible = False) - the default resolves to the most recently run pyNavis
    command, not to this panel. See the module docstring.
    """
    return PaneRegistry.SetVisible(_key(bundle_key), False)


def toggle(bundle_key=None):
    """Shows the panel when hidden, hides it when shown. False when it has no slot.

    From inside a panel's own event handler, pass bundle_key explicitly (or use
    __pane__.Visible) - the default resolves to the most recently run pyNavis command,
    not to this panel. See the module docstring.
    """
    key = _key(bundle_key)
    return PaneRegistry.SetVisible(key, not PaneRegistry.IsVisible(key))


def is_visible(bundle_key=None):
    """True while the panel is on screen.

    From inside a panel's own event handler, pass bundle_key explicitly (or read
    __pane__.Visible) - the default resolves to the most recently run pyNavis command,
    not to this panel. See the module docstring.
    """
    return PaneRegistry.IsVisible(_key(bundle_key))


def slot_of(bundle_key=None):
    """1-based slot the panel claimed, or 0 when every slot was taken."""
    return PaneRegistry.SlotFor(_key(bundle_key))


def slot_count():
    """How many panel slots this Navisworks session has."""
    return PaneRegistry.SlotCount


def add_slots(count):
    """Generates extra panel slots. Returns the new total; takes effect after a restart."""
    from PyNavis.Runtime.Panes import PaneSatellite
    return PaneSatellite.Generate(int(count))
