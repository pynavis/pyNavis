"""Flips its own on/off state; the ribbon icon should swap between the
gray (off) and blue (on) circle instantly."""
from pynavis import script, toast

on = not script.get_toggle_state()
script.set_toggle_state(on)
toast.success('Toggle is now ON' if on else 'Toggle is now OFF')
