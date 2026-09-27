"""Ask the user to click a point in the 3D view.

The pick runs on the pyNavis pick tool, a Navisworks ToolPlugin that borrows
the document's tool for the length of one click and hands it straight back.
It feels like the native Measure tool on purpose: the same vertex and edge
snapping, the same measure cursors, a marker under the pointer showing what
would be picked, and the view still zooms and pans while you line the shot up.

    from pynavis import pick

    hit = pick.point('Click a point to copy its coordinates')
    if hit is None:
        return                      # Esc, right-click, or another tool chosen
    x, y, z = hit.point             # floats, model units
    hit.normal                      # (x, y, z) or None
    hit.item                        # the ModelItem, or None
    hit.snap                        # 'vertex', 'edge', 'line-vertex',
                                    # 'line-middle', 'arc-center', or None

point() blocks the script until the click, by pumping a nested WPF dispatcher
frame; point_then() returns at once and calls back later. Blocking is the
Python half only, deliberately: the C# core is event-based, so if the nested
message loop ever misbehaves inside a Navisworks release, moving everything to
callbacks is a library change and a Reload, not a rebuild. Both forms are
exercised under IronPython, the default engine. Nothing here is engine
specific, so CPython should behave the same; point_then() is the fallback if a
nested loop ever proves unsafe there.

measure_point() asks the same question through the host's own Measure tool
instead: nothing to deploy and the native snapping exactly, at the price of
knowing only the point (no normal, item or snap kind). measure_points() asks
for a whole point-to-point line the same way and leaves it on screen.

Cancelling is never an error: point() returns None and point_then() calls back
with None. The one thing that does raise is Unavailable, which means the
deployed loader has no pick plugin in it and Navisworks has not been restarted
since pyNavis was updated.
"""

import clr

clr.AddReference('PyNavis.Runtime')
clr.AddReference('WindowsBase')

from System.Windows.Threading import Dispatcher, DispatcherFrame

# PickSession and its enums are pure C#, so they import anywhere. PickService
# is not: its signatures name Navisworks types, and importing it outside a
# session fails on the API assembly, so it is imported where it is used, the
# way pynavis.view defers its own Navisworks imports.
from PyNavis.Runtime.Pick import PickCancelReason, PickSnaps

from pynavis import toast

DEFAULT_PROMPT = 'Click a point in the view'

# Shown under the prompt so the way out is never a guess. The toast dismisses
# itself after a few seconds, which is right: it is an instruction, not a mode
# indicator, and the cursor and the marker say the pick is still running.
CANCEL_HINT = 'Esc or right-click to cancel.'

# Toasted by measure_points once the first point is down.
SECOND_PROMPT = 'Now click the second point'


class Unavailable(RuntimeError):
    """The pick tool is not registered with Navisworks.

    Raised by point() and point_then() when the running loader predates the
    pick plugin. Navisworks only scans for plugins at startup, so the remedy is
    always a restart; the message says so and is safe to toast as-is.
    """


class Hit(object):
    """One picked point. point is always a 3-tuple; the rest may be None."""

    def __init__(self, point, normal, item, snap):
        self.point = point
        self.normal = normal
        self.item = item
        self.snap = snap

    def __repr__(self):
        return 'Hit(point=%r, snap=%r)' % (self.point, self.snap)


def point(prompt=DEFAULT_PROMPT):
    """Blocks until the user clicks a point; returns a Hit, or None on cancel."""
    frame = DispatcherFrame()
    answer = []

    def ended(sender, args):
        answer.append(_hit(sender))
        frame.Continue = False

    session = _begin(prompt, ended)
    try:
        Dispatcher.PushFrame(frame)
    finally:
        # A KeyboardInterrupt or a broken handler must not leave the document
        # stuck on the pick tool with no one waiting for the click.
        if not session.HasEnded:
            session.Cancel(PickCancelReason.Error)
    return answer[0] if answer else None


def point_then(callback, prompt=DEFAULT_PROMPT):
    """Starts a pick and returns at once; calls callback(hit_or_None) later.

    Returns the session, which answers session.HasEnded while the user is still
    lining the click up."""
    def ended(sender, args):
        _LIVE.discard(ended)
        callback(_hit(sender))

    # The handler is the only reference the CLR event holds, and a Python
    # function converted to a delegate is collectable the moment Python stops
    # referencing it, so it is kept alive here for the session's life.
    _LIVE.add(ended)
    try:
        return _begin(prompt, ended)
    except Exception:
        _LIVE.discard(ended)
        raise


def cancel():
    """Ends whatever pick is running; its callback still fires, with None.

    Logged as Superseded, the same reason a second pick gives the first: in
    both cases the script that asked for the point withdrew the question."""
    from PyNavis.Runtime.Pick import PickService
    PickService.CancelCurrent(PickCancelReason.Superseded)


