"""COM API bridge - the parts the .NET API cannot do, chiefly WRITING properties.

The .NET API reads properties but cannot create them; the classic workaround is
ComApiBridge + InwGUIPropertyNode2.SetUserDefined, wrapped here so scripts get
one call: set_user_property(item, 'My Tab', 'Checked By', 'AB').
"""

from pynavis import _api
from pynavis._api import Api

_api.add_reference('Autodesk.Navisworks.ComApi')
_api.add_reference('Autodesk.Navisworks.Interop.ComApi')

from Autodesk.Navisworks.Api.ComApi import ComApiBridge
from Autodesk.Navisworks.Api.Interop.ComApi import nwEObjectType


def get_state():
    """The COM InwOpState10 root object, for advanced use."""
    return ComApiBridge.State


def set_user_property(item, tab_name, prop_name, value):
    """Creates or updates a user-defined property on a ModelItem.

    Writes into the user tab called tab_name (created when missing, updated in
    place when present - other properties on the tab are preserved).
    """
    state = ComApiBridge.State
    path = ComApiBridge.ToInwOaPath(item)
    node = state.GetGUIPropertyNode(path, True)

    index, existing_tab = _user_tab(node, tab_name)

    vec = state.ObjectFactory(nwEObjectType.eObjectType_nwOaPropertyVec, None, None)
    if existing_tab is not None:
        for prop in existing_tab.Properties():
            if prop.UserName == prop_name:
                continue
            kept = state.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, None, None)
            kept.name = prop.name
            kept.UserName = prop.UserName
            kept.value = prop.value
            vec.Properties().Add(kept)

    new_prop = state.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, None, None)
    new_prop.name = _internal_name(prop_name)
    new_prop.UserName = prop_name
    new_prop.value = value
    vec.Properties().Add(new_prop)

    node.SetUserDefined(index, tab_name, 'pyNavis', vec)


def set_custom_tab(item, tab_name, pairs):
    """Writes tab_name as a WHOLE tab on a ModelItem: unlike set_user_property
    (which preserves the tab's other properties), any existing tab_name is
    replaced entirely by pairs.

    pairs is [(display_name, value), ...] - already validated and sorted by
    pynavis.props._plan_custom, so this function trusts it as-is. Same COM
    plumbing as set_user_property (state.GetGUIPropertyNode, 1-based
    user-tab index, ObjectFactory-built nwOaPropertyVec, SetUserDefined) -
    see that function's docstring for background. Exceptions raised inside a
    COM callback vanish silently on the Navisworks side; callers (see
    pynavis.props.set_custom) wrap each item's call and capture the error
    themselves rather than trust anything raised here to surface on its own.
    """
    state = ComApiBridge.State
    path = ComApiBridge.ToInwOaPath(item)
    node = state.GetGUIPropertyNode(path, True)

    index, _existing_tab = _user_tab(node, tab_name)

    vec = state.ObjectFactory(nwEObjectType.eObjectType_nwOaPropertyVec, None, None)
    for name, value in pairs:
        prop = state.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, None, None)
        prop.name = _internal_name(name)
        prop.UserName = name
        prop.value = value
        vec.Properties().Add(prop)

    node.SetUserDefined(index, tab_name, 'pyNavis', vec)


def remove_custom_tab(item, tab_name):
    """Removes tab_name entirely from a ModelItem via SetUserDefined with an
    empty properties vector - the COM contract for deleting a whole user tab
    (there is no separate 'delete tab' call). Returns False without touching
    the item if it carries no tab_name tab.

    See set_custom_tab for the shared COM plumbing and the silent-exception
    trap; callers (see pynavis.props.remove_custom) wrap per item.
    """
    state = ComApiBridge.State
    path = ComApiBridge.ToInwOaPath(item)
    node = state.GetGUIPropertyNode(path, True)

    index, existing_tab = _user_tab(node, tab_name)
    if existing_tab is None:
        return False

    vec = state.ObjectFactory(nwEObjectType.eObjectType_nwOaPropertyVec, None, None)
    node.SetUserDefined(index, tab_name, 'pyNavis', vec)
    return True


def _user_tab(node, tab_name):
    """(index, attribute) for the user-defined GUIAttribute named tab_name on
    node, or (0, None) when tab_name is not present - shared by
    set_user_property, set_custom_tab and remove_custom_tab.

    SetUserDefined addresses user tabs by 1-based index among node's
    user-defined attributes; index 0 means 'create a new tab' to it, which is
    also the right sentinel for 'not found' here.
    """
    user_index = 0
    for attribute in node.GUIAttributes():
        if not attribute.UserDefined:
            continue
        user_index += 1
        if attribute.ClassUserName == tab_name:
            return user_index, attribute
    return 0, None


def _internal_name(display_name):
    """COM property internal names must be identifier-like."""
    chars = [c if c.isalnum() else '_' for c in str(display_name)]
    return ''.join(chars) or 'pynavis_prop'
