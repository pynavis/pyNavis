"""Smoke fixture: fires once at boot and again after every Reload."""
from pynavis import toast

toast.info('Hook app-init ran')
