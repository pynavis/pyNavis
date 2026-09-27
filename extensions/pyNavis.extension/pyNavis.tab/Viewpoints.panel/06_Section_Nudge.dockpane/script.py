# -*- coding: utf-8 -*-
"""The Section Nudge panel: move the section box or planes from the keyboard.

The panel is a remote: the six faces laid out as an unfolded box and named the
way you see them, Front being the face that looks at you, with the world axis
each faces printed under its name. The camera's right, up and forward are
snapped to world axes (the way the ViewCube reads a view) and the names follow
the camera on the readout timer, so orbit round the model and Front stays the
face in front of you. Two tabs: Adjust holds the
remote and a face is the target; Move is the whole box. Face targets nudge
In and Out; the box moves left, right, up,
down, nearer and farther on the screen: along the nearest world axes (the
compass ring round the ViewCube) or exactly along the screen (the cube
itself), as the World axes / Screen switch says. Every
nudge is one viewpoint write, so Ctrl+Z steps back one nudge. The behaviour
lives in lib/sectionnudge.py, which the six chord bundles beside this panel
share, so a key here and a chord over the view do the same thing.

Reload re-runs this script against fresh controls; the previous run's timer is
stopped through the session holder first, the way the Viewpoint Tracker does.
"""

import clr

import sectionnudge

from pynavis import app, script, section, selection, settings, toast

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')

from System import TimeSpan
from System.Windows import FontWeights, Thickness, Visibility
from System.Windows.Controls import Control
from System.Windows.Media import BrushConverter
from System.Windows.Controls import Button
from System.Windows.Input import Key
from System.Windows.Threading import DispatcherPriority, DispatcherTimer

# How often the readout and the face names follow the camera and changes made
# outside the panel.
REFRESH_MS = 1000

pane = __pane__
log = script.get_logger()
held = sectionnudge.session()

previous = getattr(held, 'pane_detach', None)
if previous is not None:
    previous()

find = pane.Find
root = find('Root')
step_box = find('StepBox')
tabs = find('Tabs')
adjust_tab, move_tab = find('AdjustTab'), find('MoveTab')
cells = dict((name, find('Cell' + name)) for name in sectionnudge.CELLS)
cell_names = dict((name, find('Cell%sName' % name)) for name in sectionnudge.CELLS)
cell_axes = dict((name, find('Cell%sAxis' % name)) for name in sectionnudge.CELLS)
others_panel = find('Others')
presets_panel = find('Presets')
moves_buttons = {'world': find('MovesWorld'), 'screen': find('MovesScreen')}

scale = [1.0]                   # document units per mm, refreshed with the readout

# The active choice gets the same fill the buttons show on hover, so the
# selected face reads at a glance rather than only by weight; the pair is
# picked for the host theme so it stays visible on dark.
_brush = BrushConverter()
if script.is_dark_theme():
    ACTIVE_FILL, ACTIVE_EDGE = _brush.ConvertFromString('#3A5A7A'), _brush.ConvertFromString('#7FB2E5')
else:
    ACTIVE_FILL, ACTIVE_EDGE = _brush.ConvertFromString('#BEE6FD'), _brush.ConvertFromString('#3C7FB1')


def mark(button, active):
    """Bold and filled when active; back to the theme's own look when not."""
    button.FontWeight = FontWeights.Bold if active else FontWeights.Normal
    if active:
        button.Background = ACTIVE_FILL
        button.BorderBrush = ACTIVE_EDGE
    else:
        button.ClearValue(Control.BackgroundProperty)
        button.ClearValue(Control.BorderBrushProperty)
last_face = [0]                 # the face to come back to when Adjust reopens
syncing = [False]               # refresh is moving the tab, not the user
cell_index = {}                 # cell name -> plane index, from the last refresh
shown_units = [None]            # which units the presets were built for


def safely(work):
    """An exception escaping a WPF handler on the UI thread takes Navisworks
    down with it; a panel is not worth that."""
    try:
        work()
    except Exception as error:
        log.error('Section Nudge: %s' % error)


def small_button(text, action):
    button = Button()
    button.Content = text
    button.Margin = Thickness(1)
    button.Padding = Thickness(6, 2, 6, 2)
    button.Click += lambda sender, args: safely(lambda: act(action))
    return button


def fill_presets(units):
    presets_panel.Children.Clear()
    for label, mm in sectionnudge.presets(units):
        presets_panel.Children.Add(small_button(label, 'step:%r' % mm))
    shown_units[0] = units


def fill_others(indices, labels, target):
    others_panel.Children.Clear()
    for index in indices:
        button = small_button(labels.get(index, sectionnudge.target_name(index)),
                              'target:%d' % index)
        mark(button, index == target)
        others_panel.Children.Add(button)
    others_panel.Visibility = Visibility.Visible if indices else Visibility.Collapsed


