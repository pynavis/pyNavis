"""Context gating demo: this button is greyed out until something is selected."""
from pynavis import selection, toast

toast.info('%d item(s) selected' % len(list(selection.get_items())))
