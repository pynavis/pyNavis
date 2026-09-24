"""Pastes a kind of view state copied earlier with Copy State.

Only the kinds actually copied for this document are offered; with nothing
copied yet the tool says so instead of showing an empty picker.
"""

from pynavis import forms, toast, viewstate

kinds = viewstate.available_kinds()

if not kinds:
    forms.alert('No copied state for this document. Use "Copy State" first.',
                title='Paste State')
else:
    # prompt=None gives the chromeless quick switch, same as Copy State.
    label = forms.ask_options(
        None,
        [viewstate.label_for(kind) for kind in kinds],
        title='Paste State',
    )
    if label is not None:                      # None means cancelled: say nothing
        result = viewstate.paste_state(viewstate.kind_for(label))
        toast.show(result.level, result.message, result.detail)
