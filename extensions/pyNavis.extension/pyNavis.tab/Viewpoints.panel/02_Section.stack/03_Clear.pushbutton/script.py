"""Switches sectioning off, leaving the camera where it is."""

from pynavis import section, toast

result = section.clear()
toast.show(result.level, result.message, result.detail)
