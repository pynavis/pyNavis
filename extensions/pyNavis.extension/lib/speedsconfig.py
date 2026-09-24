# -*- coding: utf-8 -*-
"""The settings window behind Shift+Click on either speed button.

Both bundles keep their config.py down to a call into edit(), because the two
share one settings store (speeds.SETTINGS_KEY): how fast you want to walk is a
fact about you, not about which button you pressed. The XAML sits beside this
file rather than in either bundle for the same reason, so the two can never
drift apart.
"""

import os

import speeds

from pynavis import forms, settings, toast

_XAML = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'reset_speeds_settings.xaml')


def edit():
    """Shows the settings window and saves on OK. Silent on cancel."""
    values = speeds.normalise(settings.load(speeds.SETTINGS_KEY, speeds.DEFAULTS))

    win = forms.WPFWindow(_XAML)
    for unit in speeds.UNITS:
        win['UnitBox'].Items.Add(unit)

    # The unit dropdown CONVERTS the number in the box rather than relabelling
    # it, so switching m/s to ft/s turns 30 into 98.4252 instead of silently
    # meaning something three times faster. The handler only learns the new
    # unit, so the old one has to be remembered, and a one-item list is what
    # holds it: _fill needs to reset it too, so it has to be something both
    # functions can be handed rather than a variable closed over here.
    previous = [values['linear_unit']]
    _fill(win, values, previous)

    def _unit_changed(sender, args):
        chosen = str(win['UnitBox'].SelectedItem or previous[0])
        if chosen == previous[0]:
            return
        current = speeds.parse_number(win['LinearBox'].Text, speeds.DEFAULTS['linear'])
        win['LinearBox'].Text = speeds.trim(
            speeds.convert_linear(current, previous[0], chosen))
        previous[0] = chosen

    def _ok(sender, args):
        win.close(True)

    def _reset(sender, args):
        _fill(win, speeds.DEFAULTS, previous)

    win['UnitBox'].SelectionChanged += _unit_changed
    win['OkButton'].Click += _ok
    win['ResetButton'].Click += _reset

    if not win.show_dialog():
        return                                  # cancelled: say nothing

    saved = speeds.normalise({
        'linear': win['LinearBox'].Text,
        'linear_unit': str(win['UnitBox'].SelectedItem or speeds.DEFAULTS['linear_unit']),
        'change_linear': bool(win['LinearCheck'].IsChecked),
        'angular': win['AngularBox'].Text,
        'change_angular': bool(win['AngularCheck'].IsChecked),
        'fov': win['FovBox'].Text,
        'change_fov': bool(win['FovCheck'].IsChecked),
    })
    settings.save(speeds.SETTINGS_KEY, saved)
    toast.success('Reset speed settings saved',
                  speeds.summary(saved) or 'Nothing will be reset')


def _fill(win, values, previous):
    """Writes a settings dict into the window.

    previous is updated BEFORE the dropdown, because assigning SelectedItem
    raises SelectionChanged synchronously and the handler would otherwise
    convert the number that was just written.
    """
    win['LinearBox'].Text = speeds.trim(values['linear'])
    previous[0] = values['linear_unit']
    win['UnitBox'].SelectedItem = values['linear_unit']
    win['LinearCheck'].IsChecked = bool(values['change_linear'])
    win['AngularBox'].Text = speeds.trim(values['angular'])
    win['AngularCheck'].IsChecked = bool(values['change_angular'])
    win['FovBox'].Text = speeds.trim(values['fov'])
    win['FovCheck'].IsChecked = bool(values['change_fov'])
