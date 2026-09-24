"""The current selection: read, replace, clear."""

from pynavis._api import Api, Application


def _doc(doc):
    return doc if doc is not None else Application.ActiveDocument


def get_items(doc=None):
    """The currently selected ModelItems as a python list."""
    return list(_doc(doc).CurrentSelection.SelectedItems)


def set_items(items, doc=None):
    """Replaces the current selection with the given ModelItems."""
    collection = Api.ModelItemCollection()
    for item in items:
        collection.Add(item)
    _doc(doc).CurrentSelection.CopyFrom(collection)


def clear(doc=None):
    _doc(doc).CurrentSelection.Clear()
