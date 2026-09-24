# -*- coding: utf-8 -*-
"""Nudging the section planes or box from the keyboard.

Shared by the Section Nudge dock panel and the six chord-only bundles beside
it, so a key pressed in the panel and a chord pressed over the 3D view do
exactly the same thing to exactly the same state.

Pure half first: the session state (which target, what step), what an action
does to it, which key means which action, and the words for the readout.
Nothing above the API half imports Navisworks, so it is unit-testable
anywhere. The API half reads the current viewpoint's clip planes and writes
one edited copy back per nudge, which is also what makes each nudge one
undo step.

Targets are 'box' or a plane index 0 to 5. In Planes mode the six are the
planes the Sectioning tab lists. In Box mode they are the faces, in the
order +X, -X, +Y, -Y, +Z, -Z of the box's own frame, and nudging a face
resizes the box. "In" always shrinks what is kept and "Out" grows it; for
the box target In and Out move it down and up, so the two nudge chords stay
useful whatever is selected.

No f-strings: this runs on IronPython 3.4 as well as CPython.
"""

import sys
import types

# --- pure half ---------------------------------------------------------------

TARGETS = ('box', 0, 1, 2, 3, 4, 5)
DEFAULT_STEP_MM = 100.0
MIN_STEP_MM = 0.1
MAX_STEP_MM = 1e7

# Session variable both shells share.
STATE_VAR = 'section_nudge.state'

# Face order in Box mode, and the inward normal of each in the box frame.
FACES = (
    ('+X', (-1.0, 0.0, 0.0)), ('-X', (1.0, 0.0, 0.0)),
    ('+Y', (0.0, -1.0, 0.0)), ('-Y', (0.0, 1.0, 0.0)),
    ('+Z', (0.0, 0.0, -1.0)), ('-Z', (0.0, 0.0, 1.0)),
)

_BOX_MOVES = {'x+': (1.0, 0.0, 0.0), 'x-': (-1.0, 0.0, 0.0),
              'y+': (0.0, 1.0, 0.0), 'y-': (0.0, -1.0, 0.0),
              'z+': (0.0, 0.0, 1.0), 'z-': (0.0, 0.0, -1.0)}

_KEYS_ANY = {'Left': 'prev', 'Right': 'next', 'Tab': 'next',
             'Add': 'double', 'OemPlus': 'double',
             'Subtract': 'halve', 'OemMinus': 'halve'}
_KEYS_PLANE = {'Up': 'in', 'Down': 'out'}
_KEYS_BOX = {'Up': 'y+', 'Down': 'y-', 'Left': 'x-', 'Right': 'x+',
             'PageUp': 'z+', 'Prior': 'z+', 'PageDown': 'z-', 'Next': 'z-'}

_INCHES_PER_UNIT = {'Feet': 12.0, 'Inches': 1.0, 'Yards': 36.0, 'Miles': 63360.0}
_SUFFIX = {'Meters': 'm', 'Centimeters': 'cm', 'Millimeters': 'mm',
           'Kilometers': 'km', 'Micrometers': 'um', 'Mils': 'mil',
           'Microinches': 'uin'}


def normalize(state):
    """A well-formed state from whatever the session holds."""
    target = 'box'
    step = DEFAULT_STEP_MM
    if isinstance(state, dict):
        raw = state.get('target', 'box')
        try:
            index = int(raw)
        except (TypeError, ValueError):
            index = None
        if index is not None and 0 <= index <= 5:
            target = index
        try:
            step = float(state.get('step_mm', DEFAULT_STEP_MM))
        except (TypeError, ValueError):
            step = DEFAULT_STEP_MM
        if not (MIN_STEP_MM <= step <= MAX_STEP_MM):
            step = DEFAULT_STEP_MM
    return {'target': target, 'step_mm': step}


def plan(state, action, per_mm=1.0):
    """What an action does: {'state': new state, 'edit': what to apply to the
    clip planes or None, 'problem': text when nothing sensible could happen}.

    per_mm is document units per millimetre, so edits come out in document
    units ready for the API.
    """
    state = normalize(state)
    target = state['target']
    step = state['step_mm'] * per_mm
    new_state = dict(state)
    edit = None
    problem = ''

    if action == 'next' or action == 'prev':
        position = TARGETS.index(target)
        offset = 1 if action == 'next' else -1
        new_state['target'] = TARGETS[(position + offset) % len(TARGETS)]
    elif action.startswith('target:'):
        new_state['target'] = normalize({'target': action[7:], 'step_mm': state['step_mm']})['target']
    elif action == 'double':
        new_state['step_mm'] = min(MAX_STEP_MM, state['step_mm'] * 2.0)
    elif action == 'halve':
        new_state['step_mm'] = max(MIN_STEP_MM, state['step_mm'] / 2.0)
    elif action.startswith('step:'):
        try:
            typed = float(action[5:].strip())
        except ValueError:
            typed = None
        if typed is None or typed <= 0:
            problem = 'That is not a distance'
        else:
            new_state['step_mm'] = min(MAX_STEP_MM, max(MIN_STEP_MM, typed))
    elif action in ('in', 'out'):
        sign = 1.0 if action == 'in' else -1.0
        if target == 'box':
            edit = {'kind': 'box', 'vector': (0.0, 0.0, -sign * step)}
        else:
            edit = {'kind': 'plane', 'index': target, 'distance': sign * step}
    elif action in _BOX_MOVES:
        if target == 'box':
            unit = _BOX_MOVES[action]
            edit = {'kind': 'box', 'vector': (unit[0] * step, unit[1] * step, unit[2] * step)}
        else:
            problem = 'Pick the box to move it; a plane only moves in and out'
    else:
        problem = 'Unknown action %s' % action
    return {'state': new_state, 'edit': edit, 'problem': problem}


