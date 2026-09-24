"""Camera and visibility moves that follow a selection.

Zooming onto the current selection has no .NET equivalent: the COM
InwOpState10 object is the only route, so zoom_selected goes through
pynavis._com. Isolating is plain .NET. Every Navisworks import is deferred
into the function bodies, the way viewpoints.transaction does it, so this
module still imports outside a session.
"""


def zoom_selected(doc=None):
    """Zooms the current view onto the current selection.

    Returns True when the view moved and False when the host refused. The
    caller should carry on either way: a tool that already selected the right
    elements has done its job, and failing it over the camera would be worse
    than a view that did not move. The reason lands in the log.

    doc is accepted for signature symmetry with the rest of the module; the
    COM state always acts on the active document.
    """
    from pynavis import _com

    try:
        _com.get_state().ZoomInCurViewOnCurSel()
        return True
    except Exception as error:
        _log_failure('Zoom to selection failed', error)
        return False


def aspect_ratio(doc=None):
    """The render window's width over height, refreshed before it is read.

    A Viewpoint copy can hand back a STALE AspectRatio: the value it was made
    with rather than the window's current one. That matters to anything turning a
    horizontal field of view into the vertical angle Navisworks actually stores,
    because the whole conversion divides by this number, and a ratio that is off
    by a thousandth puts the lens out by a fraction of a degree.

    Committing the current viewpoint back onto itself is what makes Navisworks
    refresh the ratio from the real render window. It changes nothing on screen,
    since the viewpoint written is the one already showing.

    Returns None when there is no usable ratio, so callers skip the field of view
    rather than dividing by a number they cannot trust.
    """
    from pynavis import doc as _doc

    document = doc if doc is not None else _doc.get_doc()
    try:
        current = document.CurrentViewpoint
        current.CopyFrom(current.CreateCopy())
        ratio = float(current.CreateCopy().AspectRatio)
    except Exception as error:
        _log_failure('Could not read the viewport aspect ratio', error)
        return None
    # Written as a range test so NaN, which fails every comparison, falls out
    # here beside zero, negatives and infinity.
    return ratio if 0.0 < ratio < float('inf') else None


def isolate(items, doc=None):
    """Hides everything except items, and returns how many were kept.

    Navisworks resolves visibility at the leaf, so hiding every root item and
    then unhiding the wanted ones leaves exactly those on screen. Both calls
    sit inside one transaction, so Ctrl+Z restores the model in a single step;
    Navisworks' own Unhide All (and unhide_all below) clears it too.

    An empty items is a no-op rather than a whole-model blackout.
    """
    from pynavis import doc as _doc
    from pynavis._api import Api
    from pynavis.viewpoints import transaction

    document = doc if doc is not None else _doc.get_doc()

    keep = Api.ModelItemCollection()
    for item in items:
        keep.Add(item)
    if not keep.Count:
        return 0

    roots = Api.ModelItemCollection()
    models = document.Models
    for index in range(models.Count):
        roots.Add(models[index].RootItem)

    with transaction('Isolate', document):
        document.Models.SetHidden(roots, True)
        document.Models.SetHidden(keep, False)
    return keep.Count


def unhide_all(doc=None):
    """Clears every hidden item in the document, in one undo step."""
    from pynavis import doc as _doc
    from pynavis.viewpoints import transaction

    document = doc if doc is not None else _doc.get_doc()
    with transaction('Unhide All', document):
        document.Models.ResetAllHidden()


def _log_failure(message, error):
    """Best-effort logging: outside a command there may be no logger at all,
    and a failed zoom must not become a second, louder failure."""
    try:
        from pynavis import script
        script.get_logger().error('%s: %s' % (message, error))
    except Exception:
        pass
