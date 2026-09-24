"""Clears the marker Go to Coordinates left in the view."""

import coords

from pynavis import overlay, toast

overlay.clear(coords.MARKER_TAG)
overlay.redraw()
toast.info('Marker cleared')
