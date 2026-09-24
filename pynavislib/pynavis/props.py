# -*- coding: utf-8 -*-
"""Model item properties: reading PropertyCategories/DataProperty values as
plain Python values.

The pure half (_variant_to_python and its small helpers below it) has no
Navisworks dependency and is unit-tested against a python stub exposing the
same VariantData Is*/To* members - the probe ORDER is the logic under test.
The Is* probes are documented exclusive (a VariantData is exactly one kind),
so for any real value the order does not change which conversion fires, but
the order still has to be pinned down: it is what decides, for two probes
both True at once (never true live, but exactly what the test forces to
check the wiring), which branch wins, and it is what a variant kind this
module does not special-case falls all the way through to reach the final
variant.ToString() fallback.

The API half (categories, get, all_of, value_map) takes an already-resolved
ModelItem and needs no lazy pynavis._api import of its own - PropertyCategories /
FindPropertyByDisplayName / FindPropertyByName access is plain attribute/method
access on the item the caller already holds, the same access doc.get_property
uses (see doc.py). This module SUPERSEDES doc.get_property for new code:
get() returns an already-coerced Python value (str / int / float / bool /
datetime.datetime / (x, y, z) tuple) instead of a raw VariantData, so callers
no longer need to know VariantData's own To* API themselves. doc.get_property
is left untouched for existing callers.

VariantData conversions used here beyond ToString(): ToDisplayString() is
field-verified (see docs/authoring troubleshooting.html, which calls it on a
doc.get_property result). ToIdentifierString/ToBoolean/ToInt32/ToDouble/
ToDateTime/ToPoint3D follow the same From*/Is* naming symmetry the plan's
Global Constraints confirm for the rest of VariantData's API
(FromDisplayString/FromDouble/FromInt32/FromBoolean/FromDateTime/
FromDoubleLength paired with IsDisplayString/IsInt32/IsBoolean/... probes) -
VERIFY ToIdentifierString/ToDateTime/ToPoint3D at smoke if a live category
ever produces one of those kinds and the coerced value looks wrong.

Any IsDouble* probe True is treated as float via ToDouble(): VariantData has
several Double subtypes (IsDouble, IsDoubleLength, IsDoubleArea,
IsDoubleVolume, IsDoubleAngle, and more - see the plan's Global Constraints),
all of which read back as a plain float regardless of which unit the value
carries - unit conversion, if a caller needs it, is not this module's job.

A third half writes custom property tabs (set_custom/get_custom/remove_custom
plus the pure _plan_custom below them). Reading a custom tab back
(get_custom) is just the API half again - a written user tab shows up as an
ordinary PropertyCategory. WRITING one cannot go through the .NET API at
all (it can only read properties); set_custom/remove_custom lazily import
pynavis._com, which wraps the ComApiBridge + InwGUIPropertyNode2 route (see
_com.py's module docstring for the COM background and its silent-exception
trap).
"""

import datetime


def categories(item):
    """[(display_name, name), ...] for every PropertyCategory on item, in
    item.PropertyCategories order."""
    return [(category.DisplayName, category.Name) for category in item.PropertyCategories]


def get(item, category, prop, by_display=True):
    """The Python value of one property on item, or None if the category or
    property is not present.

    by_display=True (the default) looks up by display name via
    FindPropertyByDisplayName, matching doc.get_property; by_display=False
    switches to FindPropertyByName (mirrors the ByDisplayName/ByName pair
    sets.py's Condition.by_display switches between for search conditions -
    VERIFY FindPropertyByName's exact name at smoke, it is not in the plan's
    reflected fact list, only FindPropertyByDisplayName is).
    """
    finder = (item.PropertyCategories.FindPropertyByDisplayName if by_display
              else item.PropertyCategories.FindPropertyByName)
    found = finder(category, prop)
    if found is None:
        return None
    return _variant_to_python(found.Value)


def all_of(item):
    """Every property on item as rows {'category', 'property', 'value'}
    (display names), in item.PropertyCategories / category.Properties
    order - a flat table-ready shape, e.g. for output.print_table or an
    Excel export."""
    rows = []
    for category in item.PropertyCategories:
        for prop in category.Properties:
            rows.append({
                'category': category.DisplayName,
                'property': prop.DisplayName,
                'value': _variant_to_python(prop.Value),
            })
    return rows


