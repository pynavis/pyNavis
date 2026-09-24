"""Smoke fixture: fires when the saved viewpoints TREE is edited.

Added, renamed, deleted, moved. Not when you look through a saved view: that is
viewpoint-recalled, one letter apart in the plural. Having all three fixtures
present is the point - recalling a view must toast recalled (and moved), while
renaming one must toast only this.
"""
from pynavis import toast

toast.info('Hook viewpoints-changed: the saved viewpoints tree was edited')
