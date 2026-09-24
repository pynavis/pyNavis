"""Groups clash results into reviewable issues: one click Smart grouping by
root-cause element, or chain custom rules (level, grid, proximity, model,
status), with a live preview before anything is written."""

import clr

from pynavis import clashgroup, forms, output, settings
from pynavis._markdown import escape as _escape

try:
    from pynavis import clash
except ImportError:
    clash = None

clr.AddReference('PyNavis.Runtime')

# Int32 explicitly, never the builtin int: IronPython 3 maps int to
# BigInteger, so Func[int, TestCounts] builds a delegate type the CLR does not
# recognise ("expected Func[Int32, TestCounts], got Func[int, TestCounts]").
from System import Func, Int32
from System.Collections.Generic import List
from PyNavis.Runtime.Forms import ClashGrouperDialog, ProgressScope

TestRow = ClashGrouperDialog.TestRow
RuleChoice = ClashGrouperDialog.RuleChoice
GrouperConfig = ClashGrouperDialog.GrouperConfig
PreviewData = ClashGrouperDialog.PreviewData
PreviewGroup = ClashGrouperDialog.PreviewGroup
TestCounts = ClashGrouperDialog.TestCounts
PreviewProgress = ClashGrouperDialog.PreviewProgress

MAX_SUMMARY_ROWS = 20

# Shift+Click (config.py) saves these; a plain run opens the dialog preset.
SETTINGS_DEFAULTS = {'smart': True, 'rules': [], 'tolerance_m': 2.0,
                     'keep_existing': True}


def saved_defaults():
    prefs = settings.load('smart_clash_grouper', SETTINGS_DEFAULTS)
    defaults = GrouperConfig()
    defaults.Smart = bool(prefs['smart'])
    defaults.KeepExisting = bool(prefs['keep_existing'])
    try:
        tolerance = float(prefs['tolerance_m'])
        if tolerance > 0:
            defaults.ToleranceMeters = tolerance
    except (TypeError, ValueError):
        pass
    known = set(rule_id for rule_id, _ in clashgroup.RULES)
    for rule_id in prefs['rules']:
        if rule_id in known:
            defaults.RuleIds.Add(rule_id)
    return defaults


def build_test_rows(tests):
    """Rows with no counts yet: counting walks every result, and the dialog
    fills the numbers in after it is on screen."""
    rows = List[TestRow]()
    for i, test in enumerate(tests):
        row = TestRow()
        row.Index = i
        row.Name = test.DisplayName
        row.Total = -1
        row.Grouped = -1
        row.Checked = False
        rows.Add(row)
    return rows


def build_rule_choices():
    grid_hint = '' if clash.has_grid() else ' (model has no grids)'
    rows = List[RuleChoice]()
    for rule_id, label in clashgroup.RULES:
        choice = RuleChoice()
        choice.Id = rule_id
        if rule_id in ('level', 'grid'):
            label += grid_hint
        choice.Label = label
        rows.Add(choice)
    return rows


def compute_plan(snapshot, config):
    tolerance = config.ToleranceMeters / clash.units_to_meters()
    if config.Smart:
        return clashgroup.smart_plan(snapshot, keep_existing=config.KeepExisting,
                                     tolerance=tolerance)
    return clashgroup.plan(snapshot, list(config.RuleIds),
                           keep_existing=config.KeepExisting, tolerance=tolerance)


