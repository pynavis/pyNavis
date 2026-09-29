"""Keeps only the items that are in memory and selected right now."""

from pynavis import memory, toast

result = memory.intersect()
toast.show(result.level, result.message, result.detail)
