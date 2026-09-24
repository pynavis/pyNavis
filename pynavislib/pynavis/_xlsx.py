"""pynavis._xlsx - the OpenXML package builder and parser behind pynavis.xl.

Written for IronPython 3.4 and CPython, pure stdlib: no f-strings, ``%``
formatting only. Every fixed OpenXML part lives here as a module-level
string template; ``pynavis.xl`` owns the public read/write API and the
zipfile/file-system plumbing.

Writing uses INLINE strings (``<c t="inlineStr"><is><t>...</t></is></c>``)
rather than a shared-strings table, so a workbook this module writes never
needs ``xl/sharedStrings.xml``. Reading still understands shared strings,
since files written by Excel itself use that table.
"""
import re
from xml.dom import minidom

_CONTROL_CHARS_RE = re.compile(u'[\x00-\x08\x0b\x0c\x0e-\x1f]')

# ---------------------------------------------------------------------------
# Fixed part templates (module-level string constants)
# ---------------------------------------------------------------------------

XML_DECLARATION = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'

CONTENT_TYPES_TEMPLATE = (
    XML_DECLARATION +
    '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
    '<Default Extension="xml" ContentType="application/xml"/>'
    '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
    '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
    '%s'
    '</Types>'
)

CONTENT_TYPE_SHEET_OVERRIDE_TEMPLATE = (
    '<Override PartName="/xl/worksheets/sheet%d.xml" '
    'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
)

PACKAGE_RELS_XML = (
    XML_DECLARATION +
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
    '<Relationship Id="rId1" '
    'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
    'Target="xl/workbook.xml"/>'
    '</Relationships>'
)

# One bold font (id 1) for header rows; id 0 is the default body font.
STYLES_XML = (
    XML_DECLARATION +
    '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
    '<fonts count="2">'
    '<font><sz val="11"/><name val="Calibri"/></font>'
    '<font><b/><sz val="11"/><name val="Calibri"/></font>'
    '</fonts>'
    '<fills count="1"><fill><patternFill patternType="none"/></fill></fills>'
    '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
    '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
    '<cellXfs count="2">'
    '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
    '<xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>'
    '</cellXfs>'
    '</styleSheet>'
)
HEADER_STYLE_INDEX = 1

WORKBOOK_TEMPLATE = (
    XML_DECLARATION +
    '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
    'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
    '<sheets>%s</sheets>'
    '</workbook>'
)
SHEET_ENTRY_TEMPLATE = '<sheet name="%s" sheetId="%d" r:id="rId%d"/>'

WORKBOOK_RELS_TEMPLATE = (
    XML_DECLARATION +
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
    '%s'
    '</Relationships>'
)
SHEET_RELATIONSHIP_TEMPLATE = (
    '<Relationship Id="rId%d" '
    'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
    'Target="worksheets/sheet%d.xml"/>'
)
STYLES_RELATIONSHIP_TEMPLATE = (
    '<Relationship Id="rId%d" '
    'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" '
    'Target="styles.xml"/>'
)

SHEET_TEMPLATE = (
    XML_DECLARATION +
    '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
    '%s'
    '<sheetData>%s</sheetData>'
    '</worksheet>'
)
COLS_TEMPLATE = '<cols>%s</cols>'
COL_TEMPLATE = '<col min="%d" max="%d" width="%d" customWidth="1"/>'
ROW_TEMPLATE = '<row r="%d">%s</row>'


# ---------------------------------------------------------------------------
# Escaping / formatting helpers
# ---------------------------------------------------------------------------

def escape_xml(text):
    """XML-escape & < > " and strip characters XML 1.0 forbids outright."""
    text = _CONTROL_CHARS_RE.sub('', text)
    text = text.replace('&', '&amp;')
    text = text.replace('<', '&lt;')
    text = text.replace('>', '&gt;')
    text = text.replace('"', '&quot;')
    return text


def element_text(element):
    """Concatenated text of an element's direct text-node children."""
    parts = []
    for node in element.childNodes:
        if node.nodeType == node.TEXT_NODE:
            parts.append(node.data)
    return ''.join(parts)