# Live point_then handlers, kept off the garbage collector until they fire.
_LIVE = set()


# ---- the native Measure tool as the picker ---------------------------------------

# How often the measurement is read while waiting for the click. The API has
# no Changed event on CurrentMeasurement, so polling is the only signal; 50ms
# costs four property reads and two key reads a tick, and is fast enough that
# a tap of Esc is still down, or still remembered, when it is read.
MEASURE_POLL_MS = 50

# Win32 virtual keys polled for a cancel. The native measure tool swallows
# both without doing anything (field-checked), so nothing else reports them.
_VK_RBUTTON = 0x02
_VK_ESCAPE = 0x1B


def measure_point(prompt=DEFAULT_PROMPT, notify=None):
    """Blocks until the user clicks a point with the native Measure tool;
    returns a Hit, or None on cancel.

    notify(message, detail) shows the prompt; None means a toast. Pass
    banner.prompt to ask on the bar along the bottom of the window instead,
    and clear it yourself when the pick returns None.

    The document is put into Measure > Point to Point, so the snapping, the
    cursors and the marker are the host's own rather than an imitation of
    them, and nothing depends on the pyNavis pick plugin being loaded. The
    click is read back off Document.CurrentMeasurement, the measurement is
    reset so the next click starts a new one instead of closing this one into
    a dimension, and the tool the user had is restored.

    Only hit.point is known: a measurement carries no normal, item or snap
    kind, so those are None. Esc, a right-click or picking another tool
    cancels."""
    picked = _measure(prompt, measured_click, keep=False, notify=notify)
    return Hit(picked, None, None, None) if picked is not None else None


def measure_points(prompt=DEFAULT_PROMPT, second_prompt=SECOND_PROMPT, notify=None, keep=True):
    """Blocks until the user measures a whole point-to-point line with the
    native Measure tool; returns (first, end) as 3-tuples, or None on cancel.

    The same Measure > Point to Point drive as measure_point, asked twice:
    `prompt` is toasted at the start and `second_prompt` once the first point
    has landed. Any measurement already on screen is dropped first, because a
    caller asking for a line wants a new one, not whatever was left there.

    With keep (the default) the finished measurement is the deliverable: it
    stays on screen with its native readout and the document stays on the
    Measure tool, so the caller can draw next to it and the user can measure
    again. With keep=False the two points are the deliverable: the
    measurement is dropped the moment it is complete and the tool the user
    had comes back, as measure_point does. Esc, a right-click or picking
    another tool cancels; a first point left behind by a cancel is dropped
    so the next click does not close it.

    notify(message, detail) shows each prompt; None means a toast. Pass
    banner.prompt to ask on the bar along the bottom of the window instead;
    the prompt stays there, so replace it with the result or clear it."""
    return _measure(prompt, measured_line, keep=keep, progress=second_prompt, notify=notify)


