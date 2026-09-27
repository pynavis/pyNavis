"""Deletes the stored memory files for every document, not just this one."""

from pynavis import forms, memory, toast

targets = memory.purge_targets()
if not targets:
    toast.info('No memory files to purge')
else:
    question = ('Delete %d stored memory file(s)?\n\n'
                'This clears the memory for every document, not just this one.'
                % len(targets))
    if forms.confirm(question, 'Purge memory files'):
        result = memory.purge()
        toast.show(result.level, result.message, result.detail)
