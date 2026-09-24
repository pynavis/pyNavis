"""Options for Resolve Clash: direction and clearance."""

import resolveclash

from pynavis import forms, settings, toast

try:
    from pynavis import app
except ImportError:                            # no Navisworks: assume millimetres
    app = None

values = settings.load(resolveclash.TOOL, resolveclash.DEFAULTS)

units = values['clearance_units']
if app is not None:
    try:
        units = str(app.get_doc().Units)
    except Exception:
        pass

choice = forms.ask_options(
    'Which way may the object move?',
    [label for key, label in resolveclash.DIRECTIONS],
    title='Resolve Clash')
if choice is not None:                         # None means cancelled: change nothing
    direction = [key for key, label in resolveclash.DIRECTIONS if label == choice][0]
    current = resolveclash.convert(values['clearance'], values['clearance_units'], units)
    clearance = forms.ask_number(
        'Clearance to leave between the two objects, in %s:' % units.lower(),
        default=current, min_value=0, title='Resolve Clash')
    if clearance is not None:
        values['direction'] = direction
        values['clearance'] = float(clearance)
        values['clearance_units'] = units
        settings.save(resolveclash.TOOL, values)
        toast.success('Saved', '%s, clearance %s.'
                      % (resolveclash.direction_label(direction),
                         resolveclash.format_length(values['clearance'], units)))
