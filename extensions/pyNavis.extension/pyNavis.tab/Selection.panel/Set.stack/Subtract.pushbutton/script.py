"""Removes the current selection from memory."""

from pynavis import memory, toast

result = memory.subtract()
toast.show(result.level, result.message, result.detail)
