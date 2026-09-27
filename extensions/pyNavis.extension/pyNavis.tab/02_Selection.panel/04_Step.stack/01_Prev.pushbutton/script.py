"""Selects the previous item in memory and zooms to it.

Wraps around at the start. The camera move cannot be undone.
"""

from pynavis import memory, toast

result = memory.step_prev()
toast.show(result.level, result.message, result.detail)
