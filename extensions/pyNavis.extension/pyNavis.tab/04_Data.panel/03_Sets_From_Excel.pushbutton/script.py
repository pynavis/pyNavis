"""Round-trips selection sets through an Excel workbook: export every set to
one row per condition, or import a workbook back into new sets.

The row<->dict converters (rows_to_spec, spec_to_rows) are pure and have no
Navisworks dependency, so this file doubles as an importable module for
tests: everything below the guard at the bottom only runs when pyNavis
actually launches the button (see the '__commandpath__ in globals()' trick
in the bundle-authoring skill's templates)."""

from pynavis import forms, output, sets, toast, xl

HEADERS = ['Folder', 'Set', 'Category', 'Property', 'Op', 'Value']

# Ops whose compiled condition dict (see pynavis.sets._condition_to_dict)
# carries no 'prop' key at all, and ops that carry no 'value' key - has_category
# has neither prop nor value, has_property/not_has_property have prop but no
# value. Mirrored on both sides of the row<->dict conversion below.
_NO_PROP_OPS = ('has_category',)
_NO_VALUE_OPS = ('has_category', 'has_property', 'not_has_property')


def spec_to_rows(spec):
    """The {'sets': [{'name', 'folder', 'query'}, ...]} shape sets.export_all()
    returns, flattened to plain rows for xl.write: one row per condition
    (folder, set name, category, property, op, value). A set with several
    AND'd conditions repeats its folder and name, one row per condition, in
    condition order - the inverse of rows_to_spec below. A set with no
    conditions at all (an empty AND group, or a non-portable {'items': N}
    static set, which only reaches here because run_export asks for
    export_all(include_static=True)) still gets a single row so it stays
    visible in the workbook, with category/property/op/value blank -
    rows_to_spec skips exactly those rows again on the way back in.
    """
    rows = []
    for entry in spec.get('sets', []):
        folder = entry.get('folder') or ''
        name = entry.get('name', '')
        conditions = _conditions_of(entry)
        if not conditions:
            rows.append([folder, name, '', '', '', ''])
            continue
        for condition in conditions:
            rows.append(_row_for_condition(folder, name, condition))
    return rows


def rows_to_spec(rows):
    """The inverse of spec_to_rows: groups xl-read rows (folder, set,
    category, property, op, value) back into the {'sets': [...]} shape
    sets.import_dict() expects. Consecutive rows sharing the same
    (folder, set) belong to one set's single AND group, in row order - a set
    never splits across a non-adjacent block of rows, matching how
    spec_to_rows lays a set's rows out.

    Returns (spec, skipped): a block whose every row has a blank op yields
    no conditions at all, and is DROPPED from spec rather than emitted as an
    empty query, with skipped counting how many blocks were dropped (the
    caller toasts it, so a silently missing set is still visible). Those
    blocks are exactly the static/explicit sets export_all(include_static=
    True) puts in the workbook for visibility: {'items': N} is a count, not
    a query, so there is nothing to rebuild the set from - an empty query
    would mean "match the whole model", which sets._validate_import rejects
    outright, and rejecting would abort the WHOLE import over rows the user
    never meant to import in the first place.

    Excel has no int/float distinction: a real workbook read with
    pynavis.xl.read hands every numeric cell back as float, so an exported
    int like 42 would otherwise re-import as 42.0 (compiling as FromDouble
    instead of FromInt32, and stringifying as '42.0' instead of '42' for
    contains/wildcard). See _normalize_value: an integral float is
    normalized back to int here on the way in; a genuine decimal (1.5) is
    left as float. This is a one-way, lossy convention (a float that was
    genuinely written as e.g. 3.0 on purpose re-imports as int 3) but it is
    the best available guess given xlsx's single numeric cell type, and it
    is what keeps a plain integer property value searching the same way
    after a round trip through Excel.
    """
    entries = []
    prev_key = None
    for row in rows:
        folder, name, category, prop, op, value = _padded(row)
        key = (folder, name)
        if key != prev_key:
            entry = {'name': name, 'folder': folder or None, 'conditions': []}
            entries.append(entry)
            prev_key = key
        else:
            entry = entries[-1]
        if op:
            entry['conditions'].append(_condition_from_row(category, prop, op, value))

    kept = [entry for entry in entries if entry['conditions']]
    spec = {'sets': [
        {'name': entry['name'], 'folder': entry['folder'],
         'query': {'or': [{'and': entry['conditions']}]}}
        for entry in kept
    ]}
    return spec, len(entries) - len(kept)


