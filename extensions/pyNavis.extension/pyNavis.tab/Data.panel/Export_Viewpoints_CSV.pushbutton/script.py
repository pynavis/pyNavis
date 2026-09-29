"""Exports every saved viewpoint (name, folder, camera position) to a CSV file."""

import csv

from pynavis import doc, forms, toast

items = list(doc.walk_saved_viewpoints())
if not items:
    forms.alert('This document has no saved viewpoints.')
else:
    default_name = (doc.get_title() or 'viewpoints') + '_viewpoints.csv'
    path = forms.save_file(default_name=default_name, title='Export viewpoints CSV')
    if path is not None:
        rows = 0
        with open(path, 'w') as f:
            writer = csv.writer(f, lineterminator='\n')
            writer.writerow(['Name', 'Folder', 'Type', 'CameraX', 'CameraY', 'CameraZ'])
            for folders, item in items:
                # the saved-viewpoints tree also holds animations/cuts, which
                # have no camera - export them with empty position columns
                viewpoint = getattr(item, 'Viewpoint', None)
                position = getattr(viewpoint, 'Position', None)
                writer.writerow([
                    item.DisplayName,
                    '/'.join(folders),
                    item.GetType().Name,
                    '%.6f' % position.X if position is not None else '',
                    '%.6f' % position.Y if position is not None else '',
                    '%.6f' % position.Z if position is not None else '',
                ])
                rows += 1
        toast.success('Exported %d saved item(s)' % rows, path)
