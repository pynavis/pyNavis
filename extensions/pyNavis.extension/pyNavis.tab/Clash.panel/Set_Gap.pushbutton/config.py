"""Options for Set Gap: the default gap the prompt is prefilled with."""

import facemove

from pynavis import forms, settings, toast

try:
    from pynavis import app
except ImportError:                            # no Navisworks: assume millimetres
    app = None

values = settings.load(facemove.GAP_TOOL, facemove.GAP_DEFAULTS)

units = values['gap_units']
if app is not None:
    try:
        units = str(app.get_doc().Units)
    except Exception:
        pass

current = facemove.convert(values['gap'], values['gap_units'], units)
gap = forms.ask_number(
    'Default gap to leave between the two faces, in %s:' % units.lower(),
    default=current, min_value=0, title='Set Gap')
if gap is not None:                            # None means cancelled: change nothing
    values['gap'] = float(gap)
    values['gap_units'] = units
    settings.save(facemove.GAP_TOOL, values)
    toast.success('Saved', 'Default gap %s.'
                  % facemove.format_length(values['gap'], units))
