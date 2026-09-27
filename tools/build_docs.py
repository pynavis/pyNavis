"""Builds the pyNavis authoring documentation site.

Reads content fragments from docs/authoring/_content/*.html and emits standalone
pages into docs/authoring/, plus the client-side search index. The fragments hold
only the prose; this script supplies the shell (sidebar, per-page contents rail,
previous/next links), turns bare <pre> blocks into copyable code cards with syntax
highlighting, and gives every heading a stable id and anchor link.

    python tools/build_docs.py            regenerate the site
    python tools/build_docs.py --check    fail if the output is out of date

Dev-time CPython only, standard library only - this never ships into Navisworks.
Never hand-edit a page in docs/authoring/: change the fragment or this script and
re-run, exactly like tools/make_icons.py and the ribbon icons.
"""

from __future__ import annotations

import html
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(ROOT, 'docs', 'authoring')
CONTENT = os.path.join(SITE, '_content')

# Page order is the reading order: it drives the sidebar, the previous/next links
# and the numbering. Adding a page means adding a fragment and a line here.
PAGES = [
    # (fragment, group)
    ('overview.html',        'Start'),
    ('quickstart.html',      'Start'),
    ('anatomy.html',         'Bundles'),
    ('buttons.html',         'Bundles'),
    ('bundle-yaml.html',     'Bundles'),
    ('icons.html',           'Bundles'),
    ('shortcuts.html',       'Bundles'),
    ('click-actions.html',   'Bundles'),
    ('hooks.html',           'Bundles'),
    ('script-environment.html', 'Scripts'),
    ('user-interface.html',  'Scripts'),
    ('settings.html',        'Scripts'),
    ('api-reference.html',   'Reference'),
    ('cookbook.html',        'Practice'),
    ('troubleshooting.html', 'Practice'),
    ('style-guide.html',     'Practice'),
]

GROUP_ORDER = ['Start', 'Bundles', 'Scripts', 'Reference', 'Practice']


# --------------------------------------------------------------------------- #
# fragment parsing
# --------------------------------------------------------------------------- #

META_RE = re.compile(r'^\s*<!--\s*(\w+):\s*(.*?)\s*-->\s*$', re.MULTILINE)


class Page(object):
    def __init__(self, fragment, group, number):
        self.fragment = fragment
        self.group = group
        self.number = number
        self.slug = os.path.splitext(fragment)[0]
        self.url = self.slug + '.html'
        self.title = self.slug
        self.lead = ''
        self.eyebrow = group
        self.body = ''
        self.headings = []  # (level, id, text)


def read_fragment(path, page):
    with io.open(path, encoding='utf-8') as fh:
        raw = fh.read()

    meta = {}
    for match in META_RE.finditer(raw):
        meta[match.group(1).lower()] = match.group(2)
    body = META_RE.sub('', raw).strip()

    page.title = meta.get('title', page.title)
    page.lead = meta.get('lead', '')
    page.eyebrow = meta.get('eyebrow', page.group)
    page.body = body
    return page


# --------------------------------------------------------------------------- #
# syntax highlighting
# --------------------------------------------------------------------------- #

PY_KEYWORDS = {
    'and', 'as', 'assert', 'break', 'class', 'continue', 'def', 'del', 'elif',
    'else', 'except', 'finally', 'for', 'from', 'global', 'if', 'import', 'in',
    'is', 'lambda', 'nonlocal', 'not', 'or', 'pass', 'raise', 'return', 'try',
    'while', 'with', 'yield', 'None', 'True', 'False', 'self',
}

# One pass, alternation ordered so strings and comments win over everything else.
PY_TOKEN = re.compile(
    r'(?P<s>"""[\s\S]*?"""|\'\'\'[\s\S]*?\'\'\'|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')'
    r'|(?P<c>#[^\n]*)'
    r'|(?P<n>\b\d+(?:\.\d+)?\b)'
    r'|(?P<w>\b[A-Za-z_][A-Za-z_0-9]*\b)'
)

YAML_TOKEN = re.compile(
    r'(?P<c>#[^\n]*)'
    r'|(?P<k>^[ \t]*[A-Za-z_][\w.-]*(?=:))'
    r'|(?P<s>"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')',
    re.MULTILINE,
)

