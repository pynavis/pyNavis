# -*- coding: utf-8 -*-
"""Writes the stored speeds and field of view into saved viewpoints you pick.

Reset Speeds beside this does the view on screen; this does views already saved
in the document, without loading any of them: each saved item is copied, its
camera edited, and the copy swapped back into place. The whole run lands as a
single undo step and can be stopped part way, keeping whatever was written.

The picking is the shared viewpoint browser the Deleter and Manager use. The
writing is lib/vpspeeds.py, which also records the two routes that look like
they should work and do not.
"""

import clr

import speeds
import vpspeeds

clr.AddReference('PyNavis.Runtime')
from System.Collections.Generic import List
from PyNavis.Runtime.Forms import ViewpointResetDialog, ViewpointRow

from pynavis import forms, script, settings, toast, viewpoints


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
    log = script.get_logger()
    values = speeds.normalise(settings.load(speeds.SETTINGS_KEY, speeds.DEFAULTS))
    wanted = speeds.summary(values)
    if not wanted:
        toast.info('Nothing to reset',
                   'All three are switched off in the settings (Shift+Click)')
        return

    snapshot = viewpoints.snapshot()
    if not snapshot:
        forms.alert('This document has no saved viewpoints.')
        return

    guids = ViewpointResetDialog.Show(to_rows(snapshot), wanted)
    if guids is None:
        return                                  # cancelled: say nothing

    guids = list(guids)
    if not guids:
        toast.info('Nothing to reset', 'No viewpoints were ticked')
        return

    # A cancellable window rather than the output bar: this can run over hundreds
    # of viewpoints, and a run with no way out is worse than a slow one. update()
    # returns False once Cancel is clicked, which is the whole cancel signal; it
    # also pumps, so the button can be clicked at all while this holds the UI
    # thread.
    running = [True]
    with forms.progress('Reset viewpoints', 'Starting') as bar:
        def tick(done, count):
            running[0] = bar.update(float(done) / count,
                                    'Resetting %d of %d' % (done, count))

        written, unchanged, errors, route, stopped = vpspeeds.apply(
            snapshot, guids, values,
            progress=tick, cancelled=lambda: not running[0])

    skipped = vpspeeds.dropped_kinds(snapshot, guids)
    notes = []
    if skipped['folder']:
        notes.append('%d folder(s) have no camera' % skipped['folder'])
    if skipped['animation']:
        notes.append('%d animation(s) keep theirs on their cuts' % skipped['animation'])
    if unchanged:
        notes.append('%d already matched' % unchanged)
    if route == vpspeeds.REBUILT:
        notes.append('redlines and appearance overrides were not carried over')

    log.info('reset %d viewpoint(s), %s: %s' % (written, route, wanted))

    if errors:
        toast.warning('Reset %d viewpoint(s), %d failed' % (written, len(errors)),
                      errors[0])
    elif stopped:
        toast.warning('Stopped after %d viewpoint(s)' % written,
                      'What was written is kept, and one undo takes it all back')
    elif written:
        detail = wanted if not notes else '%s. %s.' % (wanted, ', '.join(notes))
        toast.success('Reset %d viewpoint(s)' % written, detail)
    else:
        toast.info('Nothing to change',
                   '. '.join(notes) or 'Those viewpoints already match your settings')


if '__commandpath__' in globals():
    run()
