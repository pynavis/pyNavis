"""Nudge Next Target (Ctrl+Alt+Right): the Section Nudge action 'next', from the keyboard over the
3D view, no panel needed. lib/sectionnudge.py holds the behaviour."""

import sectionnudge

from pynavis import toast

result = sectionnudge.run_action('next')
toast.show('warning' if result['problem'] else 'info', result['toast'])
