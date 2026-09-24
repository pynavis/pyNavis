"""Selects whatever is in memory.

Items that are no longer in the model are reported and skipped.
"""

from pynavis import memory, toast

result = memory.recall()
toast.show(result.level, result.message, result.detail)
