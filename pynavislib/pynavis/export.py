# -*- coding: utf-8 -*-
"""NWD save, publish, and best-effort viewpoint image export.

The pure half (_PUBLISH_DEFAULTS/_OPTIONS_FIELDS/_PROPS_FIELDS/_publish_plan)
has no Navisworks dependency and is unit-tested directly; the API half
(save_nwd, publish_nwd, viewpoint_image) touches the live document and
lazy-imports pynavis._api / pynavis.doc / pynavis._com inside each function,
so importing this module never needs a live Navisworks session.

save_nwd wraps Document.SaveFile(string) / SaveFile(string,
DocumentFileVersion). version, when given, is a four-digit year like 2023
that maps onto the live DocumentFileVersion enum member 'Navisworks%d' %
version - reflected against Navisworks 2026's Autodesk.Navisworks.Api.dll:
the enum has Current = 0 plus one Navisworks<year> member per year from 2015
through 2026 (2015 alone carries a distinct underlying value; 2016-2025 all
share one file-format value, and 2026 carries its own - the point release,
not the numeric value, is what this module exposes). version=None (the
default) calls SaveFile(path) directly - the CURRENT format - without
touching DocumentFileVersion at all. An unrecognized year raises ValueError
naming the years the live enum actually supports, checked by reflecting the
enum itself rather than a hardcoded range, so it stays correct as new
Navisworks releases add members.

publish_nwd wraps Document.PublishFile(string, NwdExportOptions,
PublishProperties) - see _publish_plan for how its **props kwargs split
across the two option objects. FileVersion on NwdExportOptions is left at
its live default (this module exposes no publish-time version kwarg; use
save_nwd first if a specific format is required).

viewpoint_image has NO .NET API route at all (see the plan's Global
Constraints and clash.py's module docstring for the same trap elsewhere):
it drives the COM image-export plugin ('lcodpimage') via
state.DriveIOPlugin(internal_name, filename, options), state coming from
pynavis._com.get_state() - see _com.py's module docstring for the shared
ComApiBridge/ObjectFactory background this function reuses (nwOaPropertyVec/
nwOaProperty exactly like _com.set_custom_tab builds). It is SMOKE-VERIFIED
ONLY: the plugin's own option names are not part of any reflectable
metadata (InwOaProperty is a schemaless name/value pair) and this module's
guess at 'width'/'height' has not been confirmed against a live export -
see viewpoint_image's own docstring. Every failure path, an exception or a
non-OK export status, is reported as a single RuntimeError naming path,
because COM callback exceptions can vanish silently on the Navisworks side.
"""

import datetime


# --- pure: publish_nwd kwarg planning -----------------------------------------

# publish_nwd kwarg -> its default. Booleans always have a real default and so
# always appear in the planned dict; the rest default to None, meaning "leave
# that field at PublishProperties' own default" and are omitted when unset.
_PUBLISH_DEFAULTS = {
    'keywords': None,
    'comments': None,
    'published_for': None,
    'copyright': None,
    'allow_resave': True,
    'display_on_open': False,
    'expiry': None,
    'exclude_hidden': False,
    'embed_xrefs': True,
}

# publish_nwd kwarg -> live NwdExportOptions property name (both settable
# bool, reflected against Navisworks 2026's Autodesk.Navisworks.Api.dll).
_OPTIONS_FIELDS = {
    'exclude_hidden': 'ExcludeHiddenItems',
    'embed_xrefs': 'EmbedXrefs',
}

# publish_nwd kwarg -> live PublishProperties property name, reflected the
# same way (Keywords/Comments/PublishedFor/Copyright/ExpiryDate are strings
# or a DateTime; AllowResave/DisplayOnOpen are bool).
_PROPS_FIELDS = {
    'keywords': 'Keywords',
    'comments': 'Comments',
    'published_for': 'PublishedFor',
    'copyright': 'Copyright',
    'allow_resave': 'AllowResave',
    'display_on_open': 'DisplayOnOpen',
    'expiry': 'ExpiryDate',
}