def value_map(item, spec):
    """{key: value} for a spec {key: (category, prop), ...} - a compact way
    to pull several named properties into one dict for a table row or export
    column, by display name (see get(); value_map does not expose
    by_display - callers needing FindPropertyByName should call get()
    directly for that property). A (category, prop) pair not present on
    item maps to None, same as get()."""
    return dict((key, get(item, category, prop))
                for key, (category, prop) in spec.items())


def get_custom(item, tab_name):
    """{name: value} for a pynavis-written custom property tab, or None if
    item carries no tab_name tab.

    Read back through item.PropertyCategories - the SAME standard-categories
    API get()/all_of() use - rather than through the COM node: once written
    via set_custom, a user tab shows up there like any other PropertyCategory,
    keyed by DisplayName. No pynavis._com import needed for this direction;
    only the write side (set_custom/remove_custom) touches COM.
    """
    for category in item.PropertyCategories:
        if category.DisplayName == tab_name:
            return dict(
                (prop.DisplayName, _variant_to_python(prop.Value))
                for prop in category.Properties)
    return None


def set_custom(items, tab_name, values, doc=None, progress=None):
    """Writes tab_name as a WHOLE tab on every item in items: values =
    {name: str/int/float/bool}. One COM SetUserDefined call per item
    (pynavis._com.set_custom_tab) writes EXACTLY values - an existing
    same-named tab on an item is replaced, not merged, so a property left
    out of values disappears from the tab rather than surviving from a
    previous write.

    Wrapped in a single pynavis.viewpoints.transaction, INTENDED to land as
    one undo entry; STILL TO VERIFY IN A LIVE SESSION: the
    writes go through COM (SetUserDefined), and whether a transaction opened
    on the .NET Document actually captures COM-side edits into its own undo
    entry is an assumption this module rests on, not a verified fact - it is
    possible each item lands as its own undo step, or none at all.
    progress(i, total), if given, is called after every
    item (1-based i). Returns (written_count, errors): exceptions inside a
    COM callback vanish silently on the Navisworks side, so each item's
    write is wrapped in its own try/except here rather than letting one bad
    item abort - or silently drop - the rest.
    """
    if not isinstance(tab_name, str) or not tab_name.strip():
        raise ValueError('tab_name must not be empty')
    pairs = _plan_custom(values)

    from pynavis import viewpoints
    from pynavis import doc as _doc
    from pynavis import _com
    d = doc if doc is not None else _doc.get_doc()

    items = list(items)
    written = 0
    errors = []
    with viewpoints.transaction('pyNavis custom properties', d):
        for index, item in enumerate(items):
            try:
                _com.set_custom_tab(item, tab_name, pairs)
                written += 1
            except Exception as error:
                errors.append(str(error))
            if progress is not None:
                progress(index + 1, len(items))
    return written, errors


def remove_custom(items, tab_name, doc=None, progress=None):
    """Removes tab_name entirely from every item in items, via
    pynavis._com.remove_custom_tab (SetUserDefined with an empty properties
    vector - the COM contract for deleting a whole user tab). An item
    without tab_name is left alone and not counted.

    Wrapped in a single pynavis.viewpoints.transaction, intended to land as
    one undo entry - the same unverified transaction-over-COM assumption
    set_custom's docstring spells out, to verify at smoke. progress(i, total),
    if given, is called after every item (1-based i). Returns
    (removed_count, errors) - see set_custom for why each item is wrapped in
    its own try/except (COM callback exceptions vanish silently otherwise).
    """
    if not isinstance(tab_name, str) or not tab_name.strip():
        raise ValueError('tab_name must not be empty')

    from pynavis import viewpoints
    from pynavis import doc as _doc
    from pynavis import _com
    d = doc if doc is not None else _doc.get_doc()

    items = list(items)
    removed = 0
    errors = []
    with viewpoints.transaction('pyNavis custom properties', d):
        for index, item in enumerate(items):
            try:
                if _com.remove_custom_tab(item, tab_name):
                    removed += 1
            except Exception as error:
                errors.append(str(error))
            if progress is not None:
                progress(index + 1, len(items))
    return removed, errors


# --- pure: custom-tab write planning ------------------------------------------


