"""Bulk deletes saved viewpoints: search and select, or pick them out by type,
duplicates, or comments, then confirm. The whole delete is one undo step."""
import clr

clr.AddReference('PyNavis.Runtime')
from System.Collections.Generic import List
from PyNavis.Runtime.Forms import ViewpointDeleterDialog, ViewpointRow

from pynavis import forms, output, toast, viewpoints


def to_rows(snapshot):
    rows = List[ViewpointRow]()
    for row in snapshot:
        r = ViewpointRow()
        r.Guid = row['guid']
        r.Key = row['key']
        r.ParentKey = row['parent_key']
        r.Folder = row['folder']
        r.Name = row['name']
        r.Kind = row['kind']
        r.Depth = row['depth']
        r.IsFolder = row['is_folder']
        r.Comments = row['comments']
        rows.Add(r)
    return rows


def run():
    snapshot = viewpoints.snapshot()
    if not snapshot:
        forms.alert('This document has no saved viewpoints.')
        return

    guids = ViewpointDeleterDialog.Show(to_rows(snapshot))
    if guids is None:
        return

    guids = list(guids)
    if not guids:
        toast.info('Nothing to delete.')
        return

    question = ('Delete %d saved viewpoint(s)?\n\n'
                'This removes them from the document.' % len(guids))
    if not forms.confirm(question, 'Delete viewpoints'):
        return

    def tick(done, count):
        output.progress(float(done) / count, 'Deleting %d of %d' % (done, count))

    removed, errors = viewpoints.apply_deletes(
        guids, progress=tick if len(guids) > 500 else None)
    output.progress(1.0)
    if errors:
        toast.warning('Deleted %d item(s), %d failed.' % (removed, len(errors)),
                      errors[0])
    else:
        toast.success('Deleted %d item(s).' % removed)


run()