JSON_TOKEN = re.compile(
    r'(?P<k>"(?:\\.|[^"\\])*"(?=\s*:))'
    r'|(?P<s>"(?:\\.|[^"\\])*")'
    r'|(?P<n>\b-?\d+(?:\.\d+)?\b)'
    r'|(?P<w>\b(?:true|false|null)\b)'
)

TREE_TOKEN = re.compile(
    r'(?P<c>#[^\n]*)'
    r'|(?P<s>\b\w[\w.\-]*\.(?:pushbutton|stack|pulldown|panel|tab|extension|nobutton|urlbutton'
    r'|linkbutton|toggle|smartbutton|splitbutton|splitpushbutton|lib)\b)'
    r'|(?P<k>\b(?:script|config|startup)\.py\b|\bbundle\.yaml\b|\bextension\.yaml\b'
    r'|\bicon(?:\.small|\.on|\.off)?(?:\.dark)?\.png\b)'
)


def _wrap(cls, text):
    return '<span class="tok-%s">%s</span>' % (cls, html.escape(text))


def highlight(code, lang):
    """Escape and lightly colour a code block. Unknown languages are escaped only."""
    if lang == 'python':
        def repl(m):
            if m.group('s'):
                return _wrap('s', m.group('s'))
            if m.group('c'):
                return _wrap('c', m.group('c'))
            if m.group('n'):
                return _wrap('n', m.group('n'))
            word = m.group('w')
            return _wrap('k', word) if word in PY_KEYWORDS else html.escape(word)
        return _sub_escaping_gaps(PY_TOKEN, repl, code)

    if lang == 'yaml':
        def repl(m):
            if m.group('c'):
                return _wrap('c', m.group('c'))
            if m.group('k'):
                return _wrap('k', m.group('k'))
            return _wrap('s', m.group('s'))
        return _sub_escaping_gaps(YAML_TOKEN, repl, code)

    if lang == 'json':
        def repl(m):
            if m.group('k'):
                return _wrap('f', m.group('k'))
            if m.group('s'):
                return _wrap('s', m.group('s'))
            if m.group('n'):
                return _wrap('n', m.group('n'))
            return _wrap('k', m.group('w'))
        return _sub_escaping_gaps(JSON_TOKEN, repl, code)

    if lang == 'tree':
        def repl(m):
            if m.group('c'):
                return _wrap('c', m.group('c'))
            if m.group('s'):
                return _wrap('f', m.group('s'))
            return _wrap('k', m.group('k'))
        return _sub_escaping_gaps(TREE_TOKEN, repl, code)

    return html.escape(code)


def _sub_escaping_gaps(pattern, repl, text):
    """re.sub, but the text BETWEEN matches is HTML-escaped rather than passed through."""
    out = []
    last = 0
    for match in pattern.finditer(text):
        out.append(html.escape(text[last:match.start()]))
        out.append(repl(match))
        last = match.end()
    out.append(html.escape(text[last:]))
    return ''.join(out)


LANG_LABEL = {
    'python': 'python',
    'yaml': 'bundle.yaml',
    'json': 'config.json',
    'tree': 'folders',
    'text': 'text',
    'powershell': 'powershell',
}

PRE_RE = re.compile(
    r'<pre(?P<attrs>[^>]*)>(?P<code>[\s\S]*?)</pre>'
)
ATTR_RE = re.compile(r'(\w[\w-]*)="([^"]*)"')


def build_code_blocks(body):
    """Turn <pre data-lang="python" data-file="script.py"> into a copyable code card."""
    def repl(match):
        attrs = dict(ATTR_RE.findall(match.group('attrs')))
        # <pre> inside a do/dont panel stays bare - it is a fragment, not a file.
        if attrs.get('data-bare') == 'true':
            return '<pre>%s</pre>' % highlight(_dedent(match.group('code')), attrs.get('data-lang', ''))
        lang = attrs.get('data-lang', 'text')
        label = attrs.get('data-file') or LANG_LABEL.get(lang, lang)
        code = _dedent(match.group('code'))
        return (
            '<div class="code">'
            '<div class="code-head"><span>%s</span>'
            '<button class="copy-btn" type="button">Copy</button></div>'
            '<pre><code>%s</code></pre>'
            '</div>'
        ) % (html.escape(label), highlight(code, lang))

    return PRE_RE.sub(repl, body)


