"""Copies the coordinates of a point the user clicks to the clipboard.

The click runs on the native Measure tool (pynavis.pick.measure_point), so the
snapping to vertices, edges and line ends is the host's own and the point that
reaches the clipboard is the snapped one rather than wherever the cursor
happened to be. It is written in the shape Go to Coordinates reads back:
"X, Y, Z" at three decimals, in model units.

Esc, a right-click or picking another tool cancels and copies nothing, with no
complaint: a cancelled pick is an answer, not a failure. See lib/coords.py
for the formatting.
"""

import coords

from pynavis import app, pick, script, toast


def run():
    doc = app.get_doc()
    log = script.get_logger()

    hit = pick.measure_point('Click a point to copy its coordinates')
    if hit is None:
        return

    units = str(doc.Units)
    text = coords.format_point(hit.point)
    script.clipboard_copy(text)
    log.info('copied %s (%s)' % (text, units))
    toast.success('Coordinates copied', '%s %s' % (text, units))


if '__commandpath__' in globals():
    run()
