"""Adds the current selection to memory."""

from pynavis import memory, toast

result = memory.add()
toast.show(result.level, result.message, result.detail)