def _dedent(code):
    code = code.replace('\r\n', '\n')
    code = code.strip('\n')
    lines = code.split('\n')
    indents = [len(l) - len(l.lstrip()) for l in lines if l.strip()]
    cut = min(indents) if indents else 0
    return '\n'.join(l[cut:] if l.strip() else '' for l in lines).rstrip()


# --------------------------------------------------------------------------- #
# headings, contents rail, search index
# --------------------------------------------------------------------------- #

HEADING_RE = re.compile(r'<h(?P<lvl>[23])(?P<attrs>[^>]*)>(?P<text>[\s\S]*?)</h(?P=lvl)>')
TAG_RE = re.compile(r'<[^>]+>')


def slugify(text):
    text = TAG_RE.sub('', text)
    text = html.unescape(text).lower()
    text = re.sub(r'[^a-z0-9]+', '-', text).strip('-')
    return text or 'section'


def process_headings(page):
    """Give every h2/h3 an id and an anchor link; record them for the rail."""
    seen = {}

    def repl(match):
        level = int(match.group('lvl'))
        text = match.group('text')
        attrs = dict(ATTR_RE.findall(match.group('attrs')))
        hid = attrs.get('id') or slugify(text)
        if hid in seen:
            seen[hid] += 1
            hid = '%s-%d' % (hid, seen[hid])
        else:
            seen[hid] = 1
        page.headings.append((level, hid, TAG_RE.sub('', text).strip()))
        return ('<h%d id="%s">%s<a class="anchor" href="#%s" '
                'aria-label="Link to this section">#</a></h%d>'
                % (level, hid, text, hid, level))

    page.body = HEADING_RE.sub(repl, page.body)


def section_text(page, hid):
    """The first plain-text run after a heading, for the search preview."""
    marker = 'id="%s"' % hid
    start = page.body.find(marker)
    if start < 0:
        return ''
    chunk = page.body[start:start + 2600]
    chunk = chunk.split('</h', 1)[-1]
    chunk = re.sub(r'<(pre|table|svg)[\s\S]*?</\1>', ' ', chunk)
    text = html.unescape(TAG_RE.sub(' ', chunk))
    text = re.sub(r'\s+', ' ', text).strip()
    return text[:190]


# --------------------------------------------------------------------------- #
# shell
# --------------------------------------------------------------------------- #

def _logo():
    """The transparent mark from assets/logo (written by tools/make_logo.py), shrunk
    to sidebar size. Read, not copied, so a logo change only needs a rebuild."""
    with open(os.path.join(ROOT, 'assets', 'logo', 'pynavis-mark.transparent.svg'),
              encoding='utf-8') as f:
        svg = f.read()
    svg = re.sub(r'\s*<title>[^<]*</title>', '', svg)
    svg = re.sub(r'\s*\n\s*', '', svg)
    return svg.replace('width="512" height="512"', 'width="26" height="26" aria-hidden="true"')


LOGO = _logo()


def sidebar(pages, current):
    out = ['<nav class="pages">']
    for group in GROUP_ORDER:
        members = [p for p in pages if p.group == group]
        if not members:
            continue
        out.append('<div class="grp">%s</div>' % html.escape(group))
        for page in members:
            cls = ' class="active"' if page is current else ''
            out.append(
                '<a href="%s"%s><span class="num">%02d</span>%s</a>'
                % (page.url, cls, page.number, html.escape(page.title))
            )
    out.append('</nav>')
    return '\n'.join(out)


def contents_rail(page):
    if len(page.headings) < 2:
        return '<aside class="toc"></aside>'
    out = ['<aside class="toc"><div class="toc-head">On this page</div>']
    for level, hid, text in page.headings:
        cls = ' class="h3"' if level == 3 else ''
        out.append('<a href="#%s"%s>%s</a>' % (hid, cls, html.escape(text)))
    out.append('</aside>')
    return '\n'.join(out)


def pagenav(pages, page):
    idx = pages.index(page)
    prev = pages[idx - 1] if idx > 0 else None
    nxt = pages[idx + 1] if idx < len(pages) - 1 else None
    if not prev and not nxt:
        return ''
    out = ['<div class="pagenav">']
    if prev:
        out.append('<a href="%s"><span class="dir">Previous</span>'
                   '<span class="ttl">%s</span></a>' % (prev.url, html.escape(prev.title)))
    else:
        out.append('<span></span>')
    if nxt:
        out.append('<a class="next" href="%s"><span class="dir">Next</span>'
                   '<span class="ttl">%s</span></a>' % (nxt.url, html.escape(nxt.title)))
    else:
        out.append('<span></span>')
    out.append('</div>')
    return '\n'.join(out)