def refresh():
    doc = app.get_doc()
    state = sectionnudge.load_state()
    snapshot = sectionnudge.read(doc, note=log.warning)
    frame = sectionnudge.camera_frame(doc)
    units = str(doc.Units)
    scale[0] = sectionnudge.per_mm(doc)
    text = sectionnudge.describe(state, snapshot, units, scale[0], frame)
    placed = sectionnudge.layout(snapshot, frame)
    target = state['target']

    find('Readout').Text = text['readout']
    imperial = units in ('Feet', 'Inches', 'Yards', 'Miles')
    find('StepUnits').Text = '' if imperial else units.lower()
    if not step_box.IsKeyboardFocusWithin:
        step = state['step_mm'] * scale[0]
        step_box.Text = text['step'] if imperial else '%g' % step
    if units != shown_units[0]:
        fill_presets(units)

    is_box = target == 'box'
    if not is_box:
        last_face[0] = target
    syncing[0] = True
    try:
        tabs.SelectedItem = move_tab if is_box else adjust_tab
    finally:
        syncing[0] = False
    for name, button in moves_buttons.items():
        mark(button, name == state['moves'])
    planes = (snapshot or {}).get('planes') or []
    cell_index.clear()
    for name in sectionnudge.CELLS:
        index = placed['cells'][name]
        cell_index[name] = index
        cells[name].IsEnabled = index is not None
        mark(cells[name], index == target)
        if index is None or index >= len(planes):
            cell_axes[name].Text = ''
        else:
            inward = planes[index].get('normal', (0.0, 0.0, 0.0))
            cell_axes[name].Text = sectionnudge.axis_label((-inward[0], -inward[1], -inward[2]))
    fill_others(placed['others'], placed['labels'], target)

    off = snapshot is not None and not snapshot['enabled']
    find('SectionOn').Visibility = Visibility.Visible if off else Visibility.Collapsed
    find('SectionOff').IsEnabled = snapshot is not None and not off


def act(action):
    result = sectionnudge.run_action(action)
    if result['problem']:
        toast.warning(result['problem'])
    refresh()


def apply_typed_step():
    typed = sectionnudge.parse_length(step_box.Text, str(app.get_doc().Units))
    if typed is None:
        toast.warning('That is not a distance')
        return
    if typed <= 0:
        toast.warning('The step must be more than zero')
        return
    act('step:%r' % (typed / scale[0] if scale[0] else typed))
    root.Focus()


def wire(name, action):
    find(name).Click += lambda sender, args: safely(lambda: act(action))


def pick_cell(name):
    index = cell_index.get(name)
    if index is not None:
        act('target:%d' % index)


for cell in sectionnudge.CELLS:
    cells[cell].Click += (lambda n: lambda sender, args: safely(lambda: pick_cell(n)))(cell)
wire('NudgeIn', 'in')
wire('NudgeOut', 'out')
wire('MoveLeft', 'left')
wire('MoveRight', 'right')
wire('MoveUp', 'up')
wire('MoveDown', 'down')
wire('MoveNearer', 'nearer')
wire('MoveFarther', 'farther')
wire('MovesWorld', 'moves:world')
wire('MovesScreen', 'moves:screen')
wire('StepHalve', 'halve')
wire('StepDouble', 'double')


def turn_on(sender, args):
    def work():
        sectionnudge.enable(app.get_doc())
        refresh()
    safely(work)


def turn_off(sender, args):
    def work():
        result = section.clear()
        toast.show(result.level, result.message, result.detail)
        refresh()
    safely(work)


def fit(sender, args):
    def work():
        values = settings.load(section.TOOL, section.DEFAULTS)
        result = section.fit_to_selection(selection.get_items(), values)
        toast.show(result.level, result.message, result.detail)
        refresh()
    safely(work)


def on_tab(sender, args):
    # The Move tab is the box target and Adjust is a face, so switching tabs
    # switches targets; refresh moving the tab to match the target must not
    # bounce back through here.
    if syncing[0] or args.Source is not tabs:
        return
    def work():
        if tabs.SelectedItem is move_tab:
            act('target:box')
        else:
            act('target:%d' % last_face[0])
    safely(work)


tabs.SelectionChanged += on_tab
find('SectionOn').Click += turn_on
find('SectionOff').Click += turn_off
find('FitSelection').Click += fit


def on_key(sender, args):
    def work():
        name = args.Key.ToString()
        if step_box.IsKeyboardFocusWithin:
            # Typing a number: only Enter and Escape mean anything here.
            if args.Key == Key.Enter:
                args.Handled = True
                apply_typed_step()
            elif args.Key == Key.Escape:
                args.Handled = True
                root.Focus()
            return
        action = sectionnudge.action_for_key(name, sectionnudge.load_state()['target'])
        if action is not None:
            args.Handled = True
            act(action)
    safely(work)


root.PreviewKeyDown += on_key
root.PreviewMouseDown += lambda sender, args: (
    None if step_box.IsMouseOver else root.Focus())

timer = DispatcherTimer(DispatcherPriority.Background)
timer.Interval = TimeSpan.FromMilliseconds(REFRESH_MS)


def tick(sender, args):
    if pane.Visible:
        safely(refresh)


timer.Tick += tick
timer.Start()


def detach():
    try:
        timer.Stop()
    except Exception:
        pass


held.pane_detach = detach
safely(refresh)
