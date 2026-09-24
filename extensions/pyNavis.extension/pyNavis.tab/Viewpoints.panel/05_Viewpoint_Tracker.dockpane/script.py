# -*- coding: utf-8 -*-
"""Shows which saved viewpoint the camera is looking through, live.

Two lines: whether the camera is still on the view or has walked off it, and
the view's name. Clicking the name goes back to that view.

All of the behaviour is in lib/vptrackerlive.py, which the floating Tracker
Window shares, so the panel and the window can never disagree.

Reload re-runs this script against fresh TextBlocks: PaneRegistry.Configure is
"Boot and Reload: re-read the claims and re-content any open pane", and it
re-contents through PaneContentBuilder.Build. The previous run's timer and
subscriptions are still live at that point and would go on painting into
TextBlocks that are no longer on screen, so the last detach is kept in the
session holder (which outlives a run) and called before a new one is wired.
"""

import vptrackerlive

from pynavis import script

pane = __pane__

held = vptrackerlive.session()

previous = getattr(held, 'pane_detach', None)
if previous is not None:
    previous()              # self-guarded: detach() wraps its own body

held.pane_detach = vptrackerlive.attach(
    pane.Find('Status'), pane.Find('ViewName'), script.get_logger())['detach']
