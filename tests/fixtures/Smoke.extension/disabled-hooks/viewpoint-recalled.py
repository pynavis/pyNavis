"""Smoke fixture: fires when a saved viewpoint is activated.

Not debounced: one toast per recall, however fast you click through the
Saved Viewpoints tree. The name comes from __event__ rather than being read
back off the document, so it is the view that was actually recalled even if
you have moved on since.

Do not confuse this with viewpoints-changed, which fires when the saved
viewpoints TREE is edited (added, renamed, deleted).
"""
from pynavis import script, toast

name = __event__['viewpoint'] or '(unnamed)'

# Logged as well as toasted: recalling a view also moves the camera, so this hook
# and camera-moved toast within milliseconds of each other and the two overlap on
# screen. The log is the reliable way to count how many times each actually ran.
script.get_logger().info('Hook viewpoint-recalled: %s' % name)
toast.info('Hook viewpoint-recalled: %s' % name)