SHELL = """<!doctype html>
<html lang="en" data-theme="light">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} - pyNavis authoring</title>
<meta name="description" content="{lead_plain}">
<link rel="stylesheet" href="_assets/site.css">
</head>
<body data-base="">
<div class="layout">
<div class="sidebar">
  <div class="brand"><a href="index.html">{logo}<span class="brand-name">pyNavis
    <span class="brand-sub">Tool authoring guide</span></span></a></div>
  <div class="search-wrap">
    <svg class="search-icon" width="14" height="14" viewBox="0 0 14 14" fill="none">
      <circle cx="6" cy="6" r="4.2" stroke="currentColor" stroke-width="1.5"/>
      <path d="M9.2 9.2 12.5 12.5" stroke="currentColor" stroke-width="1.5"
            stroke-linecap="round"/></svg>
    <input id="search" type="search" placeholder="Search the docs" autocomplete="off"
           spellcheck="false" aria-label="Search the documentation">
    <span class="search-kbd">/</span>
    <div id="search-results" class="search-results"></div>
  </div>
  {sidebar}
  <div class="side-foot">
    <span>{version}</span>
    <button id="theme-toggle" type="button" aria-label="Toggle colour theme">
      <span>Dark</span></button>
  </div>
</div>
<main class="main">
<article>
  <p class="eyebrow">{eyebrow}</p>
  <h1>{title}</h1>
  {lead}
  {body}
  {pagenav}
</article>
{toc}
</main>
</div>
<script src="_assets/search-index.js"></script>
<script src="_assets/site.js"></script>
</body>
</html>
"""


def render_page(pages, page, version):
    lead = '<p class="lead">%s</p>' % page.lead if page.lead else ''
    return SHELL.format(
        title=html.escape(page.title),
        lead=lead,
        lead_plain=html.escape(TAG_RE.sub('', page.lead))[:180],
        eyebrow=html.escape(page.eyebrow),
        logo=LOGO,
        sidebar=sidebar(pages, page),
        body=page.body,
        pagenav=pagenav(pages, page),
        toc=contents_rail(page),
        version=html.escape(version),
    )


# --------------------------------------------------------------------------- #
# search index
# --------------------------------------------------------------------------- #

def render_index(pages):
    """window.PYNAVIS_SEARCH = [{t:title, p:page, u:url, b:body}, ...]"""
    entries = []
    for page in pages:
        entries.append({
            't': page.title,
            'p': 'Page',
            'u': page.url,
            'b': TAG_RE.sub('', page.lead),
        })
        for _level, hid, text in page.headings:
            entries.append({
                't': text,
                'p': page.title,
                'u': '%s#%s' % (page.url, hid),
                'b': section_text(page, hid),
            })

    def js(entry):
        return '{t:%s,p:%s,u:%s,b:%s}' % (
            jstr(entry['t']), jstr(entry['p']), jstr(entry['u']), jstr(entry['b']))

    body = ',\n'.join(js(e) for e in entries)
    return ('/* Generated by tools/build_docs.py - do not edit. */\n'
            'window.PYNAVIS_SEARCH = [\n%s\n];\n' % body)


def jstr(text):
    out = (text or '').replace('\\', '\\\\').replace('"', '\\"')
    out = out.replace('\n', ' ').replace('\r', ' ')
    out = out.replace('</', '<\\/')  # never close the host <script> early
    return '"%s"' % out


# --------------------------------------------------------------------------- #
# landing page
# --------------------------------------------------------------------------- #

