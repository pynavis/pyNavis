"""Interactive python console running inside Navisworks with the pyNavis environment.

Built on the DesignSystem v2 toolkit: a solid surface, the real Windows titlebar,
bordered monospace panes and an accent Run button - the same language as every
other pyNavis form.
"""

__title__ = 'Console'

import sys

import clr

from pynavis import _repl, script
from pynavis._history import CommandHistory

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')
clr.AddReference('WindowsFormsIntegration')
clr.AddReference('PyNavis.Runtime')

from System.Windows import (
    CornerRadius, GridLength, GridUnitType, HorizontalAlignment, Thickness,
    TextWrapping, VerticalAlignment, Window, WindowStartupLocation,
)
from System.Windows.Controls import (
    Border, ColumnDefinition, Grid, RowDefinition, ScrollBarVisibility, TextBox,
)
from System.Windows.Controls.Primitives import ScrollBar
from System.Windows.Input import Cursors, Key, Keyboard, ModifierKeys
from System.Windows.Forms.Integration import ElementHost
from System.Windows.Media import Brushes, FontFamily
from System.Windows.Threading import DispatcherFrame, DispatcherPriority
from System import Action
from System.Windows.Threading import Dispatcher

from PyNavis.Runtime.Forms import DesignSystem, FluentChrome
from PyNavis.Runtime.Output import OutputWindowWriter, PyNavisTheme

# Rich output (pynavis.output) goes to whichever writer the host holds, and that
# is set per RIBBON command - so console output landed in the last button's
# window, under its title. The console owns one writer for its whole life and
# re-asserts it before each run, so output.print_* always lands in a window
# titled for the console.
_output = OutputWindowWriter('pyNavis - Console')


def _pump():
    """Lets WPF finish painting before we block the UI thread on a script."""
    frame = DispatcherFrame()

    def done():
        frame.Continue = False

    Dispatcher.CurrentDispatcher.BeginInvoke(
        DispatcherPriority.Background, Action(done))
    Dispatcher.PushFrame(frame)

_BANNER = (
    'pyNavis console - IronPython %s.%s.%s\n'
    'Run: Ctrl+Enter . History: Up/Down at the edges. Try: from pynavis import doc, selection\n'
    % sys.version_info[:3])

# One namespace for the window's lifetime, so assignments survive between runs.
_namespace = {'__pynavis__': __pynavis__}
_history = CommandHistory()

_T = DesignSystem.Tokens.Current
_MONO = FontFamily('Consolas')


def _console_pane(read_only):
    """A bordered v2 frame wrapping a borderless monospace TextBox. Returns (frame, box)."""
    box = TextBox()
    box.FontFamily = _MONO
    box.FontSize = 13
    box.AcceptsReturn = True
    box.AcceptsTab = True
    box.TextWrapping = TextWrapping.NoWrap
    box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    box.IsReadOnly = read_only
    box.Background = Brushes.Transparent
    box.Foreground = DesignSystem.Brush(_T.Ink)
    box.BorderThickness = Thickness(0)
    box.Padding = Thickness(8)
    box.FocusVisualStyle = DesignSystem.FocusRing(DesignSystem.Accent)
    box.Resources.Add(clr.GetClrType(ScrollBar), DesignSystem.SlimScrollStyle())

    frame = Border()
    frame.CornerRadius = CornerRadius(4)
    frame.BorderBrush = DesignSystem.Brush(_T.LineStrong)
    frame.BorderThickness = Thickness(1)
    frame.Background = DesignSystem.Brush(_T.Paper)
    frame.Child = box
    return frame, box


