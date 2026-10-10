"""Values a tool adds to its own telemetry record.

When a firm turns telemetry on, pyNavis writes one record for every tool run: who ran
which button, on which model, how long it took and whether it worked. Only the tool knows
what the run achieved, so note() adds the tool's own values to that record, under
"results". With telemetry off, or outside a run, note() does nothing, so a tool or a
library can call it without checking first.

Needs only the PyNavis.Runtime assembly, never the Navisworks API, and works the same
under IronPython and CPython: the values cross to the runtime as JSON text.
"""

import clr
import json
import math

clr.AddReference('PyNavis.Runtime')

from PyNavis.Runtime.Execution import PyNavisHost

_MAX_DEPTH = 8


def note(**values):
    """Adds values to this run's telemetry record; returns None.

    Each call merges its names into the record's "results", and a name noted again
    replaces the earlier value. Strings, numbers, booleans, None, and lists and dicts of
    them are kept as they are; anything else is written as its str(), and a number JSON
    cannot hold (NaN, infinity) as None. Ignored with telemetry off or outside a run.
    Never raises: telemetry must not break the tool it watches.

        telemetry.note(items=len(items), minutes_saved=10)
    """
    if not values:
        return None
    try:
        text = json.dumps(_plain(values, 0), default=str)
    except Exception:
        return None
    try:
        PyNavisHost.Instance.TelemetryNoteJson(text)
    except Exception:
        pass
    return None


def _plain(value, depth):
    """value as something json.dumps writes as plain JSON."""
    if value is None or isinstance(value, (bool, str)):
        return value
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return value if not (math.isnan(value) or math.isinf(value)) else None
    if depth < _MAX_DEPTH:
        if isinstance(value, dict):
            return dict((str(k), _plain(v, depth + 1)) for k, v in value.items())
        if isinstance(value, (list, tuple, set, frozenset)):
            return [_plain(v, depth + 1) for v in value]
    return str(value)
