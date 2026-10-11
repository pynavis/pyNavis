"""Exports every saved viewpoint (name, folder, camera position) to a CSV file."""

import csvexport

from pynavis import doc, forms, toast


def row_for(folders, item):
    # the saved-viewpoints tree also holds animations/cuts, which
    # have no camera - export them with empty position columns
    viewpoint = getattr(item, 'Viewpoint', None)
    position = getattr(viewpoint, 'Position', None)
    return [
        item.DisplayName,
        '/'.join(folders),
        item.GetType().Name,
        '%.6f' % position.X if position is not None else '',
        '%.6f' % position.Y if position is not None else '',
        '%.6f' % position.Z if position is not None else '',
    ]


items = list(doc.walk_saved_viewpoints())
if not items:
    forms.alert('This document has no saved viewpoints.')
else:
    default_name = (doc.get_title() or 'viewpoints') + '_viewpoints.csv'
    path = forms.save_file(default_name=default_name, title='Export viewpoints CSV')
    if path is not None:
        rows = csvexport.write_rows(
            path, ['Name', 'Folder', 'Type', 'CameraX', 'CameraY', 'CameraZ'],
            (row_for(folders, item) for folders, item in items))
        toast.success('Exported %d saved item(s)' % rows, path)