def _display_length(value):
    if value is None:
        return 0
    if isinstance(value, bool):
        return 4 if value else 5
    if isinstance(value, (int, float)):
        return len(str(value))
    return len(value if isinstance(value, str) else str(value))


# ---------------------------------------------------------------------------
# column_letter / build_workbook
# ---------------------------------------------------------------------------

def column_letter(index):
    """0-based column index -> spreadsheet column letters (A, ..., Z, AA, ...)."""
    n = index + 1
    letters = ''
    while n > 0:
        n, remainder = divmod(n - 1, 26)
        letters = chr(65 + remainder) + letters
    return letters


def _column_index(ref):
    """Inverse of column_letter: a cell reference like 'AA12' -> 26 (0-based).

    Returns -1 for anything that does not start with column letters (an
    absolute reference like '$B$1', an empty ref, junk). Callers must treat
    -1 as "unparseable" and skip the cell: using it as a list index would
    write to the LAST column of the row instead.
    """
    letters = ''
    for ch in ref:
        if ch.isalpha():
            letters += ch
        else:
            break
    if not letters:
        return -1
    n = 0
    for ch in letters:
        n = n * 26 + (ord(ch.upper()) - 64)
    return n - 1


def _cols_xml(rows):
    widths = {}
    for row in rows:
        for col_index, value in enumerate(row):
            length = _display_length(value)
            if length > widths.get(col_index, 0):
                widths[col_index] = length
    if not widths:
        return ''
    parts = []
    for col_index in sorted(widths.keys()):
        width = min(60, max(8, widths[col_index] + 2))
        parts.append(COL_TEMPLATE % (col_index + 1, col_index + 1, width))
    return COLS_TEMPLATE % ''.join(parts)


def _cell_xml(ref, value, style_attr):
    if value is None:
        return ''
    if isinstance(value, bool):
        return '<c r="%s"%s t="b"><v>%d</v></c>' % (ref, style_attr, 1 if value else 0)
    if isinstance(value, (int, float)):
        return '<c r="%s"%s><v>%s</v></c>' % (ref, style_attr, str(value))
    text = value if isinstance(value, str) else str(value)
    return '<c r="%s"%s t="inlineStr"><is><t xml:space="preserve">%s</t></is></c>' % (
        ref, style_attr, escape_xml(text))


def _sheet_xml(rows, header_row_count):
    row_parts = []
    for row_index, row in enumerate(rows):
        style_attr = ' s="%d"' % HEADER_STYLE_INDEX if row_index < header_row_count else ''
        cell_parts = []
        for col_index, value in enumerate(row):
            ref = column_letter(col_index) + str(row_index + 1)
            cell_xml = _cell_xml(ref, value, style_attr)
            if cell_xml:
                cell_parts.append(cell_xml)
        row_parts.append(ROW_TEMPLATE % (row_index + 1, ''.join(cell_parts)))
    return SHEET_TEMPLATE % (_cols_xml(rows), ''.join(row_parts))