def run():
    tests = list(clash.walk_tests())
    if not tests:
        forms.alert('This document has no clash tests.\n\n'
                    'Run a test in Clash Detective first, then group its results here.')
        return

    snapshots = {}
    totals = {}     # test index -> (total, grouped), memo for counts()
    plans = {}      # (signature, test index) -> plan, so Apply never replans

    def counts(index):
        """The dialog's lazy per-test count. Also the preview's denominator.

        Memoised, because two callers ask: the dialog's lazy counting pass and
        preview()'s pre-pass for tests it has not seen yet. Counting walks
        every leaf result, so a test previewed first and then reached by the
        resumed counting pass would otherwise be walked twice - up to 67,000
        results on the UI thread, with no progress bar and no cancel.
        """
        if index in totals:
            total, grouped = totals[index]
        else:
            total, grouped = clash.count_results(tests[index])
            totals[index] = (total, grouped)
        row = TestCounts()
        row.Total = total
        row.Grouped = grouped
        return row

    def signature_of(config):
        return (bool(config.Smart), tuple(config.RuleIds),
                round(config.ToleranceMeters, 6), bool(config.KeepExisting))

    def preview(config, report):
        indexes = list(config.TestIndexes)
        for index in indexes:
            if index not in totals:
                # Paint the progress panel and give the user a cancel
                # checkpoint before each uncounted test's enumeration, rather
                # than walking every uncounted test before the first report:
                # that pre-pass used to freeze the dialog with the old panel
                # still on screen. count_results has no progress callback of
                # its own, so this is a checkpoint between tests, not
                # per-result cancellation the way the read below gets.
                #
                # == False, not "not x": one convention for a progress
                # callback's answer across this file and clash.py (see
                # clash.Cancelled) - only an explicit False means stop, so a
                # callback that answers None carries on.
                if report(0.0, 'Counting clashes') == False:
                    return None
                counts(index)
        grand = sum(totals.get(index, (0, 0))[0] for index in indexes) or 1
        signature = signature_of(config)
        done = [0]

        def tick(read_here):
            # False from the dialog means the user hit Cancel; snapshot_results
            # turns that into Cancelled and unwinds.
            return report(float(done[0] + read_here) / grand, 'Reading clashes')

        rows = []
        total = grouped = skipped = 0
        explanation = ''
        multi = len(indexes) > 1
        try:
            for index in indexes:
                if index not in snapshots:
                    # Assigned only on success: a cancelled read is partial and
                    # must never be cached.
                    snapshots[index] = clash.snapshot_results(tests[index],
                                                              progress=tick)
                done[0] += totals.get(index, (0, 0))[0]
            if report(1.0, 'Grouping') == False:
                return None
            # Only this run's plans can ever be reached: Apply is gated on the
            # dialog's Done state, which requires the config's signature to
            # match the signature of the most recent successful preview.
            # Dropping earlier entries keeps a model with many result ids from
            # holding several complete, unreachable plan copies at once - the
            # bound still holds, because this clears immediately before the one
            # loop that repopulates it. It waits until the read is done so a
            # cancelled re-preview of unchanged settings leaves the plans it
            # was about to rewrite intact, instead of pushing Apply onto
            # plan_for's recompute path.
            plans.clear()
            for index in indexes:
                snapshot = snapshots[index]
                plan = compute_plan(snapshot, config)
                plans[(signature, index)] = plan
                prefix = (tests[index].DisplayName + ': ') if multi else ''
                for group in plan['groups']:
                    rows.append((prefix + group['name'], len(group['ids'])))
                    grouped += len(group['ids'])
                total += len(snapshot)
                skipped += plan['skipped_existing']
                explanation = explanation or plan['explanation']
        except clash.Cancelled:
            return None

        data = PreviewData()
        data.TotalClashes = total
        data.TotalGroups = len(rows)
        # Single test: the engine's own explanation reads best. Several tests:
        # per-test counts would contradict the totals, so aggregate instead.
        parts = [] if multi else [explanation]
        loose = total - grouped - skipped
        if loose > 0:
            parts.append('%d stay individual.' % loose)
        if skipped:
            parts.append('%d stay in their existing groups.' % skipped)
        data.Note = ' '.join(parts)
        for name, count in sorted(rows, key=lambda pair: -pair[1]):
            row = PreviewGroup()
            row.Name = name
            row.Count = count
            data.Groups.Add(row)
        return data

    def plan_for(index, config):
        """The plan the user previewed, or a fresh one if it went missing."""
        cached = plans.get((signature_of(config), index))
        if cached is not None:
            return cached
        # Unreachable today: Apply is only enabled in the dialog's Done state,
        # which means the most recent preview succeeded with exactly this
        # config, so it left a plan here for every selected test. Kept as a
        # correctness backstop, not a routine path - note it calls
        # snapshot_results with progress=None, so if the C# staleness rules
        # ever loosen this becomes a silent, uncancellable full read of the
        # test with the dialog already gone. Anything that can reach it should
        # thread a progress callback through first.
        if index not in snapshots:
            snapshots[index] = clash.snapshot_results(tests[index])
        return compute_plan(snapshots[index], config)

    config = ClashGrouperDialog.Show(
        build_test_rows(tests), build_rule_choices(),
        Func[GrouperConfig, PreviewProgress, PreviewData](preview),
        Func[Int32, TestCounts](counts), saved_defaults())
    if config is None:
        return
    if config.Action == 'apply':
        apply_grouping(tests, plan_for, config)
    elif config.Action == 'ungroup':
        run_ungroup(tests, config)


