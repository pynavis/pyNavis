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

Directions are the screen's, not the world's. The camera's right, up and
forward are snapped to the nearest world axes (a "frame", the way the
ViewCube reads a view) and box moves are left, right, up, down, nearer and
farther in that frame. Faces are named Front, Back, Left, Right, Top and
Bottom from the same frame, so what the panel calls Front is the face
looking at you, whatever world axis that happens to be. The state's 'moves'
picks which frame the box moves in: 'world' is the snapped one, so the box
stays on the building's grid like the compass ring round the ViewCube;
'screen' is the camera's exact axes, so Left is exactly screen-left from
any angle, like the cube itself.

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
MOVES = ('world', 'screen')

# Face order in Box mode, and the inward normal of each in the box frame.
FACES = (
    ('+X', (-1.0, 0.0, 0.0)), ('-X', (1.0, 0.0, 0.0)),
    ('+Y', (0.0, -1.0, 0.0)), ('-Y', (0.0, 1.0, 0.0)),
    ('+Z', (0.0, 0.0, -1.0)), ('-Z', (0.0, 0.0, 1.0)),
)

# Screen-relative box moves: which frame axis, and which way along it.
_SCREEN_MOVES = {'right': ('right', 1.0), 'left': ('right', -1.0),
                 'up': ('up', 1.0), 'down': ('up', -1.0),
                 'nearer': ('forward', -1.0), 'farther': ('forward', 1.0)}

# The six cells of the remote, and the frame direction each one faces
# outward along (a face's outward normal is minus its inward one).
CELLS = ('Top', 'Left', 'Front', 'Right', 'Back', 'Bottom')
_CELL_AXES = {'Front': ('forward', -1.0), 'Back': ('forward', 1.0),
              'Right': ('right', 1.0), 'Left': ('right', -1.0),
              'Top': ('up', 1.0), 'Bottom': ('up', -1.0)}

# The frame when there is no camera to read: a plan view, Y up the screen.
_PLAN_FRAME = {'right': (1.0, 0.0, 0.0), 'up': (0.0, 1.0, 0.0), 'forward': (0.0, 0.0, -1.0)}
_AXES = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))
_AXIS_NAMES = ('X', 'Y', 'Z')
# A normal counts as axis-aligned when it is within about 18 degrees of one.
_ALIGNED = 0.95

_KEYS_ANY = {'Left': 'prev', 'Right': 'next', 'Tab': 'next',
             'Add': 'double', 'OemPlus': 'double',
             'Subtract': 'halve', 'OemMinus': 'halve'}
_KEYS_PLANE = {'Up': 'in', 'Down': 'out'}
_KEYS_BOX = {'Up': 'up', 'Down': 'down', 'Left': 'left', 'Right': 'right',
             'PageUp': 'nearer', 'Prior': 'nearer', 'PageDown': 'farther', 'Next': 'farther'}

_PRESETS_METRIC = (('10 mm', 10.0), ('50 mm', 50.0), ('100 mm', 100.0),
                   ('500 mm', 500.0), ('1 m', 1000.0))
_PRESETS_IMPERIAL = (('1/4in', 6.35), ('1in', 25.4), ('6in', 152.4),
                     ('1ft', 304.8), ('5ft', 1524.0))

_INCHES_PER_UNIT = {'Feet': 12.0, 'Inches': 1.0, 'Yards': 36.0, 'Miles': 63360.0}
_SUFFIX = {'Meters': 'm', 'Centimeters': 'cm', 'Millimeters': 'mm',
           'Kilometers': 'km', 'Micrometers': 'um', 'Mils': 'mil',
           'Microinches': 'uin'}


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def _scaled(v, k):
    return (v[0] * k, v[1] * k, v[2] * k)


def _rotate(rotation, v):
    """v turned by a Rotation3D given as (A, B, C, D): vector part then
    scalar, the order Rotation3D(a, b, c, d) takes them in. None when the
    rotation is degenerate."""
    a, b, c, d = (float(rotation[0]), float(rotation[1]),
                  float(rotation[2]), float(rotation[3]))
    size = (a * a + b * b + c * c + d * d) ** 0.5
    if size == 0.0:
        return None
    a, b, c, d = a / size, b / size, c / size, d / size
    axis = (a, b, c)
    once = _cross(axis, v)
    twice = _cross(axis, once)
    return (v[0] + 2.0 * (d * once[0] + twice[0]),
            v[1] + 2.0 * (d * once[1] + twice[1]),
            v[2] + 2.0 * (d * once[2] + twice[2]))


