"""Defaults for Clash Grouper (the Shift+Click settings).

Presets what the grouper dialog opens with: mode, custom rule chain, cluster
distance, and whether existing groups are kept. Every run can still change
them in the dialog itself. The distance is shown and typed in the document's
units, the way any pyNavis length is (pynavis.lengths: 6' 6", 2m), and kept
in metres.
"""
import clr

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')
clr.AddReference('PyNavis.Runtime')

from System.Windows import (CornerRadius, FontWeights, HorizontalAlignment,
                            ResizeMode, SizeToContent, TextWrapping, Thickness,
                            VerticalAlignment, Visibility, Window,
                            WindowStartupLocation)
from System.Windows.Controls import (Border, CheckBox, ComboBox, Grid,
                                     Orientation, StackPanel, TextBox)
from System.Windows.Media import FontFamily
from PyNavis.Runtime.Forms import DesignSystem, FluentChrome

from pynavis import clashgroup, lengths, settings, toast

TOOL = 'smart_clash_grouper'
DEFAULTS = {'smart': True, 'rules': [], 'tolerance_m': 2.0, 'keep_existing': True}

T = DesignSystem.Tokens.Current
RULE_IDS = [rule_id for rule_id, _ in clashgroup.RULES]
RULE_LABELS = [label for _, label in clashgroup.RULES]

values = settings.load(TOOL, DEFAULTS)

units = 'Meters'                              # no document open: the stored unit
try:
    from pynavis import app
    units = str(app.get_doc().Units)
except Exception:
    pass

window = Window()
window.Title = 'Grouper Defaults'
window.Width = 420
window.SizeToContent = SizeToContent.Height
window.ResizeMode = ResizeMode.NoResize
window.WindowStartupLocation = WindowStartupLocation.CenterScreen
window.FontFamily = FontFamily('Segoe UI Variable Text, Segoe UI')
window.Background = DesignSystem.Brush(T.Paper)
window.ShowInTaskbar = False
FluentChrome.Apply(window)

body = StackPanel()
body.Margin = Thickness(20, 16, 20, 16)

title = DesignSystem.Text('Clash Grouper defaults', 16, T.Ink, FontWeights.SemiBold)
body.Children.Add(title)
caption = DesignSystem.Text(
    'These preset the dialog; every run can still change them.', 12, T.Muted)
caption.Margin = Thickness(0, 4, 0, 14)
caption.TextWrapping = TextWrapping.Wrap
body.Children.Add(caption)


def section(label):
    text = DesignSystem.Text(label, 12, T.Muted, FontWeights.Medium)
    text.Margin = Thickness(0, 10, 0, 6)
    body.Children.Add(text)


section('Mode')

# A segmented control, the same affordance the grouper itself uses for this
# exact choice. Two radio buttons said the same thing in a different language.
state = {'smart': bool(values['smart'])}
seg_smart = DesignSystem.Secondary(T, 'Smart', None)
seg_custom = DesignSystem.Secondary(T, 'Custom rules', None)
for seg in (seg_smart, seg_custom):
    seg.Height = 30
    seg.MinWidth = 0
    seg.Padding = Thickness(16, 0, 16, 0)
    seg.BorderThickness = Thickness(0)

seg_row = StackPanel()
seg_row.Orientation = Orientation.Horizontal
seg_row.Children.Add(seg_smart)
seg_row.Children.Add(seg_custom)
seg_shell = Border()
seg_shell.CornerRadius = CornerRadius(4)
seg_shell.BorderBrush = DesignSystem.Brush(T.LineStrong)
seg_shell.BorderThickness = Thickness(1)
seg_shell.HorizontalAlignment = HorizontalAlignment.Left
seg_shell.Child = seg_row
body.Children.Add(seg_shell)

mode_caption = DesignSystem.Text('', 12, T.Muted)
mode_caption.Margin = Thickness(0, 8, 0, 0)
mode_caption.TextWrapping = TextWrapping.Wrap
body.Children.Add(mode_caption)

rules_label = DesignSystem.Text('Custom rule chain', 12, T.Muted, FontWeights.Medium)
rules_label.Margin = Thickness(0, 10, 0, 6)
body.Children.Add(rules_label)
rule_rows = StackPanel()
body.Children.Add(rule_rows)
empty_chain = DesignSystem.Text('Add at least one rule to group by.', 12, T.Muted)
empty_chain.Margin = Thickness(0, 0, 0, 6)
body.Children.Add(empty_chain)