def apply_grouping(tests, plan_for, config):
    """Writes the plans, then reports. The write runs behind a ProgressScope:
    it holds the UI thread, so without one Navisworks paints a white
    "Not Responding" frame for the duration and the user cannot tell a working
    Apply from the hang this whole feature used to be."""
    indexes = list(config.TestIndexes)
    scope = ProgressScope.Begin('Grouping clashes', 'Starting')
    try:
        written = _write_plans(tests, plan_for, config, indexes, scope)
    finally:
        # Never leave this to the runtime's backstop: a live scope means
        # Navisworks stays unclickable.
        scope.Dispose()
    _report_plans(written)


def _write_plans(tests, plan_for, config, indexes, scope):
    """Commits every selected test, returning [(name, plan, links)] to report.

    Nothing prints here. The output window cannot paint while this holds the UI
    thread, and materialising it mid-write would leave an empty window sitting
    behind the progress one.
    """
    written = []
    for n, index in enumerate(indexes):
        test = tests[index]
        plan = plan_for(index, config)
        # Read everything the report needs BEFORE committing: apply_plan swaps
        # the test for a restructured copy, which leaves this wrapper and all of
        # its results disposed. ModelItems live in the model, so the links
        # captured here stay valid afterwards.
        name = test.DisplayName

        # Only the first result of each reported group is wanted, so they are
        # picked out of the walk instead of materialising it: a list of every
        # result means holding 350,000 live wrappers for the sake of 20 links.
        wanted = {}
        for group in plan['groups'][:MAX_SUMMARY_ROWS]:
            wanted[group['ids'][0]] = None
        remaining = len(wanted)
        if remaining:
            for rid, result in enumerate(clash.walk_results(test)):
                if rid in wanted and wanted[rid] is None:
                    wanted[rid] = result.Item1
                    remaining -= 1
                    if remaining == 0:
                        break
        links = [(group['name'], len(group['ids']), wanted[group['ids'][0]])
                 for group in plan['groups'][:MAX_SUMMARY_ROWS]]

        def tick(fraction):
            scope.Report((n + fraction) / len(indexes), 'Grouping %s' % name)

        clash.apply_plan(test, plan, keep_existing=config.KeepExisting,
                         progress=tick)
        written.append((name, plan, links))
    scope.Report(1.0, 'Done')
    return written


def _report_plans(written):
    for name, plan, links in written:
        output.print_md('## %s' % name)
        print(plan['explanation'])
        if links:
            # Hand-built table (table_html would escape the link markup):
            # plain names for reading, one small Show link per row.
            cells = ''.join(
                '<tr><td>%s</td><td>%d</td><td>%s</td></tr>'
                % (_escape(name), count,
                   output.element_link(item, 'Show') if item is not None else '')
                for name, count, item in links)
            output.print_html(
                '<table class="pynavis"><tr><th>Group</th><th>Clashes</th>'
                '<th></th></tr>%s</table>' % cells)
        hidden = len(plan['groups']) - len(links)
        if hidden > 0:
            print('... and %d more group(s).' % hidden)
        if plan['skipped_existing']:
            print('%d clash(es) kept in their existing groups.'
                  % plan['skipped_existing'])
        if plan['ungrouped_ids']:
            print('%d clash(es) left individual.' % len(plan['ungrouped_ids']))
    # No output.progress here: progress belongs to the ProgressScope now, and a
    # bar that appears already finished only to fade out is noise.
    print('\nDone. Groups are ready in Clash Detective.')


def run_ungroup(tests, config):
    indexes = list(config.TestIndexes)
    scope = ProgressScope.Begin('Removing groups', 'Starting')
    removed_by_test = []
    try:
        for n, index in enumerate(indexes):
            # Read the name first: a commit leaves the old test wrapper disposed.
            name = tests[index].DisplayName

            def tick(fraction):
                scope.Report((n + fraction) / len(indexes),
                             'Ungrouping %s' % name)

            removed_by_test.append(
                (name, clash.ungroup(tests[index], progress=tick)))
        scope.Report(1.0, 'Done')
    finally:
        scope.Dispose()

    removed_total = 0
    for name, removed in removed_by_test:
        removed_total += removed
        if removed:
            print('%s: removed %d group(s).' % (name, removed))
    if not removed_total:
        print('No groups created by this tool were found in the selected tests. '
              'Hand-made groups are never touched.')


if clash is None:
    forms.alert('Clash Detective is not available in this Navisworks edition.\n'
                'Smart Clash Grouper needs Navisworks Manage.')
else:
    run()