def build_workbook(sheets, header_rows=None):
    """Ordered [(sheet_name, rows), ...] -> {arcname: xml_string} for the
    whole package.

    Sheets land in the workbook in the order given, so the caller owns
    sheet order outright: this used to sort names alphabetically, which
    silently reshuffled a user's sheets on every rewrite. A plain dict is
    NOT accepted, because dict iteration order is not guaranteed on
    IronPython 3.4 and would make the order accidental again.

    ``header_rows`` is an optional {sheet_name: count} of leading rows to
    render with the bold header style.
    """
    header_rows = header_rows or {}
    pairs = list(sheets)
    names = [pair[0] for pair in pairs]

    parts = {}
    sheet_entries = []
    rel_entries = []
    content_overrides = []
    for position, pair in enumerate(pairs):
        idx = position + 1
        name, rows = pair[0], pair[1]
        sheet_xml = _sheet_xml(rows, header_rows.get(name, 0))
        parts['xl/worksheets/sheet%d.xml' % idx] = sheet_xml
        sheet_entries.append(SHEET_ENTRY_TEMPLATE % (escape_xml(name), idx, idx))
        rel_entries.append(SHEET_RELATIONSHIP_TEMPLATE % (idx, idx))
        content_overrides.append(CONTENT_TYPE_SHEET_OVERRIDE_TEMPLATE % idx)

    styles_rid = len(names) + 1
    rel_entries.append(STYLES_RELATIONSHIP_TEMPLATE % styles_rid)

    parts['[Content_Types].xml'] = CONTENT_TYPES_TEMPLATE % ''.join(content_overrides)
    parts['_rels/.rels'] = PACKAGE_RELS_XML
    parts['xl/workbook.xml'] = WORKBOOK_TEMPLATE % ''.join(sheet_entries)
    parts['xl/_rels/workbook.xml.rels'] = WORKBOOK_RELS_TEMPLATE % ''.join(rel_entries)
    parts['xl/styles.xml'] = STYLES_XML
    return parts


# ---------------------------------------------------------------------------
# Reading
# ---------------------------------------------------------------------------

def _cell_value(cell_element, shared_strings):
    cell_type = cell_element.getAttribute('t')
    if cell_type == 'inlineStr':
        is_elements = cell_element.getElementsByTagName('is')
        if not is_elements:
            return ''
        return ''.join(element_text(t) for t in is_elements[0].getElementsByTagName('t'))

    value_elements = cell_element.getElementsByTagName('v')
    if cell_type == 's':
        if not value_elements:
            return ''
        try:
            index = int(element_text(value_elements[0]))
        except ValueError:
            return ''
        return shared_strings[index] if 0 <= index < len(shared_strings) else ''
    if cell_type == 'b':
        if not value_elements:
            return False
        return element_text(value_elements[0]).strip() == '1'
    if cell_type == 'str':
        if not value_elements:
            return ''
        return element_text(value_elements[0])

    # Default: numeric ("n", or no t attribute at all). Also the fallback
    # for cell types this reader does not special-case (e.g. "e" error
    # cells, "d" ISO-date cells) - degrade to the raw text rather than
    # raising, since a script reading a real-world Excel file should not
    # crash on one exotic cell.
    if not value_elements:
        return None
    text = element_text(value_elements[0]).strip()
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return text


def parse_sheet(sheet_xml, shared_strings):
    """Sheet XML text -> list of rows (values placed by each cell's ``r``
    column letters; a row's length follows its highest referenced column,
    and any gaps normalize to ''). Entirely-blank rows Excel omits from
    the XML (a ``<row r="...">`` gap) come back as ``[]`` so row position
    still lines up with the sheet's actual row numbers.

    Malformed entries are skipped rather than trusted: a cell whose ``r``
    does not start with column letters (an absolute '$B$1' style ref) is
    dropped, and a non-numeric row ``r`` falls back to the next sequential
    row number. Placing such a cell by its unparseable index would
    overwrite a real value elsewhere in the row."""
    dom = minidom.parseString(sheet_xml)
    rows = []
    next_row_number = 1
    for row_element in dom.getElementsByTagName('row'):
        row_ref = row_element.getAttribute('r')
        try:
            row_number = int(row_ref) if row_ref else next_row_number
        except ValueError:
            row_number = next_row_number
        if row_number < next_row_number:
            row_number = next_row_number
        while next_row_number < row_number:
            rows.append([])
            next_row_number += 1

        values = {}
        max_col = -1
        for cell_element in row_element.getElementsByTagName('c'):
            ref = cell_element.getAttribute('r')
            col = _column_index(ref) if ref else (max_col + 1)
            if col < 0:
                continue
            if col > max_col:
                max_col = col
            value = _cell_value(cell_element, shared_strings)
            if value is not None:
                values[col] = value
        row = ['' for _ in range(max_col + 1)]
        for col, value in values.items():
            row[col] = value
        rows.append(row)
        next_row_number = row_number + 1
    return rows