def render_landing(pages, version):
    groups = []
    for group in GROUP_ORDER:
        members = [p for p in pages if p.group == group]
        if not members:
            continue
        cards = '\n'.join(
            '<a href="%s"><span class="c-t">%s</span>'
            '<span class="c-d">%s</span></a>'
            % (p.url, html.escape(p.title), html.escape(TAG_RE.sub('', p.lead)))
            for p in members
        )
        groups.append('<h2 id="%s">%s</h2>\n<div class="cards">%s</div>'
                      % (slugify(group), html.escape(group), cards))

    landing = Page('index.html', 'Start', 0)
    landing.title = 'Build tools for Navisworks'
    landing.eyebrow = 'pyNavis authoring guide'
    landing.lead = ('Everything you need to turn a folder and a Python file into a '
                    'button on the Navisworks ribbon. No compiler, no Visual Studio, '
                    'no restart.')
    landing.body = LANDING_INTRO + '\n'.join(groups)
    landing.url = 'index.html'

    html_out = SHELL.format(
        title=html.escape(landing.title),
        lead='<p class="lead">%s</p>' % landing.lead,
        lead_plain=html.escape(landing.lead)[:180],
        eyebrow=html.escape(landing.eyebrow),
        logo=LOGO,
        sidebar=sidebar(pages, None),
        body=landing.body,
        pagenav='<div class="pagenav"><span></span>'
                '<a class="next" href="%s"><span class="dir">Start here</span>'
                '<span class="ttl">%s</span></a></div>' % (pages[0].url, html.escape(pages[0].title)),
        toc='<aside class="toc"></aside>',
        version=html.escape(version),
    )
    return html_out


LANDING_INTRO = """
<div class="note tip">
<span class="note-t">New here</span>
<p>Read <a href="quickstart.html">Your first button</a> first. It gets a working tool on
the ribbon in about five minutes, and every other page assumes you have done it.</p>
</div>

<p>pyNavis loads <strong>extensions</strong> from disk at startup. An extension is a folder
tree whose folder names describe the ribbon: a <code>.tab</code> becomes a ribbon tab, a
<code>.panel</code> becomes a panel on it, and a <code>.pushbutton</code> becomes a button
that runs the <code>script.py</code> inside it. There is no manifest to register, no project
to compile, and no build step. Create the folders, drop in a script, press
<strong>Reload</strong>.</p>

<p>These pages document the authoring surface exhaustively: every folder suffix the runtime
recognises, every key it reads out of <code>bundle.yaml</code>, every global it injects into
your script, and every function in the <code>pynavis</code> library. Where the runtime has a
sharp edge, it is called out rather than glossed over.</p>
"""


# --------------------------------------------------------------------------- #
# lint
# --------------------------------------------------------------------------- #

VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link',
        'meta', 'param', 'source', 'track', 'wbr', 'path', 'circle', 'rect',
        'line', 'polyline', 'polygon', 'ellipse', 'use', 'stop', 'image'}

VALID_LANGS = {'python', 'yaml', 'json', 'tree', 'powershell', 'text', ''}

# Hex colours are fine in a swatch, where the point is the literal value. Anywhere
# else in an SVG they break dark mode, so the diagram must use the CSS variables.
# The negative lookbehind keeps numeric HTML entities (&#8220;) out of the results.
HEX_RE = re.compile(r'(?<!&)#[0-9a-fA-F]{3,8}\b')
SVG_RE = re.compile(r'<svg[\s\S]*?</svg>')
DASH_RE = re.compile(r'[–—]')


class Balance(object):
    """Minimal tag-balance check: catches the unclosed div that wrecks a layout."""

    def __init__(self):
        try:
            from html.parser import HTMLParser
        except ImportError:
            from HTMLParser import HTMLParser  # noqa
        self.problems = []
        outer = self

        class P(HTMLParser):
            def __init__(self):
                HTMLParser.__init__(self)
                self.stack = []

            def handle_starttag(self, tag, attrs):
                if tag not in VOID:
                    self.stack.append((tag, self.getpos()[0]))

            def handle_endtag(self, tag):
                if tag in VOID:
                    return
                if not self.stack:
                    outer.problems.append('line %d: stray </%s>' % (self.getpos()[0], tag))
                    return
                if self.stack[-1][0] == tag:
                    self.stack.pop()
                else:
                    for depth in range(len(self.stack) - 1, -1, -1):
                        if self.stack[depth][0] == tag:
                            unclosed = self.stack[depth + 1:]
                            outer.problems.append(
                                'line %d: </%s> closes past unclosed %s'
                                % (self.getpos()[0], tag,
                                   ', '.join('<%s> (line %d)' % u for u in unclosed)))
                            del self.stack[depth:]
                            return
                    outer.problems.append('line %d: stray </%s>' % (self.getpos()[0], tag))

        self.parser = P()

    def run(self, text):
        self.parser.feed(text)
        for tag, line in self.parser.stack:
            self.problems.append('unclosed <%s> opened on line %d' % (tag, line))
        return self.problems


