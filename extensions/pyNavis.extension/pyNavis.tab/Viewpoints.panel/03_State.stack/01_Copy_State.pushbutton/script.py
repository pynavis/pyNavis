"""Copies one kind of view state to disk, per document.

Pick section state, hidden items, or appearance overrides; Paste State brings
it back later in this document, including in another Navisworks session.
"""

from pynavis import forms, toast, viewstate

# prompt=None gives the chromeless quick switch: the button labels ARE the
# question, so the window carries no titlebar and no prompt text.
label = forms.ask_options(
    None,
    [viewstate.label_for(kind) for kind in viewstate.KINDS],
    title='Copy State',
)

if label is not None:                          # None means cancelled: say nothing
    result = viewstate.copy_state(viewstate.kind_for(label))
    toast.show(result.level, result.message, result.detail)
