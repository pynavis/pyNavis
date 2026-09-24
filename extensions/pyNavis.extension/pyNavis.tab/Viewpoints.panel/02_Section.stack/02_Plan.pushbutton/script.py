"""Fits the section planes, then swings to an orthographic plan of them.

The camera turns with the box, so the objects sit square on screen rather than
skewed across it.
"""

from pynavis import section, selection, settings, toast

values = settings.load(section.TOOL, section.DEFAULTS)
result = section.plan_to_selection(selection.get_items(), values)
toast.show(result.level, result.message, result.detail)
