"""The settings window behind Shift+Click on either element id button.

Both bundles keep their config.py down to a call into edit(), because the two
buttons share one settings store (elementids.SETTINGS_KEY): which property
holds the id is a fact about the model, not about the button that reads it.
The XAML sits beside this file rather than in either bundle for the same
reason, so the two can never drift apart.
"""

import os

import elementids

from pynavis import forms, settings, toast

_XAML = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'element_id_settings.xaml')


def edit():
    """Shows the settings window and saves on OK. Silent on cancel."""
    values = settings.load(elementids.SETTINGS_KEY, elementids.DEFAULTS)

    win = forms.WPFWindow(_XAML)
    _fill(win, values)

    def _ok(sender, args):
        win.close(True)

    def _reset(sender, args):
        _fill(win, elementids.DEFAULTS)

    win['OkButton'].Click += _ok
    win['ResetButton'].Click += _reset

    if not win.show_dialog():
        return                                  # cancelled: say nothing

    settings.save(elementids.SETTINGS_KEY, {
        'id_category': _text(win, 'CategoryBox', 'id_category'),
        'id_property': _text(win, 'PropertyBox', 'id_property'),
        'zoom': bool(win['ZoomBox'].IsChecked),
        'isolate': bool(win['IsolateBox'].IsChecked),
        'use_clipboard': bool(win['ClipboardBox'].IsChecked),
        'every_match': bool(win['EveryMatchBox'].IsChecked),
    })
    toast.success('Element ID settings saved')


def _fill(win, values):
    win['CategoryBox'].Text = str(values['id_category'])
    win['PropertyBox'].Text = str(values['id_property'])
    win['ZoomBox'].IsChecked = bool(values['zoom'])
    win['IsolateBox'].IsChecked = bool(values['isolate'])
    win['ClipboardBox'].IsChecked = bool(values['use_clipboard'])
    win['EveryMatchBox'].IsChecked = bool(values['every_match'])


def _text(win, element, default_key):
    """A trimmed text box, falling back to the default rather than saving a
    blank that would make every search fail with no visible cause."""
    value = str(win[element].Text).strip()
    return value or elementids.DEFAULTS[default_key]
