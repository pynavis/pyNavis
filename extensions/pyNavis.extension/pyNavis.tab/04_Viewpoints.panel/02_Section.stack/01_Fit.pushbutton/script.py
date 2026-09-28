"""Fits the six section planes to the current selection.

Navisworks' own Fit Section to Selection gives a world-aligned box; this turns
the box to the objects, so a diagonal run gets planes parallel to the run
instead of four corners of empty space. Shift+Click sets the padding.
"""

from pynavis import section, selection, settings, toast

values = settings.load(section.TOOL, section.DEFAULTS)
result = section.fit_to_selection(selection.get_items(), values)
toast.show(result.level, result.message, result.detail)
