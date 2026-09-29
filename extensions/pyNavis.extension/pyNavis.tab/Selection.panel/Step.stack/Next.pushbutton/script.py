"""Selects the next item in memory and zooms to it.

Wraps around at the end. The camera move cannot be undone.
"""

from pynavis import memory, toast

result = memory.step_next()
toast.show(result.level, result.message, result.detail)
