# -*- coding: utf-8 -*-
"""The live Navisworks half of the Viewpoint Tracker, shared by both shells.

The dock panel and the floating window are two pieces of XAML around the same
behaviour: watch the document, work out which saved view the camera is on, and
write the two lines vptracker decides. attach() wires a pair of TextBlocks to
the document and hands back a detach callable.

It subscribes to Navisworks directly rather than going through a pyNavis hook,
because hooks are skipped while any pyNavis command is running. A panel that
stopped updating whenever another tool ran would be worse than no panel.

No f-strings: this runs on IronPython 3.4 as well as CPython.
"""

import sys
import types

import clr

import vptracker

from pynavis._api import Api, Application

clr.AddReference('WindowsBase')

from System import TimeSpan
from System.Windows.Threading import DispatcherPriority, DispatcherTimer

# The camera event fires continuously while the user orbits or walks.
# Repainting on every one of them would copy a viewpoint per frame on the UI
# thread, so they collapse into one repaint this long after movement stops.
DEBOUNCE_MS = 250

# sys.modules key for state that has to outlive a single run. Everything under
# <extension>\lib is deleted from sys.modules before every run so edits take
# effect on the next click, which also means a module global here is a fresh
# None every time. A module object under a key of our own is never touched by
# that sweep, so it lasts for the engine's life.
_SESSION_KEY = 'pynavis_viewpoint_tracker_session'


def session():
    """The holder that survives between runs. Has `window` and `pane_detach`."""
    holder = sys.modules.get(_SESSION_KEY)
    if holder is None:
        holder = types.ModuleType(_SESSION_KEY)
        holder.window = None
        holder.pane_detach = None
        sys.modules[_SESSION_KEY] = holder
    return holder


def attach(status_block, name_block, log):
    """Wires two TextBlocks to the active document's viewpoint events.

    Returns {'detach': callable}. The floating window calls it when it closes;
    the dock panel never does, because a pyNavis panel is built once and lives
    for the session.
    """
    # Lists, not bare names: every handler below runs long after the caller
    # returned, so they close over a container instead of rebinding.
    tracked = [None]            # the SavedViewpoint being shown, or None
    subscribed = [None]         # the Document whose events we are on, or None

    timer = DispatcherTimer(DispatcherPriority.Background)
    timer.Interval = TimeSpan.FromMilliseconds(DEBOUNCE_MS)

    def safely(work):
        """Runs a handler, logging anything it throws.

        An exception escaping a .NET event handler on the UI thread takes
        Navisworks down with it, and a tracker panel is not worth that.
        """
        try:
            work()
        except Exception as error:
            log.error('Viewpoint Tracker: %s' % error)

    def alive(saved):
        """A SavedViewpoint handle dies when its document closes or its tree is
        edited, and touching a dead one throws rather than returning anything."""
        if saved is None:
            return False
        try:
            saved.DisplayName
            return True
        except Exception:
            return False

    def camera_of(viewpoint):
        try:
            position = viewpoint.Position
            rotation = viewpoint.Rotation
            return ((position.X, position.Y, position.Z),
                    (rotation.A, rotation.B, rotation.C, rotation.D))
        except Exception:
            return (None, None)

    def is_active(document, saved):
        # CreateCopy, not CurrentViewpoint.Value: the Value handle does not
        # follow the live camera, so comparing against it reports "active" no
        # matter where the user walked.
        current = camera_of(document.CurrentViewpoint.CreateCopy())
        stored = camera_of(saved.Viewpoint)
        return vptracker.matches(current[0], current[1], stored[0], stored[1])

    def paint():
        saved = tracked[0]
        if not alive(saved):
            tracked[0] = None
            status_block.Text = ''
            name_block.Text = vptracker.NOTHING
            return
        name = saved.DisplayName
        document = Application.ActiveDocument
        active = document is not None and is_active(document, saved)
        status_block.Text = vptracker.status_line(name, active)
        name_block.Text = vptracker.display_name(name)

    def on_tick(sender, args):
        def work():
            timer.Stop()
            paint()
        safely(work)

    def on_camera_moved(sender, args):
        # Wrapped like every other registered handler. This one fires
        # continuously while the user orbits, so it is the last place an
        # escaping exception should be allowed to reach Navisworks.
        def work():
            timer.Stop()
            timer.Start()
        safely(work)

    def on_saved_changed(sender, args):
        def work():
            document = Application.ActiveDocument
            current = None
            if document is not None:
                current = document.SavedViewpoints.CurrentSavedViewpoint
            # Navisworks raises this twice for one click: once as the outgoing
            # view stops being current, with CurrentSavedViewpoint null, and
            # once as the incoming one takes over. Taking the null at face
            # value blanks the panel for a frame and, when the user simply
            # navigates off a view, forever. So a null keeps whatever was being
            # tracked and only a real view replaces it.
            if isinstance(current, Api.SavedViewpoint):
                tracked[0] = current
            paint()
        safely(work)

    def subscribe(document):
        previous = subscribed[0]
        if previous is document:
            return
        if previous is not None:
            # One try per event. If the first -= throws on a document that is
            # already going away, the second still has to run, or a handler is
            # left attached to a dead document.
            try:
                previous.SavedViewpoints.CurrentSavedViewpointChanged -= on_saved_changed
            except Exception:
                pass
            try:
                previous.CurrentViewpoint.Changed -= on_camera_moved
            except Exception:
                pass
        subscribed[0] = document
        if document is None:
            return
        document.SavedViewpoints.CurrentSavedViewpointChanged += on_saved_changed
        document.CurrentViewpoint.Changed += on_camera_moved

    def on_document_changed(sender, args):
        def work():
            tracked[0] = None
            subscribe(Application.ActiveDocument)
        safely(work)
        # Seed from whatever view the new document already has current, the
        # same way attach() seeds itself below. Painting here instead would
        # leave the panel blank until the user next touched a viewpoint, even
        # when the document is already sitting on a saved view.
        on_saved_changed(None, None)

    def on_click(sender, args):
        def work():
            saved = tracked[0]
            document = Application.ActiveDocument
            if document is None or not alive(saved):
                return
            document.SavedViewpoints.CurrentSavedViewpoint = saved
        safely(work)

    def detach():
        """Removes everything attach() wired.

        One guard per step, the same way subscribe() unsubscribes: a throw on
        any single unhook must not leave the remaining ones attached.
        """
        def stop_timer():
            timer.Stop()

        def unhook_tick():
            timer.Tick -= on_tick

        def unhook_click():
            name_block.MouseLeftButtonUp -= on_click

        def unhook_document():
            Application.ActiveDocumentChanged -= on_document_changed

        for step in (stop_timer, unhook_tick, unhook_click, unhook_document):
            safely(step)
        subscribe(None)         # already guards each -= separately

    # If any of the wiring throws, everything already hooked has to come back
    # off: the caller gets the exception and never receives a detach, so
    # anything left attached would be orphaned for the session. detach() guards
    # its own body, so it cannot mask the original failure.
    try:
        timer.Tick += on_tick
        name_block.MouseLeftButtonUp += on_click
        Application.ActiveDocumentChanged += on_document_changed
        subscribe(Application.ActiveDocument)
    except Exception:
        detach()
        raise

    # Pick up whatever view is current the moment the shell opens, so it is not
    # blank until the user clicks something.
    on_saved_changed(None, None)

    return {'detach': detach}