def _nearest_axis(v, taken=()):
    """The signed world axis v leans towards most, skipping any in taken.
    None when v has no length."""
    best, best_dot = None, 0.0
    for index, axis in enumerate(_AXES):
        if index in taken:
            continue
        d = _dot(v, axis)
        if abs(d) > best_dot:
            best, best_dot = (index, 1.0 if d >= 0 else -1.0), abs(d)
    return best


def frame_from_vectors(forward, up):
    """The camera's forward and up, snapped to world axes: {'right', 'up',
    'forward'} as signed unit axes. Forward claims its nearest axis first,
    up the nearest of the two left, right follows from those. A degenerate
    camera gives the plan-view frame."""
    f = _nearest_axis(forward)
    if f is None:
        return dict(_PLAN_FRAME)
    u = _nearest_axis(up, taken=(f[0],))
    if u is None:
        return dict(_PLAN_FRAME)
    forward_axis = _scaled(_AXES[f[0]], f[1])
    up_axis = _scaled(_AXES[u[0]], u[1])
    right_axis = _cross(forward_axis, up_axis)
    return {'right': tuple(float(x) + 0.0 for x in right_axis),
            'up': tuple(float(x) + 0.0 for x in up_axis),
            'forward': tuple(float(x) + 0.0 for x in forward_axis)}


def _unit(v):
    size = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) ** 0.5
    if size == 0.0:
        return None
    return (v[0] / size, v[1] / size, v[2] / size)


def frame_for(rotation, snap=True):
    """The frame of a camera carrying this Rotation3D (A, B, C, D): snapped
    to world axes, or with snap False the camera's exact right, up and
    forward as unit vectors. The plan-view frame when there is none."""
    if rotation is None:
        return dict(_PLAN_FRAME)
    forward = _rotate(rotation, (0.0, 0.0, -1.0))
    up = _rotate(rotation, (0.0, 1.0, 0.0))
    if forward is None or up is None:
        return dict(_PLAN_FRAME)
    if snap:
        return frame_from_vectors(forward, up)
    forward, up = _unit(forward), _unit(up)
    if forward is None or up is None:
        return dict(_PLAN_FRAME)
    return {'right': _cross(forward, up), 'up': up, 'forward': forward}


def face_names(frame):
    """{'Front': outward axis, ...} for the six cells in this frame."""
    return dict((name, _scaled(frame[axis], sign))
                for name, (axis, sign) in _CELL_AXES.items())


def name_for_normal(inward, frame):
    """The cell whose outward direction this inward normal faces, or None
    when the normal is not close to any world axis."""
    outward = _scaled(inward, -1.0)
    for name, axis in face_names(frame).items():
        if _dot(outward, axis) >= _ALIGNED:
            return name
    return None


def axis_label(outward):
    """'+X' style label for an axis-aligned direction, or 'tilted'."""
    nearest = _nearest_axis(outward)
    if nearest is None or abs(_dot(outward, _AXES[nearest[0]])) < _ALIGNED:
        return 'tilted'
    return ('+' if nearest[1] > 0 else '-') + _AXIS_NAMES[nearest[0]]


def layout(snapshot, frame):
    """Where each plane sits on the remote: {'cells': {cell: index or None},
    'others': [indices with no cell], 'labels': {index: text}}. In Box mode
    every face has a cell. In Planes mode the first plane facing a cell's
    way takes it and the rest, tilted or duplicate, go under others."""
    cells = dict.fromkeys(CELLS)
    others = []
    labels = {}
    if snapshot is None:
        return {'cells': cells, 'others': others, 'labels': labels}
    planes = snapshot.get('planes') or []
    for index, plane in enumerate(planes):
        inward = plane.get('normal', (0.0, 0.0, 0.0))
        name = name_for_normal(inward, frame)
        if name is None:
            labels[index] = 'Plane %d, tilted' % (index + 1)
        elif not plane.get('enabled', True):
            labels[index] = 'Plane %d, off' % (index + 1)
        else:
            labels[index] = 'Plane %d, %s (%s)' % (index + 1, name, axis_label(_scaled(inward, -1.0)))
        if name is not None and cells[name] is None:
            cells[name] = index
        else:
            others.append(index)
    return {'cells': cells, 'others': others, 'labels': labels}


