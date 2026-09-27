"""A result bar across the bottom of the Navisworks window.

Where a toast is a note in the corner, a banner is a readout: the full width
of the window, filled solid in the level colour, with the message in large
type on it. Use it for the one value a tool exists to produce, such as a
measurement, and for the instruction a tool is waiting on. Success is green,
errors red, information blue, warnings amber. There is only ever one banner;
a new one replaces the last. It never takes focus, dismisses itself after a
while unless told to stay, and a click dismisses it at once.
"""

import clr

clr.AddReference('PyNavis.Runtime')

from PyNavis.Runtime.Forms import Banner as _Banner


def show(level, message, detail=None, seconds=None):
    """Shows the banner; level is 'success', 'error', 'info' or 'warning'.

    seconds is how long it stays: None for the level's own duration, 0 to
    stay until clear(), another banner, a click or Reload."""
    text = str(message)
    extra = None if detail is None else str(detail)
    if seconds is None:
        _Banner.Show(str(level), text, extra)
    else:
        _Banner.Show(str(level), text, extra, float(seconds))


def success(message, detail=None):
    show('success', message, detail)


def error(message, detail=None):
    show('error', message, detail)


def info(message, detail=None):
    show('info', message, detail)


def warning(message, detail=None):
    show('warning', message, detail)


def prompt(message, detail=None):
    """An instruction the tool is waiting on: stays until replaced or
    cleared. Shaped to be handed to pick.measure_points(notify=...)."""
    show('info', message, detail, seconds=0)


def clear():
    """Removes the banner, if one is showing."""
    _Banner.CloseAll()
