"""Smoke fixture: fires when the view moves under navigation.

Debounced 250ms, and that is the whole point: the underlying event fires
continuously while you orbit or walk, so one long drag must produce ONE toast
once you stop, not a stream of them. The timestamp is there to prove it - three
toasts sharing a second came from one gesture and the debounce is broken, three
seconds apart means three gestures and it is working.

Reports the camera position, which also shows the hook sees the resting view.
Read it with CreateCopy(), the same way pynavis.viewstate does: CurrentViewpoint
is a DocumentCurrentViewpoint wrapper, and its .Value does not track the live
camera - it reports the same coordinates however far you orbit.
"""
import datetime

from pynavis import doc, toast

view = doc.get_doc().CurrentViewpoint.CreateCopy()
position, rotation = view.Position, view.Rotation
toast.info('Hook camera-moved at %s' % datetime.datetime.now().strftime('%H:%M:%S.%f')[:-3],
           'pos %.1f, %.1f, %.1f | rot %.3f, %.3f, %.3f, %.3f' % (
               position.X, position.Y, position.Z,
               rotation.A, rotation.B, rotation.C, rotation.D))
