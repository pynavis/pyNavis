"""Options for True Distance: fraction denominator and parallel tolerance."""

import truedistance

from pynavis import forms, settings, toast

values = settings.load(truedistance.TOOL, truedistance.DEFAULTS)

denominator = forms.ask_number(
    'Round feet-inches readings to this fraction of an inch (1/N):',
    default=values['denominator'], min_value=1, max_value=1024,
    title='True Distance')
if denominator is not None:                    # None means cancelled: change nothing
    degrees = forms.ask_number(
        'Treat two faces as parallel when their normals differ by no more than (degrees):',
        default=values['parallel_degrees'], min_value=0, max_value=45,
        title='True Distance')
    if degrees is not None:
        values['denominator'] = int(round(denominator))
        values['parallel_degrees'] = float(degrees)
        settings.save(truedistance.TOOL, values)
        toast.success('Saved', 'Fractions to 1/%d, parallel within %.1f deg.'
                      % (values['denominator'], values['parallel_degrees']))
