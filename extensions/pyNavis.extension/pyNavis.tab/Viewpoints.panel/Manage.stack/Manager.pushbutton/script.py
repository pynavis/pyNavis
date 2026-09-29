"""Sorts, reorganizes, and cleans up saved viewpoint folders: sort A-Z, move
items between folders, create folders, and purge empty ones, applied live."""
import clr

clr.AddReference('PyNavis.Runtime')
from System import Func
from System.Collections.Generic import List
from PyNavis.Runtime.Forms import ViewpointManagerDialog, ViewpointRow

from pynavis import forms, viewpoints

ManagerAction = ViewpointManagerDialog.ManagerAction
ManagerResult = ViewpointManagerDialog.ManagerResult


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

    current = {'rows': snapshot}

    def key_of_folder(path):
        """The index-path key for a folder path, or '' for the top level."""
        if not path:
            return ''
        for row in current['rows']:
            if row['is_folder']:
                full = (row['folder'] + '/' + row['name']) if row['folder'] else row['name']
                if full == path:
                    return row['key']
        return ''

    def fresh(result, message):
        current['rows'] = viewpoints.snapshot()
        result.Success = True
        result.Message = message
        result.Rows = to_rows(current['rows'])
        return result

    def fail(result, message):
        result.Success = False
        result.Message = message
        return result

    def execute(action):
        result = ManagerResult()
        try:
            guids = list(action.Guids)
            by_guid = dict((row['guid'], row) for row in current['rows'])
            keys = [by_guid[g]['key'] for g in guids if g in by_guid]
            target_key = key_of_folder(action.TargetKey)

            if action.Kind == 'sort':
                moved, errors = viewpoints.apply_sort(current['rows'])
                if errors:
                    current['rows'] = viewpoints.snapshot()
                    return fail(result, errors[0])
                return fresh(result, 'Sorted: %d move(s).' % moved)

            if action.Kind == 'move':
                if not keys:
                    return fail(result, 'Select what to move first.')
                error = viewpoints.validate_move(current['rows'], keys, target_key)
                if error is not None:
                    return fail(result, error)
                moved, errors = viewpoints.apply_moves(
                    current['rows'], keys, target_key)
                if errors:
                    current['rows'] = viewpoints.snapshot()
                    return fail(result, errors[0])
                return fresh(result, 'Moved %d item(s).' % moved)

            if action.Kind == 'create':
                name = (action.Name or '').strip()
                if not name:
                    return fail(result, 'Give the new folder a name first.')
                viewpoints.create_folder(name, target_key)
                return fresh(result, "Created folder '%s'." % name)

            if action.Kind == 'purge':
                empties = [row['key'] for row in current['rows']
                           if row['is_folder'] and not any(
                               not other['is_folder']
                               and other['key'].startswith(row['key'] + '/')
                               for other in current['rows'])]
                if not empties:
                    return fail(result, 'No empty folders.')
                plan = viewpoints.plan_deletes(current['rows'], empties)
                question = 'Delete %d empty folder(s)?' % plan['count']
                if not forms.confirm(question, 'Purge empty folders'):
                    return fail(result, 'Purge cancelled.')
                removed, errors = viewpoints.apply_deletes(plan['guids'])
                if errors:
                    current['rows'] = viewpoints.snapshot()
                    return fail(result, errors[0])
                return fresh(result, 'Purged %d folder(s).' % removed)

            return fail(result, 'Unknown action.')
        except Exception as error:
            # Whatever went wrong, the tree may have moved: re-read before the
            # next action so nothing is planned against a stale snapshot.
            try:
                current['rows'] = viewpoints.snapshot()
            except Exception:
                pass
            return fail(result, str(error))

    ViewpointManagerDialog.Show(
        to_rows(snapshot), Func[ManagerAction, ManagerResult](execute))


run()
