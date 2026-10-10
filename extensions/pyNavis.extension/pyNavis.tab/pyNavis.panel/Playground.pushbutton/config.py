# -*- coding: utf-8 -*-
"""Shift+Click on Playground: where the catalog comes from, and the install folder.

Writes config.json playground.catalog, or removes it to go back to the catalog
on GitHub. A local path is accepted for working offline or on a catalog that is
not published yet; its zip may sit beside it.
"""

import os

import clr

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')
clr.AddReference('PyNavis.Runtime')

from System.Windows import (
    GridLength, GridUnitType, HorizontalAlignment, ResizeMode, SizeToContent,
    Thickness, Window, WindowStartupLocation,
)
from System.Windows.Controls import ColumnDefinition, Grid, Orientation, StackPanel, TextBox
from System.Windows.Media import FontFamily

from PyNavis.Runtime import RuntimeHost
from PyNavis.Runtime.Forms import DesignSystem, FluentChrome

from pynavis import forms, script, toast

try:
    import playground as pg       # extension lib/, which pynavis run does not put on the path
except ImportError:
    pg = None

_T = DesignSystem.Tokens.Current


def _current(path):
    try:
        section = pg.read_config(path).get('playground')
    except Exception:
        return ''
    if isinstance(section, dict) and isinstance(section.get('catalog'), str):
        return section['catalog']
    return ''


def _ask(current):
    """The catalog the user wants ('' for GitHub), or None when they cancel."""
    window = Window()
    window.Title = 'Playground settings'
    window.Width = 520
    window.SizeToContent = SizeToContent.Height
    window.ResizeMode = ResizeMode.NoResize
    window.WindowStartupLocation = WindowStartupLocation.CenterScreen
    window.Background = DesignSystem.Brush(_T.Paper)
    window.FontFamily = FontFamily('Segoe UI Variable Text, Segoe UI')
    window.FontSize = 13

    body = StackPanel()
    body.Margin = Thickness(24)
    body.Children.Add(DesignSystem.Text('Catalog', 13, _T.Ink))

    box = TextBox()
    box.Text = current
    field = DesignSystem.InputField(_T, box)
    field.Height = 32
    field.Margin = Thickness(0, 8, 0, 0)
    body.Children.Add(field)

    hint = DesignSystem.Text(
        'Leave it empty for the catalog on GitHub. A web address or the path of a '
        'catalog.json on this PC or a share also works; a local catalog may keep its '
        'zip beside it.', 12, _T.Muted)
    hint.Margin = Thickness(0, 6, 0, 0)
    body.Children.Add(hint)

    tools = StackPanel()
    tools.Orientation = Orientation.Horizontal
    tools.Margin = Thickness(-12, 12, 0, 0)

    def browse():
        path = forms.open_file('Catalog (*.json)|*.json|All files (*.*)|*.*', 'Choose a catalog')
        if path is not None:
            box.Text = path

    def open_folder():
        target = pg.extension_dir()
        if os.path.isdir(target):
            script.show_in_explorer(target)
        else:
            toast.info('Nothing is installed yet', 'Install a tool from Playground first')

    tools.Children.Add(DesignSystem.Quiet(_T, 'Browse...', browse))
    tools.Children.Add(DesignSystem.Quiet(_T, 'Use GitHub', lambda: setattr(box, 'Text', '')))
    tools.Children.Add(DesignSystem.Quiet(_T, 'Open install folder', open_folder))
    body.Children.Add(tools)

    answer = {'value': None}

    def save():
        answer['value'] = box.Text.strip()
        window.DialogResult = True

    footer = Grid()
    footer.Margin = Thickness(0, 24, 0, 0)
    fill = ColumnDefinition()
    fill.Width = GridLength(1, GridUnitType.Star)
    footer.ColumnDefinitions.Add(fill)
    buttons = StackPanel()
    buttons.Orientation = Orientation.Horizontal
    buttons.HorizontalAlignment = HorizontalAlignment.Right
    cancel = DesignSystem.Secondary(_T, 'Cancel', lambda: window.Close())
    cancel.IsCancel = True
    ok = DesignSystem.Primary(_T, 'Save', save)
    ok.IsDefault = True
    ok.Margin = Thickness(8, 0, 0, 0)
    buttons.Children.Add(cancel)
    buttons.Children.Add(ok)
    footer.Children.Add(buttons)
    body.Children.Add(footer)

    window.Content = body
    window.ContentRendered += lambda s, e: box.Focus()
    FluentChrome.Apply(window)
    if not window.ShowDialog():
        return None
    return answer['value']


def run():
    if globals().get('__runner__') or script.is_runner():
        toast.info('Playground settings open a window', 'Run them from the ribbon, not from pynavis run')
        return
    path = RuntimeHost.UserConfigPath
    current = _current(path)
    value = _ask(current)
    if value is None:
        return                                      # cancelled: say nothing
    if value == current.strip():
        toast.info('The catalog setting is unchanged')
        return
    try:
        pg.save_catalog_override(path, value)
    except Exception as error:
        script.get_logger().error('playground catalog not saved: %s' % error)
        toast.error('The catalog setting could not be saved', str(error))
        return
    if value:
        toast.success('Playground catalog set', value)
    else:
        toast.success('Playground uses the catalog on GitHub')


if '__commandpath__' in globals():
    run()
