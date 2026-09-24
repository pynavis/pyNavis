# -*- coding: utf-8 -*-
"""Opens the viewpoint tracker as a floating window that stays on top.

The same two lines as the Viewpoint Tracker panel, in a window that can sit
over another application while you walk the model. Clicking the name goes back
to that view. The behaviour is shared: both shells call
lib/vptrackerlive.attach.

Clicking again focuses the window that is already open rather than stacking a
second one on top of it.
"""

import clr

import vptrackerlive

from pynavis import forms, script

clr.AddReference('PyNavis.Runtime')
clr.AddReference('WindowsFormsIntegration')

from System.Windows.Forms.Integration import ElementHost

from PyNavis.Runtime.Forms import FluentChrome


def run():
    held = vptrackerlive.session()

    existing = held.window
    if existing is not None:
        try:
            if existing.IsLoaded:
                existing.Activate()
                return                  # already open: bring it forward, say nothing
        except Exception:
            pass                        # it was closed out from under us; rebuild
        held.window = None

    win = forms.WPFWindow('window.xaml')
    window = win.window
    live = vptrackerlive.attach(win['Status'], win['ViewName'], script.get_logger())

    def on_closed(sender, args):
        live['detach']()
        held.window = None

    window.Closed += on_closed

    # Owns the window to the Navisworks main window and themes its titlebar.
    FluentChrome.Apply(window)

    # A modeless WPF window rides the HOST's message pump, which preprocesses
    # keyboard messages itself: most keydowns and every WM_CHAR die before the
    # window's WndProc. This window takes no typing, but Esc and the system
    # menu are keyboard too, and the call costs nothing. It has to run BEFORE
    # Show(). Modal ShowDialog windows pump for themselves and never need it.
    ElementHost.EnableModelessKeyboardInterop(window)

    window.Show()
    held.window = window
    # No toast. An always-on-top window centred on screen is its own report,
    # the way the dock panel's toggle is, and a toast on top of it is noise.


if '__commandpath__' in globals():
    run()