def public_api():
    """{module: {public names}} for pynavislib, so examples can be checked against it."""
    import ast
    libdir = os.path.join(ROOT, 'pynavislib', 'pynavis')
    api = {}
    if not os.path.isdir(libdir):
        return api
    for entry in sorted(os.listdir(libdir)):
        if not entry.endswith('.py') or entry.startswith('_'):
            continue
        module = entry[:-3]
        try:
            with io.open(os.path.join(libdir, entry), encoding='utf-8') as fh:
                tree = ast.parse(fh.read())
        except (IOError, SyntaxError):
            continue
        names = set()
        for node in tree.body:
            if isinstance(node, (ast.FunctionDef, ast.ClassDef)):
                names.add(node.name)
            elif isinstance(node, ast.Assign):
                for target in node.targets:
                    if isinstance(target, ast.Name):
                        names.add(target.id)
        api[module] = set(n for n in names if not n.startswith('_'))
    return api


PY_BLOCK_RE = re.compile(r'<pre[^>]*data-lang="python"[^>]*>([\s\S]*?)</pre>')
CODE_SPAN_RE = re.compile(r'<code>([^<]{1,80})</code>')


def check_prose(raw, api):
    """API names cited in prose must exist too.

    Only two unambiguous shapes are checked, so that filenames like "script.py" and
    "bundle.yaml" are never mistaken for an attribute access: a fully qualified
    "pynavis.module.name", or a "module.name()" call with parentheses.
    """
    if not api:
        return []
    problems = []
    modules = '|'.join(sorted(api))
    qualified = re.compile(r'^pynavis\.(%s)\.(\w+)$' % modules)
    called = re.compile(r'^(%s)\.(\w+)\(\)?$' % modules)
    for match in CODE_SPAN_RE.finditer(raw):
        span = html.unescape(match.group(1)).strip()
        hit = qualified.match(span) or called.match(span)
        if hit and hit.group(2) not in api[hit.group(1)]:
            problems.append('%s.%s cited in prose does not exist in pynavislib'
                            % (hit.group(1), hit.group(2)))
    return problems


def check_examples(raw, api):
    """Every pynavis call in an example must resolve to a real name in the library."""
    if not api:
        return []
    problems = []
    # A trailing "(" means a call; an ALL_CAPS name means a constant. Anything else
    # (notably the literal filename "script.py") is not an API reference.
    pattern = re.compile(
        r'\b(' + '|'.join(sorted(api)) + r')\.([A-Za-z_][A-Za-z_0-9]*)\s*(\()?')
    for match in PY_BLOCK_RE.finditer(raw):
        code = html.unescape(match.group(1))
        for ref in pattern.finditer(code):
            module, attr, call = ref.group(1), ref.group(2), ref.group(3)
            if not call and attr != attr.upper():
                continue
            if attr not in api[module]:
                problems.append('%s.%s does not exist in pynavislib' % (module, attr))
    return problems


