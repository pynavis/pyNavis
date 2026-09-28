"""Nudge Previous Target (Ctrl+Alt+Left): the Section Nudge action 'prev', from the keyboard over the
3D view, no panel needed. lib/sectionnudge.py holds the behaviour."""

import sectionnudge

from pynavis import toast

result = sectionnudge.run_action('prev')
toast.show('warning' if result['problem'] else 'info', result['toast'])