def presets(units):
    """The step presets a document in these units wants: [(label, mm), ...]."""
    if units in _INCHES_PER_UNIT:
        return list(_PRESETS_IMPERIAL)
    return list(_PRESETS_METRIC)


def normalize(state):
    """A well-formed state from whatever the session holds."""
    target = 'box'
    step = DEFAULT_STEP_MM
    moves = MOVES[0]
    if isinstance(state, dict):
        if state.get('moves') in MOVES:
            moves = state['moves']
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
    return {'target': target, 'step_mm': step, 'moves': moves}


def plan(state, action, per_mm=1.0, frame=None):
    """What an action does: {'state': new state, 'edit': what to apply to the
    clip planes or None, 'problem': text when nothing sensible could happen}.

    per_mm is document units per millimetre, so edits come out in document
    units ready for the API. frame is the camera frame the box moves follow,
    already snapped or not as the state's 'moves' asks; None means the plan
    view.
    """
    if frame is None:
        frame = _PLAN_FRAME
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
    elif action.startswith('moves:'):
        if action[6:] in MOVES:
            new_state['moves'] = action[6:]
        else:
            problem = 'Unknown frame %s' % action[6:]
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
    elif action in _SCREEN_MOVES:
        if target == 'box':
            axis, sign = _SCREEN_MOVES[action]
            edit = {'kind': 'box', 'vector': _scaled(frame[axis], sign * step)}
        else:
            problem = 'Pick the whole box to move it; a face only moves in and out'
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
    """Feet and inches the way Revit writes them: 0' 6", 1' 0 1/8", the feet
    always there so the eye lands on the same column every time."""
    sign = '-' if total_inches < 0 else ''
    ticks = int(round(abs(total_inches) * denominator))
    feet, rest = divmod(ticks, 12 * denominator)
    inches, num = divmod(rest, denominator)
    text = "%s%d' %d" % (sign, feet, inches)
    if num:
        g = _gcd(num, denominator)
        text += ' %d/%d' % (num // g, denominator // g)
    return text + '"'


def _number(token):
    """A token as a float: a decimal or a fraction like 1/8, else None."""
    if '/' in token:
        top, _, bottom = token.partition('/')
        try:
            top, bottom = float(top), float(bottom)
        except ValueError:
            return None
        return top / bottom if bottom else None
    try:
        return float(token)
    except ValueError:
        return None


def parse_length(text, units):
    """A typed length in document units, or None when it does not read as
    one. Metric documents take a plain number. Imperial documents take the
    forms Revit does: 0.5 (feet, or inches on an inch document), 0 6 (feet
    then inches), 0 0 1/8 (feet, inches, fraction), 1' 6", 6 1/2", 18",
    1-6, and a bare fraction like 1/8 is inches."""
    text = (text or '').strip()
    if not text:
        return None
    per_unit = _INCHES_PER_UNIT.get(units)
    if per_unit is None:
        value = _number(text)
        return value if value is not None else None

    cleaned = (text.lower().replace("'", ' ft ').replace('"', ' in ')
               .replace('ft.', ' ft ').replace('in.', ' in ')
               .replace(',', ' ').replace('-', ' '))
    for word in ('feet', 'foot', 'inches', 'inch'):
        cleaned = cleaned.replace(word, ' ft ' if word.startswith('f') else ' in ')
    tokens = cleaned.split()

    # Group each number, and the fraction that may follow it, with the unit
    # tag that may follow that: [(value, 'ft' | 'in' | None, is_bare_fraction)].
    groups = []
    for token in tokens:
        if token in ('ft', 'in'):
            if not groups or groups[-1][1] is not None:
                return None
            groups[-1][1] = token
            continue
        value = _number(token)
        if value is None:
            return None
        if '/' in token and groups and groups[-1][1] is None and not groups[-1][2]:
            groups[-1][0] += value
        else:
            groups.append([value, None, '/' in token])
    if not groups:
        return None

    default = 'ft' if per_unit == 12.0 else 'in'
    total_inches = 0.0
    untagged = 0
    for value, tag, bare_fraction in groups:
        if tag is None:
            if bare_fraction:
                tag = 'in'
            else:
                tag = default if untagged == 0 else 'in'
                untagged += 1
        total_inches += value * (12.0 if tag == 'ft' else 1.0)
    return total_inches / per_unit


def _gcd(a, b):
    while b:
        a, b = b, a % b
    return a


def _normal_name(normal):
    for name, (x, y, z) in FACES:
        if abs(normal[0] + x) < 1e-6 and abs(normal[1] + y) < 1e-6 and abs(normal[2] + z) < 1e-6:
            return name                       # FACES holds inward normals
    return '(%.2f, %.2f, %.2f)' % (normal[0], normal[1], normal[2])


def target_label(target, snapshot, frame):
    """The target as the panel names it: 'Box', 'Front face (+Y)' in Box
    mode, 'Plane 3, Left (-X)' or 'Plane 3, tilted' in Planes mode."""
    if target == 'box':
        return 'Box'
    if snapshot is None:
        return target_name(target)
    planes = snapshot.get('planes') or []
    if target >= len(planes):
        return target_name(target)
    inward = planes[target].get('normal', (0.0, 0.0, 0.0))
    name = name_for_normal(inward, frame)
    if snapshot.get('mode') == 'box':
        if name is None:
            return '%s face' % FACES[target][0]
        return '%s face (%s)' % (name, axis_label(_scaled(inward, -1.0)))
    return layout(snapshot, frame)['labels'].get(target, target_name(target))


def describe(state, snapshot, units, per_mm=1.0, frame=None):
    """{'target', 'readout', 'step'}: the three lines the panel shows."""
    if frame is None:
        frame = _PLAN_FRAME
    state = normalize(state)
    target = state['target']
    text = {'target': target_label(target, snapshot, frame),
            'step': format_length(state['step_mm'] * per_mm, units),
            'readout': ''}
    if snapshot is None:
        text['readout'] = 'No document'
    elif not snapshot.get('enabled'):
        text['readout'] = 'Sectioning is off'
    elif target == 'box':
        box = snapshot.get('box')
        if not box:
            text['readout'] = 'Six planes; the arrows move them all together'
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
            words = {'left': 'left', 'right': 'right', 'up': 'up', 'down': 'down',
                     'nearer': 'nearer', 'farther': 'farther', 'in': 'down', 'out': 'up'}
            return 'Box %s by %s' % (words.get(action, action), step)
        return '%s %s by %s' % (target_name(edit['index']), action, step)
    if action in ('double', 'halve') or action.startswith('step:'):
        return 'Step: %s' % format_length(state['step_mm'] * per_mm, units)
    if action.startswith('moves:'):
        return 'Box moves along the %s' % ('screen' if state['moves'] == 'screen' else 'world axes')
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


def camera_frame(doc, snap=True):
    """The frame of the document's live camera, snapped to world axes or
    the camera's own."""
    try:
        rotation = doc.CurrentViewpoint.CreateCopy().Rotation
        return frame_for((rotation.A, rotation.B, rotation.C, rotation.D), snap)
    except Exception:
        return frame_for(None)


def per_mm(doc):
    """Document units per millimetre."""
    from pynavis._api import Api
    return Api.UnitConversion.ScaleFactor(Api.Units.Millimeters, doc.Units)


def _point(p):
    return (float(p.X), float(p.Y), float(p.Z))


def read(doc, note=None):
    """A plain snapshot of the current viewpoint's clip planes, or None when
    the document has none to read. note, if given, is told why.

    CreateCopy, not CurrentViewpoint.ClipPlanes: the current-viewpoint handle
    has no ClipPlanes of its own, only the Viewpoint a copy hands back."""
    try:
        planes = doc.CurrentViewpoint.CreateCopy().ClipPlanes
    except Exception as error:
        if note is not None:
            note('Section Nudge: could not read the clip planes: %s' % error)
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
    result = plan(state, action, scale, camera_frame(document, state['moves'] == 'world'))
    if result['edit'] is not None and not result['problem']:
        result['problem'] = apply(result['edit'], document)
    save_state(result['state'])
    result['toast'] = (result['problem'] or
                       toast_for(result['state'], action, result['edit'], units, scale))
    return result