def paint_mode():
    """Selected segment carries the accent; the caption states what the mode
    does; the rule chain only exists in Custom, exactly as in the grouper."""
    for seg, on in ((seg_smart, state['smart']), (seg_custom, not state['smart'])):
        seg.Background = DesignSystem.Brush(
            DesignSystem.Accent if on else T.Paper)
        seg.Foreground = DesignSystem.Brush(
            DesignSystem.OnAccent(DesignSystem.Accent) if on else T.Ink)
    mode_caption.Text = (
        'Smart finds the elements causing the clashes and groups them for you.'
        if state['smart'] else
        'Chain rules; each one splits the groups made by the one before.')
    shown = Visibility.Collapsed if state['smart'] else Visibility.Visible
    rules_label.Visibility = shown
    rule_rows.Visibility = shown
    add_btn.Visibility = shown
    empty_chain.Visibility = (
        Visibility.Visible
        if not state['smart'] and rule_rows.Children.Count == 0
        else Visibility.Collapsed)


def set_mode(smart):
    state['smart'] = smart
    paint_mode()


seg_smart.Click += lambda s, e: set_mode(True)
seg_custom.Click += lambda s, e: set_mode(False)


def add_rule_row(rule_id=None):
    row = StackPanel()
    row.Orientation = Orientation.Horizontal
    row.Margin = Thickness(0, 0, 0, 6)

    # The chain is ordered, so each row carries its position, as in the grouper.
    ordinal = DesignSystem.TabularNumber('', 12, T.Muted)
    ordinal.Width = 18
    ordinal.VerticalAlignment = VerticalAlignment.Center
    row.Children.Add(ordinal)

    combo = ComboBox()
    combo.Width = 230
    combo.ItemsSource = RULE_LABELS
    combo.SelectedIndex = RULE_IDS.index(rule_id) if rule_id in RULE_IDS else 0
    row.Children.Add(combo)

    def remove():
        rule_rows.Children.Remove(row)
        renumber()
        paint_mode()

    remove_btn = DesignSystem.Quiet(T, 'Remove', remove)
    remove_btn.Margin = Thickness(8, 0, 0, 0)
    row.Children.Add(remove_btn)
    rule_rows.Children.Add(row)
    renumber()
    paint_mode()


def renumber():
    for index in range(rule_rows.Children.Count):
        rule_rows.Children[index].Children[0].Text = '%d.' % (index + 1)


for saved_id in values['rules']:
    if saved_id in RULE_IDS:
        add_rule_row(saved_id)

add_btn = DesignSystem.Quiet(T, 'Add rule', lambda: add_rule_row())
add_btn.HorizontalAlignment = HorizontalAlignment.Left
body.Children.Add(add_btn)

section('Cluster distance')
tolerance_box = TextBox()
tolerance_box.Text = lengths.format_input(
    lengths.convert(float(values['tolerance_m']), 'Meters', units), units)
tolerance_box.Width = 110
field = DesignSystem.InputField(T, tolerance_box, lengths.suffix(units) or None)
field.HorizontalAlignment = HorizontalAlignment.Left
body.Children.Add(field)
tolerance_error = DesignSystem.Text(
    'Enter a distance greater than 0, %s.' % lengths.hint(units), 12, T.Error)
tolerance_error.TextWrapping = TextWrapping.Wrap
tolerance_error.Margin = Thickness(0, 4, 0, 0)
tolerance_error.Visibility = Visibility.Collapsed
body.Children.Add(tolerance_error)

keep_box = CheckBox()
keep_box.Content = 'Keep existing groups'
keep_box.IsChecked = bool(values['keep_existing'])
keep_box.Foreground = DesignSystem.Brush(T.Ink)
keep_box.Margin = Thickness(0, 12, 0, 0)
body.Children.Add(keep_box)
keep_caption = DesignSystem.Text(
    'Groups you already made by hand stay untouched.', 12, T.Muted)
keep_caption.Margin = Thickness(0, 4, 0, 0)
body.Children.Add(keep_caption)


def tolerance_value():
    """The typed distance in metres; 0 when it does not read."""
    typed = lengths.parse(tolerance_box.Text, units)
    return 0.0 if typed is None else lengths.convert(typed, units, 'Meters')


def validate(*args):
    """Live: the error appears as you type and clears the same way, instead of
    waiting for Save to tell you."""
    bad = tolerance_value() <= 0
    tolerance_error.Visibility = Visibility.Visible if bad else Visibility.Collapsed
    field.BorderBrush = DesignSystem.Brush(T.Error if bad else T.LineStrong)
    save_btn.IsEnabled = not bad


def save():
    if tolerance_value() <= 0:
        validate()
        return
    rules = []
    for row in rule_rows.Children:
        rules.append(RULE_IDS[row.Children[1].SelectedIndex])
    settings.save(TOOL, {
        'smart': state['smart'],
        'rules': rules,
        'tolerance_m': tolerance_value(),
        'keep_existing': keep_box.IsChecked == True,
    })
    toast.success('Grouper defaults saved.')
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

tolerance_box.TextChanged += validate
paint_mode()
validate()

window.Content = body
window.ShowDialog()
