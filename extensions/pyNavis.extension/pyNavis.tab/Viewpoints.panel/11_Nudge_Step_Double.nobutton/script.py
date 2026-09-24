"""Nudge Step Double (Ctrl+Alt+PgUp): the Section Nudge action 'double', from the keyboard over the
3D view, no panel needed. lib/sectionnudge.py holds the behaviour."""

import sectionnudge

from pynavis import toast

result = sectionnudge.run_action('double')
toast.show('warning' if result['problem'] else 'info', result['toast'])
