"""Options for Resolve Clash: the clearance to leave between the two faces."""

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

current = resolveclash.convert(values['clearance'], values['clearance_units'], units)
clearance = forms.ask_number(
    'Clearance to leave between the two faces, in %s:' % units.lower(),
    default=current, min_value=0, title='Resolve Clash')
if clearance is not None:                      # None means cancelled: change nothing
    values['clearance'] = float(clearance)
    values['clearance_units'] = units
    settings.save(resolveclash.TOOL, values)
    toast.success('Saved', 'Clearance %s.'
                  % resolveclash.format_length(values['clearance'], units))
