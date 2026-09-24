"""Smoke fixture: fires when a document opens."""
from pynavis import doc, toast

toast.info('Hook doc-opened: %s' % (doc.get_title() or 'untitled'))
