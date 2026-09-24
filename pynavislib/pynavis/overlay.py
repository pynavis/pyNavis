"""Draw labelled lines in the 3D view.

Items are world-coordinate line segments plus an optional label, redrawn on
every frame by the pyNavis overlay plugin, so they follow orbit and zoom.
Points are plain (x, y, z) tuples in model units. Every call returns True
when the overlay plugin is loaded and False (logged) when it is not, which
is what an un-deployed loader looks like: the script's own result is still
fine, only the drawing is missing.

An item given anchor=(first, end) belongs to the native point-to-point
measurement with those two points and disappears the first frame that
measurement changes or is cleared. Anything else stays until clear().
"""

import clr

clr.AddReference('PyNavis.Runtime')

from System import Array, Double
from PyNavis.Runtime.Overlay import OverlayItem, OverlayRegistry, OverlaySegment


def _xyz(point):
    if point is None:
        return None
    x, y, z = point
    return Array[Double]([float(x), float(y), float(z)])


def add(tag, segments, label=None, label_at=None, anchor=None):
    """Draws segments [((x,y,z), (x,y,z), dashed), ...] under tag, replacing
    any earlier item with that tag. label is pinned to label_at."""
    item = OverlayItem()
    item.Tag = str(tag)
    for start, end, dashed in segments:
        seg = OverlaySegment()
        seg.From = _xyz(start)
        seg.To = _xyz(end)
        seg.Dashed = bool(dashed)
        item.Segments.Add(seg)
    if label is not None and label_at is not None:
        item.Label = str(label)
        item.LabelAt = _xyz(label_at)
    if anchor is not None:
        first, end = anchor
        item.AnchorFirst = _xyz(first)
        item.AnchorEnd = _xyz(end)
    OverlayRegistry.Add(item)
    return _loaded()


def dimension(tag, start, end, label, extension_to=None, anchor=None):
    """A dimension: a solid line start-end with label at its midpoint, and an
    optional dashed extension from end to extension_to."""
    segments = [(start, end, False)]
    if extension_to is not None:
        segments.append((end, extension_to, True))
    mid = tuple((float(a) + float(b)) / 2.0 for a, b in zip(start, end))
    return add(tag, segments, label=label, label_at=mid, anchor=anchor)


def clear(tag=None):
    """Removes the items under tag, or every item when tag is None."""
    OverlayRegistry.Clear(None if tag is None else str(tag))
    return _loaded()


def redraw():
    """Asks the view to repaint its overlay once pending events have run.
    Call after add/dimension/clear so the change shows without a nudge."""
    loaded = _loaded()
    OverlayRegistry.Redraw()
    return loaded


def _loaded():
    return bool(OverlayRegistry.EnsureLoaded())
