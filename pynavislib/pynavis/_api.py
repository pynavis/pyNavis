"""Loads the Navisworks .NET API for the other pynavis modules (internal).

Importing this module (directly or via pynavis.app/doc/selection/...) requires a
live Navisworks process: outside one the API assembly cannot resolve and a clear
ImportError is raised instead of a raw CLR loader exception.
"""

import clr

try:
    clr.AddReference('Autodesk.Navisworks.Api')
except Exception:
    raise ImportError(
        'pynavis: could not load the Navisworks .NET API (Autodesk.Navisworks.Api). '
        'This module only works inside a Navisworks session running pyNavis.')

import Autodesk.Navisworks.Api as Api
from Autodesk.Navisworks.Api import Application


def add_reference(name):
    """Loads an additional Navisworks assembly, with the same clear failure mode."""
    try:
        clr.AddReference(name)
    except Exception:
        raise ImportError(
            'pynavis: could not load %s. '
            'This module only works inside a Navisworks session running pyNavis.' % name)
