"""Defaults for Section Fit (the Shift+Click settings).

Padding is how far each of the six planes stands off the objects, so faces do
not graze the geometry. Snap angle is how close to square counts as square:
below it the box stops turning, because a two-degree tilt costs you every
plane's alignment and buys nothing.
"""
import clr

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')
clr.AddReference('PyNavis.Runtime')

from System.Windows import (FontWeights, HorizontalAlignment, ResizeMode,
                            SizeToContent, TextWrapping, Thickness, Visibility,
                            Window, WindowStartupLocation)
from System.Windows.Controls import Orientation, StackPanel, TextBox
from System.Windows.Media import FontFamily
from PyNavis.Runtime.Forms import DesignSystem, FluentChrome

from pynavis import section, settings, toast

T = DesignSystem.Tokens.Current
values = settings.load(section.TOOL, section.DEFAULTS)

window = Window()
window.Title = 'Section Defaults'
window.Width = 380
window.SizeToContent = SizeToContent.Height
window.ResizeMode = ResizeMode.NoResize
window.WindowStartupLocation = WindowStartupLocation.CenterScreen
window.FontFamily = FontFamily('Segoe UI Variable Text, Segoe UI')
window.Background = DesignSystem.Brush(T.Paper)
window.ShowInTaskbar = False
FluentChrome.Apply(window)

body = StackPanel()
body.Margin = Thickness(20, 16, 20, 16)

title = DesignSystem.Text('Section fit defaults', 16, T.Ink, FontWeights.SemiBold)
body.Children.Add(title)
caption = DesignSystem.Text('Used every time Fit or Plan runs.', 12, T.Muted)
caption.Margin = Thickness(0, 4, 0, 14)
caption.TextWrapping = TextWrapping.Wrap
body.Children.Add(caption)


def section_label(label):
    text = DesignSystem.Text(label, 12, T.Muted, FontWeights.Medium)
    text.Margin = Thickness(0, 10, 0, 6)
    body.Children.Add(text)


section_label('Padding')
padding_box = TextBox()
padding_box.Text = '%g' % float(values['padding_mm'])
padding_box.Width = 80
padding_field = DesignSystem.InputField(T, padding_box, 'mm')
padding_field.HorizontalAlignment = HorizontalAlignment.Left
body.Children.Add(padding_field)
padding_caption = DesignSystem.Text(
    'How far each plane stands off the objects.', 12, T.Muted)
padding_caption.Margin = Thickness(0, 4, 0, 0)
body.Children.Add(padding_caption)
padding_error = DesignSystem.Text('Enter 0 or more.', 12, T.Error)
padding_error.Margin = Thickness(0, 4, 0, 0)
padding_error.Visibility = Visibility.Collapsed
body.Children.Add(padding_error)

section_label('Snap angle')
snap_box = TextBox()
snap_box.Text = '%g' % float(values['snap_degrees'])
snap_box.Width = 80
snap_field = DesignSystem.InputField(T, snap_box, 'deg')
snap_field.HorizontalAlignment = HorizontalAlignment.Left
body.Children.Add(snap_field)
snap_caption = DesignSystem.Text(
    'Within this of square, the box stays square to the world.', 12, T.Muted)
snap_caption.Margin = Thickness(0, 4, 0, 0)
snap_caption.TextWrapping = TextWrapping.Wrap
body.Children.Add(snap_caption)
snap_error = DesignSystem.Text('Enter between 0 and 45.', 12, T.Error)
snap_error.Margin = Thickness(0, 4, 0, 0)
snap_error.Visibility = Visibility.Collapsed
body.Children.Add(snap_error)


def number(box):
    try:
        return float(box.Text)
    except ValueError:
        return None


def validate(*args):
    """Live, so the error appears as you type and clears the same way."""
    padding = number(padding_box)
    snap = number(snap_box)
    padding_bad = padding is None or padding < 0
    snap_bad = snap is None or snap < 0 or snap > 45
    padding_error.Visibility = (Visibility.Visible if padding_bad
                                else Visibility.Collapsed)
    snap_error.Visibility = Visibility.Visible if snap_bad else Visibility.Collapsed
    padding_field.BorderBrush = DesignSystem.Brush(
        T.Error if padding_bad else T.LineStrong)
    snap_field.BorderBrush = DesignSystem.Brush(
        T.Error if snap_bad else T.LineStrong)
    save_btn.IsEnabled = not (padding_bad or snap_bad)
    return not (padding_bad or snap_bad)


def save():
    if not validate():
        return
    settings.save(section.TOOL, {'padding_mm': number(padding_box),
                                 'snap_degrees': number(snap_box)})
    toast.success('Section defaults saved.')
    window.Close()


footer = StackPanel()
footer.Orientation = Orientation.Horizontal
footer.HorizontalAlignment = HorizontalAlignment.Right
footer.Margin = Thickness(0, 18, 0, 0)
cancel_btn = DesignSystem.Secondary(T, 'Cancel', lambda: window.Close())
cancel_btn.Margin = Thickness(0, 0, 8, 0)
footer.Children.Add(cancel_btn)
save_btn = DesignSystem.Primary(T, 'Save', save)
footer.Children.Add(save_btn)
body.Children.Add(footer)

padding_box.TextChanged += validate
snap_box.TextChanged += validate
validate()

window.Content = body
window.ShowDialog()