def _build_window():
    window = Window()
    window.Title = 'pyNavis Console'
    window.Width = 720
    window.Height = 540
    window.MinWidth = 460
    window.MinHeight = 320
    window.WindowStartupLocation = WindowStartupLocation.CenterScreen
    window.Background = DesignSystem.Brush(_T.Surface)
    window.FontFamily = FontFamily('Segoe UI Variable Text, Segoe UI')
    FluentChrome.Apply(window)  # real Windows titlebar (dark attr in dark theme)

    grid = Grid()
    grid.Margin = Thickness(16)
    for height in (GridLength(1, GridUnitType.Star), GridLength.Auto):
        row = RowDefinition()
        row.Height = height
        grid.RowDefinitions.Add(row)

    output_frame, output_box = _console_pane(True)
    output_box.Text = _BANNER
    Grid.SetRow(output_frame, 0)
    grid.Children.Add(output_frame)

    # Two columns: the input pane fills the row, Run sits in its own auto column
    # so the two never overlap and Run's bottom aligns with the pane's bottom.
    input_grid = Grid()
    input_grid.Margin = Thickness(0, 12, 0, 0)
    Grid.SetRow(input_grid, 1)
    fill_col = ColumnDefinition()
    fill_col.Width = GridLength(1, GridUnitType.Star)
    run_col = ColumnDefinition()
    run_col.Width = GridLength.Auto
    input_grid.ColumnDefinitions.Add(fill_col)
    input_grid.ColumnDefinitions.Add(run_col)

    input_frame, input_box = _console_pane(False)
    input_frame.MinHeight = 84
    input_frame.MaxHeight = 160
    Grid.SetColumn(input_frame, 0)
    input_grid.Children.Add(input_frame)

    run = DesignSystem.Primary(_T, 'Run', None)
    run.MinWidth = 0
    run.Width = 92
    run.Margin = Thickness(12, 0, 0, 0)
    run.VerticalAlignment = VerticalAlignment.Bottom
    Grid.SetColumn(run, 1)
    input_grid.Children.Add(run)
    grid.Children.Add(input_grid)

    window.Content = grid

    def append(text):
        output_box.AppendText(text)
        output_box.ScrollToEnd()

    def run_code(sender, args):
        code = input_box.Text.replace('\r\n', '\n').strip()
        if not code:
            return
        _history.add(code)
        for line in code.split('\n'):
            append('>>> %s\n' % line)

        # A script runs on the UI thread (the Navisworks API demands it), so a
        # slow one freezes this window. Say so instead of looking hung: disable
        # Run, show the wait cursor, and let WPF paint before we block.
        run.IsEnabled = False
        run.Content = 'Running'
        window.Cursor = Cursors.Wait
        try:
            _pump()
            # Console output belongs in the console's own window, not in whatever
            # ribbon button ran last. Guarded: a failure here must never be the
            # reason a script does not run.
            try:
                __pynavis__.SetCurrentOutput(_output)
            except Exception:
                pass
            append(_repl.execute(code, _namespace))
        finally:
            window.Cursor = None
            run.Content = 'Run'
            run.IsEnabled = True
        input_box.Clear()
        input_box.Focus()

    def recall(entry):
        input_box.Text = entry
        input_box.CaretIndex = len(input_box.Text)

    def key_down(sender, args):
        # PREVIEW, not KeyDown: a TextBox with AcceptsReturn owns Enter (it
        # inserts a line break) and the arrow keys (caret movement), and marks
        # them handled in class handling, which runs BEFORE instance handlers.
        # On the bubbling event this whole function never sees them, so both
        # Ctrl+Enter and the history keys are dead. Tunnelling gets there first,
        # and Handled=True here also stops the stray newline.
        if args.Key == Key.Enter and Keyboard.Modifiers == ModifierKeys.Control:
            args.Handled = True
            run_code(sender, args)
            return
        # History: Up on the first line / Down on the last (REPL feel; middle
        # lines keep normal caret movement for multiline editing).
        if args.Key == Key.Up:
            if input_box.GetLineIndexFromCharacterIndex(input_box.CaretIndex) == 0:
                args.Handled = True
                recall(_history.previous())
        elif args.Key == Key.Down:
            at_last = (input_box.GetLineIndexFromCharacterIndex(input_box.CaretIndex)
                       == input_box.LineCount - 1)
            if at_last:
                args.Handled = True
                recall(_history.next())

    run.Click += run_code
    input_box.PreviewKeyDown += key_down
    window.ContentRendered += lambda s, e: input_box.Focus()
    return window


# Modeless: Navisworks stays usable while the console floats. A modeless WPF
# window rides the HOST's message pump, which preprocesses keyboard messages
# itself - most keydowns (digits, Enter) and every WM_CHAR die before the
# window's WndProc, so typing shows nothing and Ctrl+Enter never fires. The
# interop call registers a filter that hands keyboard messages to this window
# first; it must run before Show(). Modal ShowDialog windows pump for
# themselves and do not need it.
_window = _build_window()
ElementHost.EnableModelessKeyboardInterop(_window)
_window.Show()
