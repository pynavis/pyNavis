"""Output helpers for the pyNavis output window.

format_table/table_html are pure python (unit-tested anywhere); the print_* and
progress helpers talk to the host's current output window and render rich HTML
there. On machines without the WebView2 runtime they degrade to
readable plain text - scripts never need to care.
"""

from pynavis import _markdown


def _host():
    from pynavis import script
    return script.get_host()


def print_html(html):
    """Renders an HTML fragment in the output window."""
    _host().WriteHtml(html)


def print_md(markdown):
    """Renders markdown (headers, **bold**, `code`, - lists, links)."""
    _host().WriteHtml(_markdown.to_html(markdown))


def print_table(rows, headers):
    """Renders a styled HTML table (falls back to text on non-HTML output)."""
    _host().WriteHtml(table_html(rows, headers))


def progress(fraction, label=''):
    """Shows/updates the output progress bar; fraction 0..1 (1.0 completes it)."""
    _host().Progress(float(fraction), label)


def chart_bar(labels, values, title=None):
    """Bar chart card in the output window (SVG, theme-aware)."""
    from pynavis import _charts
    print_html(_charts.bar_svg(list(labels), list(values), title))


def chart_line(labels, series, title=None):
    """Line chart; series is {name: [values]} sharing the labels axis."""
    from pynavis import _charts
    print_html(_charts.line_svg(list(labels), dict(series), title))


def chart_pie(labels, values, title=None):
    """Pie chart card in the output window (SVG, theme-aware)."""
    from pynavis import _charts
    print_html(_charts.pie_svg(list(labels), list(values), title))


def chart_doughnut(labels, values, title=None):
    """Doughnut chart card in the output window (SVG, theme-aware)."""
    from pynavis import _charts
    print_html(_charts.pie_svg(list(labels), list(values), title, doughnut=True))


def element_link(item, label):
    """HTML for a clickable link that selects the ModelItem in the model.
    Use inside print_html: output.print_html('Worst clash: ' + output.element_link(item, item.DisplayName))
    """
    return _host().ElementLink(item, str(label))


def _is_number(text):
    try:
        float(text.replace(',', ''))
        return True
    except (ValueError, AttributeError):
        return False


def table_html(rows, headers):
    """The HTML for print_table (pure - testable without a window).

    Numeric columns are tagged so the stylesheet can right-align and tabulate
    them. A column counts as numeric when every cell in it is a number, which
    is why alignment follows the data instead of the column's position.
    """
    text_rows = [[_cell(row, i) for i in range(len(headers))] for row in rows]
    numeric = [
        bool(text_rows) and all(_is_number(row[i]) for row in text_rows if row[i] != '')
        and any(row[i] != '' for row in text_rows)
        for i in range(len(headers))]

    def cls(i):
        return ' class="num"' if numeric[i] else ''

    head = ''.join('<th%s>%s</th>' % (cls(i), _markdown.escape(str(h)))
                   for i, h in enumerate(headers))
    body = ''.join(
        '<tr>%s</tr>' % ''.join(
            '<td%s>%s</td>' % (cls(i), _markdown.escape(row[i]))
            for i in range(len(headers)))
        for row in text_rows)
    return ('<table class="pynavis"><thead><tr>%s</tr></thead>'
            '<tbody>%s</tbody></table>' % (head, body))


def format_table(rows, headers):
    """Formats rows (any cell type) as an aligned text table with a dashed
    header rule. Column count follows headers; short rows are padded.
    """
    text_rows = [[_cell(row, i) for i in range(len(headers))] for row in rows]
    widths = [
        max([len(str(h))] + [len(r[i]) for r in text_rows])
        for i, h in enumerate(headers)
    ]

    lines = [
        _join([str(h) for h in headers], widths),
        _join(['-' * w for w in widths], widths),
    ]
    for row in text_rows:
        lines.append(_join(row, widths))
    return '\n'.join(lines)


def print_image(path, caption=None):
    """Embeds an image file (png/jpg/gif/svg) in the output window."""
    import base64, os
    ext = os.path.splitext(str(path))[1].lower()
    mime = {'.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg',
            '.gif': 'image/gif', '.svg': 'image/svg+xml'}.get(ext)
    if mime is None:
        raise ValueError('Unsupported image type: %s' % ext)
    with open(str(path), 'rb') as f:
        data = base64.b64encode(f.read()).decode('ascii')
    from pynavis import _markdown
    cap = '<figcaption>%s</figcaption>' % _markdown.escape(caption) if caption else ''
    print_html('<figure class="pynavis-img"><img src="data:%s;base64,%s"/>%s</figure>' % (mime, data, cap))


def print_code(text):
    """Monospace code block (escaped, horizontal scroll)."""
    from pynavis import _markdown
    print_html('<pre class="pynavis-code">%s</pre>' % _markdown.escape(str(text)))


def save(path):
    """Writes everything printed so far as a standalone HTML file."""
    _host().SaveOutput(str(path))


def set_title(text):
    """Retitles the output window."""
    _host().SetOutputTitle(str(text))


def _cell(row, index):
    return str(row[index]) if index < len(row) else ''


def _join(cells, widths):
    return '  '.join(c.ljust(w) for c, w in zip(cells, widths)).rstrip()