def _measure(prompt, decide, keep, progress=None, notify=None):
    """Runs the native Measure tool until `decide` says the answer is in.

    decide(before, after) is measured_click or measured_line: a pure reading
    of two measurement snapshots. 'waiting' polls on, 'first' is progress
    (the snapshot moves on and `progress` is shown), 'cancelled' ends with
    None, anything else ends with its payload. With `keep` the measurement
    and the Measure tool are left in place on success; without it they are
    reset and the previous tool restored."""
    from System import TimeSpan
    from System.Windows.Threading import DispatcherTimer

    from pynavis import app, script
    from pynavis._api import Api

    doc = app.get_doc()
    log = script.get_logger()
    if notify is None:
        notify = toast.info
    tool = doc.Tool
    previous = tool.Value
    if previous == Api.Tool.CustomToolPlugin:
        previous = Api.Tool.Select      # the API refuses to restore a plugin tool
    measure = Api.Tool.MeasurePointToPoint

    frame = DispatcherFrame()
    answer = []
    state = {'setting': False}

    def set_tool(value):
        # Our own sets and restores must not read as the user choosing a tool.
        state['setting'] = True
        try:
            tool.Value = value
        finally:
            state['setting'] = False

    def reset(then):
        """Drops the current measurement and leaves the document on `then`.

        The API reads a measurement but cannot clear one. Changing the
        measure mode is what clears it in the host, so the tool is walked
        through another mode on its way to `then`. The reading afterwards is
        logged, because that behaviour is observed rather than documented."""
        set_tool(Api.Tool.MeasureAngle)
        set_tool(then)
        left = _measurement(doc)
        if left != (None, None):
            log.warning('pick: measurement survived the reset: %s' % (left,))

    def tool_changed(sender, args):
        if state['setting'] or tool.Value == measure:
            return
        state['left'] = True
        frame.Continue = False

    def tick(sender, args):
        if _pressed(_VK_ESCAPE) or _pressed(_VK_RBUTTON):
            frame.Continue = False
            return
        now = _measurement(doc)
        kind, picked = decide(state['before'], now)
        if kind == 'waiting':
            return
        if kind == 'first':
            state['before'] = now
            if progress:
                notify(str(progress), CANCEL_HINT)
            return
        if kind != 'cancelled':
            answer.append(picked)
        frame.Continue = False

    timer = DispatcherTimer()
    timer.Interval = TimeSpan.FromMilliseconds(MEASURE_POLL_MS)
    timer.Tick += tick
    tool.Changed += tool_changed
    try:
        # A half-finished measurement would take the click as its END point
        # and draw a dimension. A finished one is left alone by a single-point
        # pick until there is a point to replace it with, so a cancelled pick
        # costs the user nothing; a line pick drops it, since a new line is
        # what was asked for.
        first, end = _measurement(doc)
        if first is not None and (end is None or keep):
            reset(measure)
        elif previous != measure:
            set_tool(measure)
        state['before'] = _measurement(doc)
        # Both keys remember a press since they were last read; forget any
        # from before the pick so an old Esc does not cancel this one.
        _pressed(_VK_ESCAPE)
        _pressed(_VK_RBUTTON)
        notify(DEFAULT_PROMPT if prompt is None else str(prompt), CANCEL_HINT)
        timer.Start()
        Dispatcher.PushFrame(frame)
    finally:
        timer.Stop()
        timer.Tick -= tick
        tool.Changed -= tool_changed
        # The user who picked another tool already has the one they want.
        if not state.get('left'):
            first, end = _measurement(doc)
            if answer and not keep:
                reset(previous)
            elif not answer and first is not None and end is None:
                reset(previous)     # a lone first point would close on the next click
            elif not answer and previous != measure:
                set_tool(previous)
    return answer[0] if answer else None


def _pressed(vk):
    """True when the key or mouse button is down, or went down since the last
    call. GetAsyncKeyState's low bit is what catches a tap between two polls.
    Any failure reads as not pressed: cancelling by another tool still works."""
    try:
        import ctypes
        return (ctypes.windll.user32.GetAsyncKeyState(vk) & 0x8001) != 0
    except Exception:
        return False


def measured_click(before, after):
    """What one poll of the measurement says happened since the pick began.

    before and after are (first, end) pairs, each a 3-tuple or None. Returns
    ('waiting', None), ('cancelled', None) or ('point', xyz). Pure, so it is
    testable without Navisworks.

    A click lands in one of two places: on a half-finished measurement it
    becomes the end point, otherwise it starts a new measurement and becomes
    the first point. A measurement that was there and is now gone is the
    user's right-click clearing it, which is their way of saying never mind."""
    if after == before:
        return ('waiting', None)
    first, end = after
    if first is None:
        return ('cancelled', None)
    if end is not None and end != before[1] and first == before[0]:
        return ('point', end)
    return ('point', first)


def measured_line(before, after):
    """What one poll says about a whole measurement since the last one.

    before and after are (first, end) pairs, each a 3-tuple or None. Returns
    ('waiting', None), ('first', xyz) when a first point has landed and the
    end is still to come, ('line', (first, end)) once both are there, or
    ('cancelled', None) when a measurement that was there is gone. Pure, so
    it is testable without Navisworks.

    A pair that was already complete when the pick began reads as waiting:
    it is the stale line the pick was started over, not an answer."""
    if after == before:
        return ('waiting', None)
    first, end = after
    if first is None:
        return ('cancelled', None)
    if end is None:
        return ('first', first)
    return ('line', (first, end))


def _measurement(doc):
    m = doc.CurrentMeasurement
    return (_xyz(m.FirstPoint) if m.HasFirstPoint else None,
            _xyz(m.EndPoint) if m.HasEndPoint else None)


def _xyz(p):
    return (float(p.X), float(p.Y), float(p.Z))


def _begin(prompt, ended):
    from PyNavis.Runtime.Pick import PickService
    text = DEFAULT_PROMPT if prompt is None else str(prompt)
    session = PickService.Begin(text)
    if session is None:
        raise Unavailable(
            'The pyNavis pick tool is not loaded. Restart Navisworks to finish '
            'updating pyNavis.')
    session.Ended += ended
    toast.info(text, CANCEL_HINT)
    return session


def _hit(session):
    """The Hit for a finished session, or None when it was cancelled."""
    result = session.Result
    if result is None:
        return None
    return Hit(_tuple(result.Point), _tuple(result.Normal),
               result.Item, PickSnaps.Name(result.Snap))


def _tuple(values):
    if values is None:
        return None
    return (float(values[0]), float(values[1]), float(values[2]))
