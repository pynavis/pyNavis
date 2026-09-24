"""Small status toasts in the corner of the Navisworks window.

Success is green, errors red, information blue, warnings amber. A toast never
takes focus and dismisses itself, so tools that run constantly can report what
they did without opening the output window.
"""

import clr

clr.AddReference('PyNavis.Runtime')

from PyNavis.Runtime.Forms import Toast as _Toast


def show(level, message, detail=None):
    """Shows a toast; level is 'success', 'error', 'info' or 'warning'."""
    _Toast.Show(str(level), str(message), None if detail is None else str(detail))


def success(message, detail=None):
    show('success', message, detail)


def error(message, detail=None):
    show('error', message, detail)


def info(message, detail=None):
    show('info', message, detail)


def warning(message, detail=None):
    show('warning', message, detail)
