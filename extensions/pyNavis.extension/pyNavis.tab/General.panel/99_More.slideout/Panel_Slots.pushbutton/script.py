"""Adds pyNavis panel slots beyond the five that ship with the loader."""
from pynavis import forms, panes, toast

current = panes.slot_count()
extra = forms.ask_number(
    'Panel slots available now: %d.\nHow many extra slots do you want?' % current,
    default=5, min_value=0, max_value=95, title='Panel slots')
if extra is None:
    pass   # cancelled: stay silent
else:
    try:
        total = panes.add_slots(extra)
        toast.success('Panel slots: %d after restart' % total,
                      'Restart Navisworks to register the new slots.')
    except Exception as ex:
        toast.error('Could not add panel slots', str(ex))
