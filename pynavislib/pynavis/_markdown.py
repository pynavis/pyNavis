"""Minimal markdown -> HTML for the output window (pure python, unit-tested).

Covers what tool scripts actually write: #/##/### headers, **bold**, *italic*,
`code`, ``` blocks, - lists, [text](url) links, paragraphs. Everything is
HTML-escaped first, so raw markup in the input never becomes live HTML.
"""

import re


def escape(text):
    """HTML-escape & < > and ", so the text is safe in an element or a double-quoted attribute."""
    return (text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
            .replace('"', '&quot;'))


# A code span's stand-in while the emphasis and link rules run. The text reaching
# _inline is already escaped, so it holds no '<' and no stand-in can be real input.
_CODE_SLOT = re.compile(r'<(\d+)>')


def _inline(text):
    """Inline markup on already-escaped text. Code spans come out first and go back
    last, so the asterisks in `a*b*c` stay asterisks."""
    spans = []

    def hold(match):
        spans.append(match.group(1))
        return '<%d>' % (len(spans) - 1)

    text = re.sub(r'`(.+?)`', hold, text)
    text = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', text)
    text = re.sub(r'\*(.+?)\*', r'<em>\1</em>', text)
    text = re.sub(r'\[(.+?)\]\((.+?)\)', r'<a href="\2">\1</a>', text)
    return _CODE_SLOT.sub(lambda match: '<code>%s</code>' % spans[int(match.group(1))], text)


def to_html(markdown):
    lines = (markdown or '').split('\n')
    html = []
    in_list = False
    in_code = False
    code_lines = []

    def close_list():
        if html and in_list:
            html.append('</ul>')

    for raw in lines:
        if raw.strip().startswith('```'):
            if in_code:
                html.append('<pre class="codeblock">%s</pre>' % '\n'.join(code_lines))
                code_lines = []
                in_code = False
            else:
                if in_list:
                    html.append('</ul>')
                    in_list = False
                in_code = True
            continue
        if in_code:
            code_lines.append(escape(raw))
            continue

        line = escape(raw.rstrip())
        stripped = line.strip()

        if stripped.startswith('- '):
            if not in_list:
                html.append('<ul>')
                in_list = True
            html.append('<li>%s</li>' % _inline(stripped[2:]))
            continue
        if in_list:
            html.append('</ul>')
            in_list = False

        if stripped.startswith('### '):
            html.append('<h3>%s</h3>' % _inline(stripped[4:]))
        elif stripped.startswith('## '):
            html.append('<h2>%s</h2>' % _inline(stripped[3:]))
        elif stripped.startswith('# '):
            html.append('<h1>%s</h1>' % _inline(stripped[2:]))
        elif stripped:
            html.append('<p>%s</p>' % _inline(stripped))

    if in_list:
        html.append('</ul>')
    if in_code and code_lines:
        html.append('<pre class="codeblock">%s</pre>' % '\n'.join(code_lines))
    return '\n'.join(html)