def _plan_custom(values):
    """[(name, value), ...] from values = {name: str/int/float/bool}, sorted
    by name for a deterministic write order - a python dict's iteration
    order is not something two 'equivalent' calls are guaranteed to agree
    on, and the COM side writes properties in whatever order this list
    hands them over.

    Raises ValueError for an empty or whitespace-only name, and for a value
    whose type is not one of str/int/float/bool (bool passes as an int
    subclass, which is intended - a custom tab checkbox is exactly this
    case). The message names the offending type so a caller passing e.g. a
    list or None sees why.
    """
    pairs = []
    for name, value in values.items():
        if not isinstance(name, str) or not name.strip():
            raise ValueError('custom property name must not be empty: %r' % (name,))
        if not isinstance(value, (bool, int, float, str)):
            raise ValueError(
                'unsupported custom property value type for %r: %s' %
                (name, type(value).__name__))
        pairs.append((name, value))
    pairs.sort(key=lambda pair: pair[0])
    return pairs


# --- pure: VariantData -> python value ----------------------------------------


def _variant_to_python(variant):
    """A plain python value from a Navisworks VariantData, probed in this
    PINNED order:

        IsDisplayString    -> str    (ToDisplayString)
        IsIdentifierString -> str    (ToIdentifierString)
        IsBoolean          -> bool   (ToBoolean)
        IsInt32            -> int    (ToInt32)
        IsDateTime         -> datetime.datetime - via ToDateTime() if the
                               variant exposes it, else the ToString() text
                               (see _variant_datetime)
        any IsDouble*      -> float  (ToDouble) - IsDouble, IsDoubleLength,
                               IsDoubleArea, IsDoubleVolume, IsDoubleAngle,
                               and any other IsDouble-prefixed probe the live
                               VariantData exposes (see _is_double_kind)
        IsPoint3D          -> (x, y, z) float tuple (ToPoint3D)
        anything else      -> variant.ToString()

    See the module docstring for why this order is pinned down and tested
    even though the Is* probes are documented exclusive on any real value.
    """
    if getattr(variant, 'IsDisplayString', False):
        return variant.ToDisplayString()
    if getattr(variant, 'IsIdentifierString', False):
        return variant.ToIdentifierString()
    if getattr(variant, 'IsBoolean', False):
        return bool(variant.ToBoolean())
    if getattr(variant, 'IsInt32', False):
        return int(variant.ToInt32())
    if getattr(variant, 'IsDateTime', False):
        return _variant_datetime(variant)
    if _is_double_kind(variant):
        return float(variant.ToDouble())
    if getattr(variant, 'IsPoint3D', False):
        return _point3d_tuple(variant.ToPoint3D())
    return variant.ToString()


def _is_double_kind(variant):
    """True if any attribute named IsDouble* is True on variant.

    Checked by scanning dir(variant) for the IsDouble prefix, rather than a
    hardcoded list of the Double subtypes named in the plan's Global
    Constraints, so a VariantData exposing a Double probe this module was
    not written against still reads back as a float instead of falling all
    the way through to the ToString() text.
    """
    for name in dir(variant):
        if name.startswith('IsDouble') and getattr(variant, name, False):
            return True
    return False


def _variant_datetime(variant):
    """The DateTime probe's python value: ToDateTime(), converted to a
    datetime.datetime if it is not already one (a real VariantData's
    ToDateTime hands back a .NET System.DateTime; a python test stub can
    hand back a datetime.datetime directly and is returned unchanged), or
    variant.ToString() if the variant exposes no ToDateTime at all - see the
    module docstring's ToDateTime VERIFY-at-smoke note."""
    to_datetime = getattr(variant, 'ToDateTime', None)
    if to_datetime is None:
        return variant.ToString()
    value = to_datetime()
    if isinstance(value, datetime.datetime):
        return value
    return datetime.datetime(
        value.Year, value.Month, value.Day,
        value.Hour, value.Minute, value.Second,
        int(getattr(value, 'Millisecond', 0)) * 1000)


def _point3d_tuple(point):
    """(x, y, z) floats from a Point3D-like object: a real Point3D exposes
    X/Y/Z properties; a plain 3-item sequence (e.g. a test fake) is accepted
    too."""
    x = getattr(point, 'X', None)
    y = getattr(point, 'Y', None)
    z = getattr(point, 'Z', None)
    if x is None or y is None or z is None:
        x, y, z = point
    return (float(x), float(y), float(z))
