"""Lengths as people type them, for any script that asks for one.

In a feet-and-inches document a length reads the way Revit reads it: 1' 6",
1'-6 1/2", 6 1/2", 3/4", 1.5', the space form 1 6 and the hyphen form 1-6
(feet, then inches), and a bare fraction as inches. In any document a bare
number is in the document's own unit, and a value with its unit (25mm, 0.1m,
6", 1' 6") is converted. Every pyNavis length field uses this, so a length
that works in one tool works in all of them.

A thin face over PyNavis.Runtime.Units.Lengths, which also serves the
runtime's own dialogs. Units are Navisworks unit names ('Feet',
'Millimeters', ...), str(doc.Units). forms.ask_length is the prompt built on
it.
"""

import clr

clr.AddReference('PyNavis.Runtime')

from PyNavis.Runtime.Units import Lengths as _Lengths


def parse(text, units):
    """A typed length in the document's units, or None when it does not read
    as one (negative lengths included)."""
    if text is None:
        return None
    value = _Lengths.Parse(str(text), str(units))
    return None if value is None else float(value)


def format_input(value, units, denominator=16):
    """A length in the document's units as a user would type it: 1' 6 1/2"
    in a feet-and-inches document, a plain decimal otherwise. The pre-fill
    for a length prompt; parse() reads it back."""
    return str(_Lengths.FormatInput(float(value), str(units), int(denominator)))


def convert(value, from_units, to_units):
    """A length in one unit name expressed in another; unknown names are metres."""
    return float(_Lengths.Convert(float(value), str(from_units), str(to_units)))


def is_imperial(units):
    """True for document units that read as feet and inches."""
    return bool(_Lengths.IsImperial(str(units)))


def suffix(units):
    """What an input box shows after the number: nothing in a feet-and-inches
    document, whose text carries its own marks, the short unit name otherwise."""
    return str(_Lengths.Suffix(str(units)))


def hint(units):
    """What may be typed, as the end of a prompt: 'like 3", 1' 6 1/2" or 25mm'
    in a feet-and-inches document, 'in millimeters, or with a unit like 2" or
    0.1m' otherwise."""
    if is_imperial(units):
        return 'like 3", 1\' 6 1/2" or 25mm'
    return 'in %s, or with a unit like 2" or 0.1m' % str(units).lower()
