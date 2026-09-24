"""Smoke fixture: fires on selection changes (debounced 250ms).

Rapid clicking should yield one toast per pause, never one per click.
While a pyNavis command is running this hook is skipped (see the log).
"""
from pynavis import selection, toast

toast.info('Hook selection-changed: %d item(s)' % len(list(selection.get_items())))
