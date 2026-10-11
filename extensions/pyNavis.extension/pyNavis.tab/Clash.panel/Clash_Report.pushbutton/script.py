"""Summarizes every Clash Detective test (result counts by status), with CSV export."""

import csvexport

from pynavis import clash, forms, output

STATUSES = ['New', 'Active', 'Reviewed', 'Approved', 'Resolved']

summaries = clash.summarize()
if not summaries:
    forms.alert('This document has no clash tests.')
else:
    headers = ['Test', 'Total'] + STATUSES
    rows = [
        [s['name'], s['total']] + [s['by_status'].get(status, 0) for status in STATUSES]
        for s in summaries
    ]
    print(output.format_table(rows, headers))
    total = sum(s['total'] for s in summaries)
    print('\n%d test(s), %d result(s) in total.' % (len(summaries), total))

    if forms.confirm('Export this clash summary to CSV?'):
        path = forms.save_file(default_name='clash_report.csv', title='Export clash report')
        if path is not None:
            csvexport.write_rows(path, headers, rows)
            print('Exported to %s' % path)
