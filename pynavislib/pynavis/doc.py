"""Active-document helpers: metadata, saved viewpoints, model item properties."""

from pynavis import _util
from pynavis._api import Api, Application


def get_doc():
    return Application.ActiveDocument


def get_title(doc=None):
    """Document title (file name without path for saved documents)."""
    d = doc if doc is not None else get_doc()
    return d.Title


def get_filename(doc=None):
    """Full path of the open file; empty string for an unsaved document."""
    d = doc if doc is not None else get_doc()
    return d.FileName


def walk_saved_viewpoints(doc=None):
    """Yields (folder_names, saved_item) for every saved viewpoint/animation in
    the document, depth-first, flattening viewpoint folders. folder_names is the
    list of ancestor folder display names (empty for top-level items).

    Only FolderItems are descended into: animations are GroupItems too, but must
    come out as one item each, not as their cuts - see _util.flatten_with_path.
    """
    d = doc if doc is not None else get_doc()
    return _util.flatten_with_path(
        d.SavedViewpoints.RootItem.Children,
        is_folder=lambda item: isinstance(item, Api.FolderItem))


def get_property(item, category_name, property_name):
    """A model item's property VariantData by display names, or None."""
    prop = item.PropertyCategories.FindPropertyByDisplayName(category_name, property_name)
    return None if prop is None else prop.Value
