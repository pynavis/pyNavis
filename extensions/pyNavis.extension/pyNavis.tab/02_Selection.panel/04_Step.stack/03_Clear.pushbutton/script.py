"""Empties the memory for this document."""

from pynavis import memory, toast

result = memory.clear()
toast.show(result.level, result.message, result.detail)
