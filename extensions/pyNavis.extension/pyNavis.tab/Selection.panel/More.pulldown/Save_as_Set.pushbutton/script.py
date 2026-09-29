"""Saves the memory as a Navisworks selection set.

Unlike the memory itself, a set is stored in the document, so this marks the
document as modified.
"""

import datetime

from pynavis import forms, memory, toast

default = 'Memory %s' % datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
name = forms.ask_string('Name for the selection set', default, 'Save memory as set')
if name:
    result = memory.save_as_set(name)
    toast.show(result.level, result.message, result.detail)
