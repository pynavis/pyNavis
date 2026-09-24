# -*- coding: utf-8 -*-
"""Per-tool user settings for bundle scripts (the Shift+Click convention).

A bundle's config.py stores its defaults here and its script.py reads them
back: one JSON file per tool under %APPDATA%\\pyNavis\\settings\\. Pure
stdlib on purpose - no Navisworks API import - so it works in any engine
and outside Navisworks entirely.
"""
import json
import os

# Test seam: points the store somewhere else instead of %APPDATA%.
_APPDATA_OVERRIDE = None


def _root():
    base = _APPDATA_OVERRIDE or os.environ.get('APPDATA', '')
    return os.path.join(base, 'pyNavis', 'settings')


def path_for(tool):
    """The settings file for a tool key like 'smart_clash_grouper'."""
    return os.path.join(_root(), tool + '.json')


def merge(defaults, stored):
    """Defaults overlaid with stored values; keys outside defaults are dropped,
    so stale entries from old tool versions never leak back in."""
    result = dict(defaults)
    for key in defaults:
        if key in stored:
            result[key] = stored[key]
    return result


def load(tool, defaults):
    """The tool's saved settings merged over defaults; a missing, corrupt, or
    non-object file yields a fresh copy of the defaults."""
    try:
        with open(path_for(tool), 'r') as f:
            stored = json.load(f)
    except Exception:
        return dict(defaults)
    if not isinstance(stored, dict):
        return dict(defaults)
    return merge(defaults, stored)


def save(tool, values):
    root = _root()
    if not os.path.isdir(root):
        os.makedirs(root)
    from pynavis import _atomic
    _atomic.write_text(path_for(tool), json.dumps(values, indent=2, sort_keys=True))