def lint(pages):
    """House rules. Errors block --check; warnings are advisory."""
    errors = []
    warnings = []
    api = public_api()
    # Validate against the whole manifest, not just the pages built so far, so a
    # cross-link to a page that is still being written is not reported as broken.
    known = set(os.path.splitext(f)[0] + '.html' for f, _g in PAGES) | {'index.html'}
    anchors = {}
    for page in pages:
        anchors[page.url] = set(hid for _l, hid, _t in page.headings)
    anchors['index.html'] = set(slugify(g) for g in GROUP_ORDER)

    for page in pages:
        with io.open(os.path.join(CONTENT, page.fragment), encoding='utf-8') as fh:
            raw = fh.read()
        where = '_content/%s' % page.fragment

        for problem in Balance().run(raw):
            errors.append('%s: %s' % (where, problem))

        if '<h1' in raw:
            errors.append('%s: fragments must not contain <h1>, the shell supplies it' % where)

        for problem in sorted(set(check_examples(raw, api) + check_prose(raw, api))):
            errors.append('%s: %s' % (where, problem))

        # The user preference, and the design system bans them too.
        for match in DASH_RE.finditer(raw):
            line = raw.count('\n', 0, match.start()) + 1
            context = raw[max(0, match.start() - 40):match.start() + 40].replace('\n', ' ')
            errors.append('%s line %d: em or en dash, use a comma or restructure: ...%s...'
                          % (where, line, context.strip()))

        for match in re.finditer(r'<pre([^>]*)>', raw):
            attrs = dict(ATTR_RE.findall(match.group(1)))
            lang = attrs.get('data-lang', '')
            if lang not in VALID_LANGS:
                errors.append('%s: unknown data-lang "%s"' % (where, lang))

        for svg in SVG_RE.findall(raw):
            # A malformed diagram fails silently in the browser, so parse it here.
            try:
                import xml.etree.ElementTree as ET
                # Named HTML entities are legal in HTML but not in bare XML.
                probe = re.sub(r'&(?!#|amp;|lt;|gt;|quot;|apos;)[a-zA-Z]+;', '&amp;', svg)
                ET.fromstring(probe.encode('utf-8'))
            except Exception as ex:
                label = re.search(r'aria-label="([^"]{0,60})', svg)
                errors.append('%s: malformed SVG (%s): %s'
                              % (where, label.group(1) if label else '?', ex))

            if 'data-literal-colors' in svg[:400]:
                continue
            for hexcode in set(HEX_RE.findall(svg)):
                warnings.append('%s: hardcoded %s inside an SVG breaks dark mode; '
                                'use var(--ink) etc, or mark the figure '
                                'data-literal-colors="true"' % (where, hexcode))

        for href in re.findall(r'href="([^"]+)"', raw):
            if href.startswith(('http://', 'https://', 'mailto:')):
                continue
            target, _, frag = href.partition('#')
            if target and target not in known:
                errors.append('%s: link to unknown page "%s"' % (where, target))
            elif frag:
                page_key = target or page.url
                if page_key in anchors and frag not in anchors[page_key]:
                    warnings.append('%s: link to missing anchor "%s" on %s'
                                    % (where, frag, page_key))
    return errors, warnings


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #

def read_version():
    init = os.path.join(ROOT, 'pynavislib', 'pynavis', '__init__.py')
    try:
        with io.open(init, encoding='utf-8') as fh:
            match = re.search(r"__version__\s*=\s*'([^']+)'", fh.read())
        if match:
            return 'pynavis %s' % match.group(1)
    except IOError:
        pass
    return 'pyNavis'


def build():
    if not os.path.isdir(CONTENT):
        sys.exit('No content folder at %s' % CONTENT)

    version = read_version()
    pages = []
    missing = []
    for number, (fragment, group) in enumerate(PAGES, start=1):
        path = os.path.join(CONTENT, fragment)
        if not os.path.isfile(path):
            # Skip rather than abort, so the site still builds while a page is being
            # written. --check treats a missing fragment as a failure.
            missing.append(fragment)
            continue
        page = read_fragment(path, Page(fragment, group, number))
        page.body = build_code_blocks(page.body)
        process_headings(page)
        pages.append(page)

    written = {}
    for page in pages:
        written[page.url] = render_page(pages, page, version)
    written['index.html'] = render_landing(pages, version)
    written[os.path.join('_assets', 'search-index.js')] = render_index(pages)

    changed = []
    for rel, text in written.items():
        target = os.path.join(SITE, rel)
        old = None
        if os.path.isfile(target):
            with io.open(target, encoding='utf-8') as fh:
                old = fh.read()
        if old != text:
            changed.append(rel)
            if '--check' not in sys.argv:
                with io.open(target, 'w', encoding='utf-8', newline='\n') as fh:
                    fh.write(text)

    errors, warnings = lint(pages)
    for warning in warnings:
        print('  warn  %s' % warning)
    for error in errors:
        print('  ERROR %s' % error)

    if '--check' in sys.argv:
        if missing:
            sys.exit('Missing fragment(s): %s' % ', '.join(missing))
        if errors:
            sys.exit('%d lint error(s).' % len(errors))
        if changed:
            sys.exit('Out of date, re-run tools/build_docs.py:\n  ' + '\n  '.join(sorted(changed)))
        print('Docs are up to date (%d pages).' % len(pages))
        return

    total = sum(len(p.headings) for p in pages)
    print('Built %d pages + landing, %d sections indexed.' % (len(pages), total + len(pages)))
    if changed:
        print('Updated: %s' % ', '.join(sorted(changed)))
    else:
        print('No changes.')
    if missing:
        print('WARNING: %d fragment(s) not written yet: %s'
              % (len(missing), ', '.join(missing)))


if __name__ == '__main__':
    build()
