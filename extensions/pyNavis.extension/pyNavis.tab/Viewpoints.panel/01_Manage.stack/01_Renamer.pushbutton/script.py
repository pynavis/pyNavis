"""Batch renames saved viewpoints: find/replace (plain or regex), prefix and
suffix, sequential numbering, or case cleanup, with a live preview."""
import clr

clr.AddReference('PyNavis.Runtime')
from System import Func
from System.Collections.Generic import List
from PyNavis.Runtime.Forms import ViewpointRenamerDialog, ViewpointRow

from pynavis import forms, output, toast, viewpoints

RenameRequest = ViewpointRenamerDialog.RenameRequest
RenamePreview = ViewpointRenamerDialog.RenamePreview
RenameItem = ViewpointRenamerDialog.RenameItem


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


def op_dict(op):
    return {
        'type': op.Type,
        'find': op.Find, 'replace': op.Replace, 'regex': op.Regex,
        'prefix': op.Prefix, 'suffix': op.Suffix,
        'pattern': op.Pattern, 'start': op.Start, 'pad': op.Pad,
        'mode': op.CaseMode,
    }


def run():
    snapshot = viewpoints.snapshot()
    if not snapshot:
        forms.alert('This document has no saved viewpoints.')
        return

    by_guid = dict((row['guid'], row) for row in snapshot)

    def plan_for(request):
        keys = [by_guid[g]['key'] for g in request.CheckedGuids if g in by_guid]
        return viewpoints.plan_renames(snapshot, keys, op_dict(request.Op))

    def preview(request):
        plan = plan_for(request)
        data = RenamePreview()
        for entry in plan['renames']:
            item = RenameItem()
            item.Guid = entry['guid']
            item.OldName = entry['old']
            item.NewName = entry['new']
            item.Collision = bool(entry.get('collision'))
            data.Items.Add(item)
        for problem in plan['problems']:
            data.Problems.Add(problem)
        return data

    request = ViewpointRenamerDialog.Show(
        to_rows(snapshot), Func[RenameRequest, RenamePreview](preview))
    if request is None:
        return

    plan = plan_for(request)
    if plan['collisions'] or plan['problems'] or not plan['renames']:
        toast.info('Nothing to rename.')
        return

    total = len(plan['renames'])

    def tick(done, count):
        output.progress(float(done) / count, 'Renaming %d of %d' % (done, count))

    applied, errors = viewpoints.apply_renames(
        plan['renames'], progress=tick if total > 500 else None)
    output.progress(1.0)
    if errors:
        toast.warning('Renamed %d item(s), %d failed.' % (applied, len(errors)),
                      errors[0])
    else:
        toast.success('Renamed %d item(s).' % applied)


run()
