"""Rescan extension folders and rebuild the pyNavis ribbon (no restart needed)."""
from pynavis import toast

__pynavis__.Reload()
toast.success('pyNavis ribbon reloaded.')