def _publish_plan(kwargs):
    """(options_dict, props_dict) - the two live-property-name dicts
    publish_nwd feeds straight onto NwdExportOptions and PublishProperties
    via setattr, from kwargs = publish_nwd's **props.

    Unknown keys raise ValueError naming the offending key(s) plus the
    complete valid list (sorted, for a deterministic message - same shape
    as clashtest._map_lookup). expiry, if given, must be a
    datetime.datetime; anything else (a string, a date, None is fine and
    means "not given") raises ValueError naming the type.

    Missing kwargs take the _PUBLISH_DEFAULTS default. The four string
    fields (keywords/comments/published_for/copyright) and expiry are
    omitted from props_dict entirely when left at their None default -
    PublishProperties is not touched for a field the caller never set,
    rather than being handed an explicit empty string or an unset DateTime.
    allow_resave and display_on_open always have a real (non-None) default
    and so always appear in props_dict; exclude_hidden and embed_xrefs
    likewise always appear in options_dict.
    """
    unknown = sorted(set(kwargs) - set(_PUBLISH_DEFAULTS))
    if unknown:
        raise ValueError(
            'unknown publish_nwd option(s): %s; valid options are %s' %
            (', '.join(repr(key) for key in unknown),
             ', '.join(sorted(_PUBLISH_DEFAULTS))))

    merged = dict(_PUBLISH_DEFAULTS)
    merged.update(kwargs)

    expiry = merged['expiry']
    if expiry is not None and not isinstance(expiry, datetime.datetime):
        raise ValueError(
            'expiry must be a datetime.datetime, not %s' % type(expiry).__name__)

    options = dict((field, merged[key]) for key, field in _OPTIONS_FIELDS.items())
    props = dict(
        (field, merged[key])
        for key, field in _PROPS_FIELDS.items() if merged[key] is not None)
    return options, props


# --- API half (Navisworks only; lazy imports keep the module pure) -----------


