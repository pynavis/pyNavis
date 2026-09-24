# -*- coding: utf-8 -*-
"""The Section Nudge panel: move the section box or planes from the keyboard.

Pick a target (the box, or one of the six planes or faces), set a step, and
nudge. Every nudge is one viewpoint write, so Ctrl+Z steps back one nudge.
The behaviour lives in lib/sectionnudge.py, which the six chord bundles beside
this panel share, so a key here and a chord over the view do the same thing.

Reload re-runs this script against fresh controls; the previous run's timer is
stopped through the session holder first, the way the Viewpoint Tracker does.
"""

import clr

import sectionnudge

from pynavis import app, script, toast

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')

from System import TimeSpan
from System.Windows import FontWeights, Visibility
from System.Windows.Input import Key
from System.Windows.Threading import DispatcherPriority, DispatcherTimer

# How often the readout follows changes made outside the panel.
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
targets = {'box': find('TargetBox')}
for i in range(6):
    targets[i] = find('Target%d' % (i + 1))
scale = [1.0]                   # document units per mm, refreshed with the readout


def safely(work):
    """An exception escaping a WPF handler on the UI thread takes Navisworks
    down with it; a panel is not worth that."""
    try:
        work()
    except Exception as error:
        log.error('Section Nudge: %s' % error)


def refresh():
    doc = app.get_doc()
    state = sectionnudge.load_state()
    snapshot = sectionnudge.read(doc)
    units = str(doc.Units)
    scale[0] = sectionnudge.per_mm(doc)
    text = sectionnudge.describe(state, snapshot, units, scale[0])

    find('TargetName').Text = text['target']
    find('Readout').Text = text['readout']
    find('StepUnits').Text = units.lower()
    if not step_box.IsKeyboardFocusWithin:
        step_box.Text = '%g' % (state['step_mm'] * scale[0])
    for key, button in targets.items():
        button.FontWeight = FontWeights.Bold if key == state['target'] else FontWeights.Normal
    is_box = state['target'] == 'box'
    find('BoxPad').Visibility = Visibility.Visible if is_box else Visibility.Collapsed
    find('PlanePad').Visibility = Visibility.Collapsed if is_box else Visibility.Visible
    off = snapshot is not None and not snapshot['enabled']
    find('SectionOn').Visibility = Visibility.Visible if off else Visibility.Collapsed


def act(action):
    result = sectionnudge.run_action(action)
    if result['problem']:
        toast.warning(result['problem'])
    refresh()


def apply_typed_step():
    try:
        typed = float(step_box.Text.strip())
    except ValueError:
        toast.warning('That is not a distance')
        return
    if typed <= 0:
        toast.warning('The step must be more than zero')
        return
    act('step:%r' % (typed / scale[0] if scale[0] else typed))
    root.Focus()


def wire(name, action):
    find(name).Click += lambda sender, args: safely(lambda: act(action))


wire('TargetBox', 'target:box')
for i in range(6):
    wire('Target%d' % (i + 1), 'target:%d' % i)
wire('NudgeIn', 'in')
wire('NudgeOut', 'out')
wire('MoveXPlus', 'x+')
wire('MoveXMinus', 'x-')
wire('MoveYPlus', 'y+')
wire('MoveYMinus', 'y-')
wire('MoveZPlus', 'z+')
wire('MoveZMinus', 'z-')
wire('StepHalve', 'halve')
wire('StepDouble', 'double')


def turn_on(sender, args):
    def work():
        sectionnudge.enable(app.get_doc())
        refresh()
    safely(work)


find('SectionOn').Click += turn_on


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