def _conditions_of(entry):
    query = entry.get('query')
    if not isinstance(query, dict):
        return []
    conditions = []
    for group in query.get('or', []):
        conditions.extend(group.get('and', []))
    return conditions


def _row_for_condition(folder, name, condition):
    op = condition.get('op', '')
    if op == 'raw':
        # to_dict's marker for a condition it could not reverse-map: not
        # portable (sets._validate_import rejects it on import), but the
        # raw text is worth showing rather than a silently blank row.
        return [folder, name, '', '', op, condition.get('text', '')]
    return [
        folder, name,
        condition.get('category', ''),
        '' if op in _NO_PROP_OPS else condition.get('prop', ''),
        op,
        '' if op in _NO_VALUE_OPS else condition.get('value', ''),
    ]


def _condition_from_row(category, prop, op, value):
    if op == 'raw':
        return {'op': 'raw', 'text': value}
    condition = {'category': category, 'op': op}
    if op not in _NO_PROP_OPS:
        condition['prop'] = prop
    if op not in _NO_VALUE_OPS:
        condition['value'] = _normalize_value(value)
    return condition


def _normalize_value(value):
    """An integral float becomes int; anything else passes through
    unchanged. See rows_to_spec's docstring for why: xl.read hands back
    every numeric cell as float, so this is what makes an exported int
    property value (e.g. a level number, a count) behave the same way after
    a round trip through Excel instead of silently becoming a float."""
    if isinstance(value, float):
        try:
            as_int = int(value)
        except (OverflowError, ValueError):
            return value
        if as_int == value:
            return as_int
    return value


def _padded(row):
    """Exactly 6 cells, missing trailing ones treated as ''."""
    values = list(row) + [''] * 6
    return tuple(values[:6])


def run_export():
    # include_static=True: a static/explicit set exports as {'items': N},
    # which is NOT importable - but leaving it out of the workbook entirely
    # would make a set the user can see in Navisworks silently missing from
    # the sheet. It gets a blank row here for visibility, and rows_to_spec
    # skips those blank rows again on the way back in.
    spec = sets.export_all(include_static=True)
    rows = spec_to_rows(spec)
    if not rows:
        toast.info('No sets to export', 'This document has no selection sets.')
        return

    path = forms.save_file(
        filter='Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*',
        default_name='selection_sets.xlsx', title='Export sets to Excel')
    if path is None:                                # cancelled: say nothing
        return

    xl.write(path, rows, headers=HEADERS)
    toast.success('Exported %d row(s)' % len(rows), path)


def run_import():
    path = forms.open_file(
        filter='Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*',
        title='Import sets from Excel')
    if path is None:                                # cancelled: say nothing
        return

    raw_rows = xl.read(path)
    data_rows = raw_rows[1:] if raw_rows and raw_rows[0] == HEADERS else raw_rows
    if not data_rows:
        toast.info('Nothing to import', 'The workbook has no rows.')
        return

    spec, skipped = rows_to_spec(data_rows)
    if not spec['sets']:
        detail = ('%d set(s) had no conditions and were skipped.' % skipped
                  if skipped else 'No rows carried a condition.')
        toast.info('Nothing to import', detail)
        return

    problems = sets._validate_import(spec)
    if problems:
        output.print_table([[p] for p in problems], ['Problem'])
        toast.error('Cannot import', '%d problem(s); see the output window.' % len(problems))
        return

    try:
        with forms.progress('Importing sets', 'Starting') as p:
            def report(done, total):
                p.check()
                p.update(float(done) / total if total else 1.0,
                         '%d of %d' % (done, total))
            created, errors = sets.import_dict(spec, progress=report)
    except forms.Cancelled:                         # cancelled: say nothing
        return

    skipped_note = ('%d set(s) with no conditions skipped' % skipped) if skipped else None
    if errors:
        toast.warning('Created %d set(s), %d failed' % (created, len(errors)), errors[0])
    else:
        toast.success('%d sets created' % created, skipped_note)


def run():
    choice = forms.ask_options(
        'Export the selection sets in this document to an Excel workbook, '
        'or import a workbook back into new sets?', ['Export', 'Import'],
        title='Sets from Excel')
    if choice is None:                              # cancelled: say nothing
        return
    if choice == 'Export':
        run_export()
    else:
        run_import()


if '__commandpath__' in globals():
    run()