def save_nwd(path, doc=None, version=None):
    """Saves the document as an NWD at path via Document.SaveFile.

    version, if given, is a four-digit year like 2023 mapping onto the live
    DocumentFileVersion enum member 'Navisworks%d' % version - see the
    module docstring. version=None (the default) calls SaveFile(path)
    directly, saving in the CURRENT format without touching
    DocumentFileVersion at all.

    An unsupported year raises ValueError naming every year the live enum
    actually supports, before any file is written.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api
    d = doc if doc is not None else _doc.get_doc()

    if version is None:
        d.SaveFile(path)
        return

    member = 'Navisworks%d' % version
    file_version = getattr(Api.DocumentFileVersion, member, None)
    if file_version is None:
        years = sorted(
            int(name[len('Navisworks'):]) for name in dir(Api.DocumentFileVersion)
            if name.startswith('Navisworks') and name[len('Navisworks'):].isdigit())
        raise ValueError(
            'unsupported NWD version %r; expected one of %s' %
            (version, ', '.join(str(year) for year in years)))
    d.SaveFile(path, file_version)


def publish_nwd(path, doc=None, **props):
    """Publishes the document as an NWD at path via Document.PublishFile,
    which additionally carries a PublishProperties record (title metadata,
    an optional expiry/password-style lock, and whether the recipient may
    resave it) alongside the same export scoping NwdExportOptions offers.

    props accepts: keywords, comments, published_for, copyright (strings),
    allow_resave (default True), display_on_open (default False), expiry
    (a datetime.datetime or None, default None), exclude_hidden (default
    False), embed_xrefs (default True) - see _publish_plan for exactly how
    each maps onto NwdExportOptions/PublishProperties and for the
    ValueError raised on an unknown kwarg or a non-datetime expiry, both
    checked before any Navisworks import.
    """
    options_fields, props_fields = _publish_plan(props)

    from pynavis import doc as _doc
    from pynavis._api import Api
    d = doc if doc is not None else _doc.get_doc()

    options = Api.NwdExportOptions()
    for field, value in options_fields.items():
        setattr(options, field, value)

    properties = Api.PublishProperties()
    for field, value in props_fields.items():
        if field == 'ExpiryDate':
            value = _to_net_datetime(value)
        setattr(properties, field, value)

    d.PublishFile(path, options, properties)


# Best-guess lcodpimage plugin option names for width/height - not a
# reflected fact (see viewpoint_image's SMOKE-VERIFIED ONLY docstring note);
# adjust here if a live state.GetIOPluginOptions('lcodpimage') call reports
# different names.
_IMAGE_OPTION_NAMES = {'width': 'width', 'height': 'height'}


def viewpoint_image(path, doc=None, width=1920, height=1080):
    """Writes the current view as an image at path via the COM image-export
    plugin ('lcodpimage'), the only export route for images at all - see
    the module docstring.

    SMOKE-VERIFIED ONLY: this function's 'width'/'height' option names are
    a best guess, not a reflected fact (InwOaProperty carries a schemaless
    name/value pair, so there is nothing to reflect); if an exported image
    comes out at the plugin's own default size rather than width x height,
    re-check the option names the live plugin actually reports via
    state.GetIOPluginOptions('lcodpimage') in-app and adjust _IMAGE_OPTION_NAMES.

    doc is accepted for signature symmetry with the rest of this module but
    is not used: the COM image export runs off the process-global COM state
    (pynavis._com.get_state(), the ComApiBridge's view of the ACTIVE
    document), which no per-call argument can redirect - passing a
    non-active document does not export that document's view.

    Best-effort: any failure - a raised exception, or DriveIOPlugin
    returning anything other than eExport_OK - is reported as a single
    RuntimeError naming path, rather than letting a plugin-specific COM
    error (which can vanish silently on the Navisworks side to begin with,
    see _com.py's module docstring) surface directly. The COM imports sit
    INSIDE the try for that reason: outside it, an ImportError from a
    missing Interop assembly would escape as its own exception type and
    break the single-RuntimeError contract this docstring promises.
    """
    try:
        from pynavis import _com
        from Autodesk.Navisworks.Api.Interop.ComApi import nwEExportStatus

        state = _com.get_state()
        vec = state.GetIOPluginOptions('lcodpimage')
        if vec is None:
            from Autodesk.Navisworks.Api.Interop.ComApi import nwEObjectType
            vec = state.ObjectFactory(nwEObjectType.eObjectType_nwOaPropertyVec, None, None)
        _set_plugin_option(state, vec, _IMAGE_OPTION_NAMES['width'], int(width))
        _set_plugin_option(state, vec, _IMAGE_OPTION_NAMES['height'], int(height))
        status = state.DriveIOPlugin('lcodpimage', path, vec)
    except Exception as error:
        raise RuntimeError('viewpoint image export to %r failed: %s' % (path, error))

    if status != nwEExportStatus.eExport_OK:
        raise RuntimeError(
            'viewpoint image export to %r failed with status %r' % (path, status))


def _set_plugin_option(state, vec, name, value):
    """Sets name=value on an InwOaPropertyVec, updating an existing
    same-named (case-insensitive) property in place or appending a new one
    via state.ObjectFactory when absent - the same ObjectFactory/
    Properties() plumbing pynavis._com.set_custom_tab uses for custom
    property tabs, reused here rather than duplicated."""
    from Autodesk.Navisworks.Api.Interop.ComApi import nwEObjectType
    for prop in vec.Properties():
        if str(prop.name).lower() == name.lower():
            prop.value = value
            return
    prop = state.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, None, None)
    prop.name = name
    prop.UserName = name
    prop.value = value
    vec.Properties().Add(prop)


def _to_net_datetime(value):
    """A System.DateTime for a python datetime.datetime, for
    PublishProperties.ExpiryDate (a live DateTime property, not nullable -
    see the module docstring's reflected property list)."""
    from System import DateTime
    return DateTime(
        value.year, value.month, value.day,
        value.hour, value.minute, value.second,
        value.microsecond // 1000)
