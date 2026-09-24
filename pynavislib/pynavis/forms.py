"""Modern dialogs for scripts: alerts, confirmations, text and file prompts.

Backed by the runtime's Fluent WPF dialogs (rounded corners, dark-mode titlebar,
Windows accent color) - see PyNavis.Runtime.Forms.Dialogs. No Navisworks API is
touched, so these also work while a model is still loading.
"""

import clr

clr.AddReference('PyNavis.Runtime')

from PyNavis.Runtime.Forms import Dialogs, Pickers


def alert(message, title='pyNavis'):
    Dialogs.Alert(str(message), title)


def confirm(message, title='pyNavis'):
    """Yes/No question; returns True on Yes."""
    return Dialogs.Confirm(str(message), title)


def ask_string(prompt, default='', title='pyNavis'):
    """Single-line text prompt; returns the entered string, or None on cancel."""
    return Dialogs.AskString(str(prompt), default, title)


def save_file(filter='CSV files (*.csv)|*.csv|All files (*.*)|*.*',
              default_name='', title='Save file'):
    """Save dialog; returns the chosen path, or None on cancel."""
    return Dialogs.SaveFile(filter, default_name, title)


def open_file(filter='All files (*.*)|*.*', title='Open file'):
    """Open dialog; returns the chosen path, or None on cancel."""
    return Dialogs.OpenFile(filter, title)


def select_from_list(items, title='Select', multiselect=False, prompt=None):
    """List picker with search. Items are strings or (label, value) pairs.
    Returns the picked value (or list of values when multiselect), None on cancel."""
    from System.Collections.Generic import List
    from System import String
    labels, values = [], []
    for item in items:
        if isinstance(item, tuple):
            labels.append(str(item[0])); values.append(item[1])
        else:
            labels.append(str(item)); values.append(item)
    boxed = List[String]()
    for label in labels:
        boxed.Add(label)
    picked = Pickers.SelectFromList(title, boxed, bool(multiselect), prompt)
    if picked is None:
        return None
    result = [values[i] for i in picked]
    return result if multiselect else (result[0] if result else None)


def ask_options(prompt, options, title='pyNavis'):
    """One-click choice between a few options; returns the option or None."""
    from PyNavis.Runtime.Forms import Pickers
    from System.Collections.Generic import List
    from System import String
    boxed = List[String]()
    for option in options:
        boxed.Add(str(option))
    index = Pickers.CommandSwitch(str(title), '' if prompt is None else str(prompt), boxed)
    return None if index < 0 else options[index]


def ask_number(prompt, default=None, min_value=None, max_value=None, title='pyNavis'):
    """Numeric prompt with live validation; returns float or None on cancel."""
    from PyNavis.Runtime.Forms import Pickers
    from System import Nullable, Double
    result = Pickers.AskNumber(str(prompt),
                               Nullable[Double](min_value) if min_value is not None else None,
                               Nullable[Double](max_value) if max_value is not None else None,
                               Nullable[Double](float(default)) if default is not None else None,
                               title)
    return None if result is None else float(result)


def pick_folder(title='Select folder', initial=None):
    """Folder picker; returns the path or None on cancel."""
    from PyNavis.Runtime.Forms import FolderPicker
    return FolderPicker.Pick(title, initial)


class Cancelled(Exception):
    """Raised by progress().check() after the user clicks Cancel."""


class _Progress(object):
    def __init__(self, scope):
        self._scope = scope

    def update(self, fraction, label=None):
        """Reports progress; returns False once the user cancelled.

        label defaults to None, which leaves the window's current label
        alone. That matters after a Cancel click: the window relabels
        itself to 'Cancelling...', and a plain update() must not wipe
        that back out."""
        self._scope.Report(float(fraction), None if label is None else str(label))
        return not self._scope.IsCancelled

    def check(self):
        """Raises Cancelled after the user clicks Cancel.

        Pumps the message loop first (throttled exactly like update()
        does), because a loop that only ever calls check() would otherwise
        never let the Cancel button be clicked at all: scripts hold the UI
        thread for their whole run."""
        self._scope.Pump()
        if self._scope.IsCancelled:
            raise Cancelled()


def progress(title, label=''):
    """Context manager showing a cancellable progress window.

    with forms.progress('Exporting') as p:
        for i, item in enumerate(items):
            p.check()                       # raises Cancelled on cancel
            p.update(float(i) / len(items))
    """
    from PyNavis.Runtime.Forms import ProgressScope

    class _Ctx(object):
        def __enter__(self):
            self._scope = ProgressScope.Begin(str(title), str(label), True)
            return _Progress(self._scope)

        def __exit__(self, exc_type, exc_value, traceback):
            self._scope.Dispose()
            return False
    return _Ctx()


class WPFWindow(object):
    """A WPF window loaded from a .xaml file shipped in the bundle.

    win = forms.WPFWindow('layout.xaml')
    win['Ok'].Click += lambda s, e: win.close(True)
    if win.show_dialog():
        name = win['NameBox'].Text
    """

    def __init__(self, xaml_file):
        import os
        from pynavis import script
        from PyNavis.Runtime.Forms import XamlHost
        path = script.get_bundle_file(xaml_file) if not os.path.isabs(str(xaml_file)) else str(xaml_file)
        if path is None or not os.path.exists(path):
            raise IOError('XAML file not found: %s' % xaml_file)
        self.window = XamlHost.Load(path)

    def find(self, name):
        """The named element from the XAML, or None."""
        return self.window.FindName(name)

    def __getitem__(self, name):
        element = self.find(name)
        if element is None:
            raise KeyError('No element named %r in the XAML' % name)
        return element

    def show_dialog(self):
        """Shows modal (owned by Navisworks, themed chrome); returns DialogResult."""
        from PyNavis.Runtime.Forms import FluentChrome
        FluentChrome.Apply(self.window)
        return self.window.ShowDialog()

    def close(self, result=None):
        if result is not None:
            self.window.DialogResult = bool(result)
        self.window.Close()
