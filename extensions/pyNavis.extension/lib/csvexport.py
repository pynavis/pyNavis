"""CSV files that Excel opens as UTF-8, for the bundles that export one.

open(path, 'w') with no encoding writes the ANSI code page under IronPython
3.4 on .NET Framework, with strict errors, so one non-Latin name raises
UnicodeEncodeError halfway through and leaves a truncated file behind. UTF-8
fixes that, and Excel needs a byte order mark at the top to read the file as
UTF-8 rather than in the ANSI code page.

The obvious encoding='utf-8-sig' is a trap under IronPython: its text writer
encodes every write() from scratch, so a byte order mark lands in front of
every row. The file is opened as plain UTF-8 and the mark written once, by
hand. newline='' leaves line endings to the csv module (CRLF, the CSV
standard, with a line break inside a quoted cell kept as it is).

Pure stdlib, so it is tested outside Navisworks (src/PyNavis.Tests/CsvExportTests.cs).
"""

import csv
import io


def write_rows(path, headers, rows):
    """Writes headers, then every row of rows (any iterable), to path as a
    UTF-8 CSV with one byte order mark. Returns the number of rows written,
    headers not counted."""
    count = 0
    with io.open(path, 'w', encoding='utf-8', newline='') as handle:
        handle.write('﻿')
        writer = csv.writer(handle)
        writer.writerow(headers)
        for row in rows:
            writer.writerow(row)
            count += 1
    return count
