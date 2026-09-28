"""Nudge Out (Ctrl+Alt+Down): the Section Nudge action 'out', from the keyboard over the
3D view, no panel needed. lib/sectionnudge.py holds the behaviour."""

import sectionnudge

from pynavis import toast

result = sectionnudge.run_action('out')
toast.show('warning' if result['problem'] else 'info', result['toast'])
