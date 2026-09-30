"""Chooses which tabs Hide Tabs takes off the ribbon.

Tabs that are off the ribbon cannot be picked, so while they are off they
go back for the picker and come off again afterwards: with the new choice
when there is one, with the old one on cancel.
"""

import tabhider

from pynavis import script, settings, toast

was_hidden = tabhider.hidden()
if was_hidden:
    tabhider.show()

chosen = tabhider.choose()
if was_hidden:
    tabhider.hide(chosen if chosen is not None
                  else settings.load(tabhider.TOOL, tabhider.DEFAULTS)['tabs'])
    script.set_toggle_state(tabhider.hidden())

if chosen is not None:                     # None means cancelled: say nothing
    count = '%d tab%s' % (len(chosen), '' if len(chosen) == 1 else 's')
    if was_hidden:
        toast.success('Saved', 'The new choice of %s is off the ribbon now.' % count)
    else:
        toast.success('Saved', 'Click Hide Tabs to take off the %s you chose.' % count)
