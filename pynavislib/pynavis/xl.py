"""pynavis.xl - pure-python xlsx read/write, no external dependencies.

Writes a minimal but valid OpenXML workbook (inline strings, one bold font
for headers, approximate column autofit). Reads both what this module
writes and files Excel itself produces, including its shared-strings
table. Pure stdlib throughout, so it runs unmodified on IronPython 3.4 and
CPython alike.

    from pynavis import xl
    xl.write(path, rows, headers=['Type', 'Count'])
    rows = xl.read(path)              # first sheet
    rows = xl.read(path, sheet='Log') # by name
    names = xl.sheets(path)
"""
import os
import zipfile
from xml.dom import minidom

from . import _xlsx


def write(path, rows, headers=None, sheet='Sheet1'):
    """Write rows (each a list of str/int/float/bool/None) to path as an
    .xlsx workbook. headers, if given, is written as a bold first row.

    Writing to a path that already holds a workbook adds the named sheet
    at the end, or replaces it in place if a sheet by that name already
    exists; the existing sheet order is preserved. This is implemented by
    reading the whole existing workbook back and rebuilding the package
    from scratch, which is simple and correct at the sizes pynavis
    scripts deal with, but it is a VALUES-ONLY round trip: every other
    sheet keeps its name, position and cell values, and loses its
    formatting. Concretely, a sheet written earlier with headers= comes
    back without its bold header row, and numbers that were written as
    int come back as float. Pass headers= on every write to a sheet whose
    header row must stay bold.

    The new package is built in a sibling temp file and moved over path
    only once it is complete, so a crash mid-write leaves the previous
    workbook intact rather than a truncated one.
    """
    if headers is not None:
        combined = [list(headers)] + [list(row) for row in rows]
        header_row_count = 1
    else:
        combined = [list(row) for row in rows]
        header_row_count = 0

    ordered = []
    if os.path.exists(path):
        for name in sheets(path):
            ordered.append((name, read(path, sheet=name)))

    for position, pair in enumerate(ordered):
        if pair[0] == sheet:
            ordered[position] = (sheet, combined)
            break
    else:
        ordered.append((sheet, combined))

    header_rows = {}
    if header_row_count:
        header_rows[sheet] = header_row_count

    parts = _xlsx.build_workbook(ordered, header_rows)

    folder = os.path.dirname(path)
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)

    # Write-then-rename: zipfile 'w' truncates on open, so writing straight
    # to path would destroy the workbook we just read back if anything
    # failed part way through.
    temp_path = path + '.tmp'
    try:
        with zipfile.ZipFile(temp_path, 'w', zipfile.ZIP_DEFLATED) as archive:
            for arcname, content in parts.items():
                archive.writestr(arcname, content)
        os.replace(temp_path, path)
    except BaseException:
        try:
            if os.path.exists(temp_path):
                os.remove(temp_path)
        except OSError:
            pass
        raise


def sheets(path):
    """Sheet names in the workbook, in workbook order."""
    with zipfile.ZipFile(path, 'r') as archive:
        workbook_xml = archive.read('xl/workbook.xml').decode('utf-8')
    dom = minidom.parseString(workbook_xml)
    return [element.getAttribute('name') for element in dom.getElementsByTagName('sheet')]


def read(path, sheet=None):
    """Rows from the named sheet, or the first sheet if sheet is None.

    Numbers come back as float, booleans as True/False, everything else
    (including inline and shared strings) as str. A row's length follows
    the highest column it uses; gaps within that span normalize to ''.
    """
    with zipfile.ZipFile(path, 'r') as archive:
        workbook_dom = minidom.parseString(archive.read('xl/workbook.xml').decode('utf-8'))
        sheet_elements = workbook_dom.getElementsByTagName('sheet')
        if not sheet_elements:
            raise ValueError('workbook has no sheets: %r' % (path,))

        if sheet is None:
            target = sheet_elements[0]
        else:
            target = None
            for element in sheet_elements:
                if element.getAttribute('name') == sheet:
                    target = element
                    break
            if target is None:
                raise ValueError('no such sheet %r in %r' % (sheet, path))

        rid = target.getAttribute('r:id')
        rels_dom = minidom.parseString(archive.read('xl/_rels/workbook.xml.rels').decode('utf-8'))
        arc_target = None
        for relationship in rels_dom.getElementsByTagName('Relationship'):
            if relationship.getAttribute('Id') == rid:
                arc_target = relationship.getAttribute('Target')
                break
        if arc_target is None:
            raise ValueError('no relationship for sheet rId %r in %r' % (rid, path))
        arcname = arc_target.lstrip('/') if arc_target.startswith('/') else 'xl/' + arc_target

        sheet_xml = archive.read(arcname).decode('utf-8')

        shared_strings = []
        if 'xl/sharedStrings.xml' in archive.namelist():
            shared_strings = _read_shared_strings(archive.read('xl/sharedStrings.xml').decode('utf-8'))

    return _xlsx.parse_sheet(sheet_xml, shared_strings)


def _read_shared_strings(shared_strings_xml):
    dom = minidom.parseString(shared_strings_xml)
    result = []
    for si in dom.getElementsByTagName('si'):
        result.append(''.join(_xlsx.element_text(t) for t in si.getElementsByTagName('t')))
    return result
