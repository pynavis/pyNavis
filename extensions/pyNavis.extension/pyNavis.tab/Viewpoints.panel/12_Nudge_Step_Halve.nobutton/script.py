"""Nudge Step Halve (Ctrl+Alt+PgDn): the Section Nudge action 'halve', from the keyboard over the
3D view, no panel needed. lib/sectionnudge.py holds the behaviour."""

import sectionnudge

from pynavis import toast

result = sectionnudge.run_action('halve')
toast.show('warning' if result['problem'] else 'info', result['toast'])
