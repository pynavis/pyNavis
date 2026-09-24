"""Stores the current selection in memory.

The memory belongs to this document and survives closing Navisworks.
"""

from pynavis import memory, toast

result = memory.memorize()
toast.show(result.level, result.message, result.detail)