def action_for_key(key_name, target):
    """The action a WPF Key name means for the current target, or None."""
    if key_name in _KEYS_ANY and not (target == 'box' and key_name in _KEYS_BOX):
        return _KEYS_ANY[key_name]
    table = _KEYS_BOX if target == 'box' else _KEYS_PLANE
    if key_name in table:
        return table[key_name]
    if key_name in _KEYS_ANY:
        return _KEYS_ANY[key_name]
    digit = None
    if len(key_name) == 2 and key_name[0] == 'D' and key_name[1].isdigit():
        digit = int(key_name[1])
    elif key_name.startswith('NumPad') and key_name[6:].isdigit():
        digit = int(key_name[6:])
    if digit == 0:
        return 'target:box'
    if digit is not None and 1 <= digit <= 6:
        return 'target:%d' % (digit - 1)
    return None


def target_name(target):
    return 'Box' if target == 'box' else 'Plane %d' % (target + 1)


def format_length(value, units):
    """A length in document units as the readout shows it."""
    per_unit = _INCHES_PER_UNIT.get(units)
    if per_unit is not None:
        return _feet_inches(value * per_unit)
    return '%.3f %s' % (value, _SUFFIX.get(units, units))


def _feet_inches(total_inches, denominator=16):
    sign = '-' if total_inches < 0 else ''
    ticks = int(round(abs(total_inches) * denominator))
    feet, rest = divmod(ticks, 12 * denominator)
    inches, num = divmod(rest, denominator)
    text = '%s%dft %din' % (sign, feet, inches)
    if num:
        g = _gcd(num, denominator)
        text += ' %d/%d' % (num // g, denominator // g)
    return text


def _gcd(a, b):
    while b:
        a, b = b, a % b
    return a


def _normal_name(normal):
    for name, (x, y, z) in FACES:
        if abs(normal[0] + x) < 1e-6 and abs(normal[1] + y) < 1e-6 and abs(normal[2] + z) < 1e-6:
            return name                       # FACES holds inward normals
    return '(%.2f, %.2f, %.2f)' % (normal[0], normal[1], normal[2])


def describe(state, snapshot, units, per_mm=1.0):
    """{'target', 'readout', 'step'}: the three lines the panel shows."""
    state = normalize(state)
    target = state['target']
    text = {'target': target_name(target),
            'step': format_length(state['step_mm'] * per_mm, units),
            'readout': ''}
    if snapshot is None:
        text['readout'] = 'No document'
    elif not snapshot.get('enabled'):
        text['readout'] = 'Sectioning is off'
    elif target == 'box':
        box = snapshot.get('box')
        if not box:
            text['readout'] = 'Six planes; arrows move them all together'
        else:
            low, high = box['min'], box['max']
            centre = [(low[i] + high[i]) / 2.0 for i in range(3)]
            size = [high[i] - low[i] for i in range(3)]
            suffix = format_length(0, units).split(' ', 1)[-1] if units not in _INCHES_PER_UNIT else ''
            if units in _INCHES_PER_UNIT:
                text['readout'] = 'Centre %s, %s, %s; size %s x %s x %s' % tuple(
                    [format_length(v, units) for v in centre] + [format_length(v, units) for v in size])
            else:
                text['readout'] = 'Centre %.3f, %.3f, %.3f %s; size %.3f x %.3f x %.3f %s' % (
                    centre[0], centre[1], centre[2], suffix, size[0], size[1], size[2], suffix)
    else:
        planes = snapshot.get('planes') or []
        if target >= len(planes):
            text['readout'] = 'This view has no plane %d' % (target + 1)
        else:
            plane = planes[target]
            text['readout'] = '%s, normal %s, %s along it' % (
                'On' if plane.get('enabled') else 'Off',
                _normal_name(plane.get('normal', (0.0, 0.0, 0.0))),
                format_length(plane.get('distance', 0.0), units))
    return text


def toast_for(state, action, edit, units, per_mm=1.0):
    """One line for the chord bundles, which have no panel to update."""
    state = normalize(state)
    if edit is not None:
        step = format_length(state['step_mm'] * per_mm, units)
        if edit['kind'] == 'box':
            words = {'x+': 'in +X', 'x-': 'in -X', 'y+': 'in +Y', 'y-': 'in -Y',
                     'z+': 'up', 'z-': 'down', 'in': 'down', 'out': 'up'}
            return 'Box %s by %s' % (words.get(action, action), step)
        return '%s %s by %s' % (target_name(edit['index']), action, step)
    if action in ('double', 'halve') or action.startswith('step:'):
        return 'Step: %s' % format_length(state['step_mm'] * per_mm, units)
    return 'Target: %s' % target_name(state['target'])


# --- API half (Navisworks only; lazy imports keep the module pure) -----------

_SESSION_KEY = 'pynavis_section_nudge_session'


def session():
    """A holder that survives Reload: lib modules are dropped from sys.modules
    before every run, but a module object under our own key is not."""
    holder = sys.modules.get(_SESSION_KEY)
    if holder is None:
        holder = types.ModuleType(_SESSION_KEY)
        holder.pane_detach = None
        sys.modules[_SESSION_KEY] = holder
    return holder


def load_state():
    from pynavis import script
    return normalize(script.get_envvar(STATE_VAR))


def save_state(state):
    from pynavis import script
    script.set_envvar(STATE_VAR, normalize(state))


def per_mm(doc):
    """Document units per millimetre."""
    from pynavis._api import Api
    return Api.UnitConversion.ScaleFactor(Api.Units.Millimeters, doc.Units)


def _point(p):
    return (float(p.X), float(p.Y), float(p.Z))


def read(doc):
    """A plain snapshot of the current viewpoint's clip planes, or None when
    the document has none to read."""
    try:
        planes = doc.CurrentViewpoint.ClipPlanes
    except Exception:
        return None
    from pynavis._api import Api
    mode = 'box' if planes.Mode == Api.ClipPlaneSetMode.Box else 'planes'
    snapshot = {'enabled': bool(planes.Enabled), 'mode': mode, 'planes': [], 'box': None}
    if mode == 'box':
        try:
            box = planes.Box
            snapshot['box'] = {'min': _point(box.Min), 'max': _point(box.Max)}
        except Exception:
            snapshot['box'] = None
        for name, inward in FACES:
            snapshot['planes'].append({'enabled': True, 'normal': inward,
                                       'origin': (0.0, 0.0, 0.0), 'distance': 0.0})
    else:
        for index in range(min(6, planes.Size())):
            plane = planes.Get(index)
            snapshot['planes'].append({
                'enabled': bool(plane.Enabled),
                'normal': _point(plane.Normal),
                'origin': _point(plane.Origin),
                'distance': float(plane.Distance)})
    return snapshot


def apply(edit, doc):
    """Writes one edit onto a copy of the current viewpoint and swaps it in,
    which is one undo step. Returns '' or a problem."""
    from pynavis._api import Api
    viewpoint = doc.CurrentViewpoint.CreateCopy()
    planes = viewpoint.ClipPlanes
    if edit['kind'] == 'box':
        v = edit['vector']
        planes.Transform(Api.Transform3D.CreateTranslation(Api.Vector3D(v[0], v[1], v[2])))
    elif planes.Mode == Api.ClipPlaneSetMode.Box:
        problem = _resize_box(planes, edit['index'], edit['distance'], Api)
        if problem:
            return problem
    else:
        if edit['index'] >= planes.Size():
            return 'This view has no plane %d' % (edit['index'] + 1)
        plane = planes.Get(edit['index'])
        n = plane.Normal
        d = edit['distance']
        plane.Translate(Api.Vector3D(n.X * d, n.Y * d, n.Z * d))
    doc.CurrentViewpoint.CopyFrom(viewpoint)
    return ''


def _resize_box(planes, face, distance, Api):
    box = planes.Box
    low, high = list(_point(box.Min)), list(_point(box.Max))
    axis = face // 2
    if face % 2 == 0:
        high[axis] -= distance              # +X face moves in = max shrinks
    else:
        low[axis] += distance               # -X face moves in = min grows
    if high[axis] - low[axis] <= 0:
        return 'The box would turn inside out'
    planes.Box = Api.BoundingBox3D(Api.Point3D(low[0], low[1], low[2]),
                                   Api.Point3D(high[0], high[1], high[2]))
    return ''


def enable(doc):
    """Switches sectioning on in the current viewpoint, one undo step."""
    viewpoint = doc.CurrentViewpoint.CreateCopy()
    viewpoint.ClipPlanes.Enabled = True
    doc.CurrentViewpoint.CopyFrom(viewpoint)


def run_action(action, doc=None):
    """The one entry point both shells use: plan, apply, remember.
    Returns {'state', 'edit', 'problem', 'toast'}."""
    from pynavis import app
    document = doc if doc is not None else app.get_doc()
    state = load_state()
    scale = per_mm(document)
    units = str(document.Units)
    result = plan(state, action, scale)
    if result['edit'] is not None and not result['problem']:
        result['problem'] = apply(result['edit'], document)
    save_state(result['state'])
    result['toast'] = (result['problem'] or
                       toast_for(result['state'], action, result['edit'], units, scale))
    return result
