"""Options for Face Distance: fraction denominator and parallel tolerance."""

import facedistance

from pynavis import forms, settings, toast

values = settings.load(facedistance.TOOL, facedistance.DEFAULTS)

denominator = forms.ask_number(
    'Round feet-inches readings to this fraction of an inch (1/N):',
    default=values['denominator'], min_value=1, max_value=1024,
    title='Face Distance')
if denominator is not None:                    # None means cancelled: change nothing
    degrees = forms.ask_number(
        'Treat two faces as parallel when their normals differ by no more than (degrees):',
        default=values['parallel_degrees'], min_value=0, max_value=45,
        title='Face Distance')
    if degrees is not None:
        values['denominator'] = int(round(denominator))
        values['parallel_degrees'] = float(degrees)
        settings.save(facedistance.TOOL, values)
        toast.success('Saved', 'Fractions to 1/%d, parallel within %.1f deg.'
                      % (values['denominator'], values['parallel_degrees']))
