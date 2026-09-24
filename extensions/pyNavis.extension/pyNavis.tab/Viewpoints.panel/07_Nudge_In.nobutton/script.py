"""Nudge In (Ctrl+Alt+Up): the Section Nudge action 'in', from the keyboard over the
3D view, no panel needed. lib/sectionnudge.py holds the behaviour."""

import sectionnudge

from pynavis import toast

result = sectionnudge.run_action('in')
toast.show('warning' if result['problem'] else 'info', result['toast'])
