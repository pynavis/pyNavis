"""Builds the pyNavis tools guide, the user manual published at tools.pynavis.com.

One page per tool, grouped by ribbon panel, generated from fragments in
docs/tools/_content/<panel>.html. Each fragment holds one <section> per tool with
the tool's metadata in data attributes and its prose as ordinary HTML. This script
supplies the shell (sidebar, previous/next, the icon strip on the landing page),
copies each tool's ribbon icon out of its bundle so the page shows the same art the
ribbon does, and turns every <figure data-shot="name"> into either the screenshot at
docs/tools/_assets/shots/<name>.png or, until that file exists, a labelled
placeholder. SHOTLIST.md lists every shot the pages are waiting for.

    python tools/build_tools_site.py            regenerate the site
    python tools/build_tools_site.py --check    fail if the output is out of date

Dev-time CPython only, standard library only. Never hand-edit a page in docs/tools/:
change the fragment or this script and re-run, like tools/build_docs.py.
"""

from __future__ import annotations

import html
import io
import os
import re
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(ROOT, 'docs', 'tools')
CONTENT = os.path.join(SITE, '_content')
ASSETS = os.path.join(SITE, '_assets')
SHOTS = os.path.join(ASSETS, 'shots')
ICONS = os.path.join(ASSETS, 'icons')
AUTHORING_ASSETS = os.path.join(ROOT, 'docs', 'authoring', '_assets')
EXTENSION = os.path.join(ROOT, 'extensions', 'pyNavis.extension', 'pyNavis.tab')

# Panel order is the ribbon's order. Each fragment holds that panel's tools.
PANELS = [
    ('pynavis.html',    'pyNavis',    'The tool itself: settings, shortcuts, the console, Reload.'),
    ('selection.html',  'Selection',  'Undo for selections: remember a selection, get it back, build on it.'),
    ('clash.html',      'Clash',      'Report and group clashes, clear them to the gap you want, and measure the real gap.'),
    ('viewpoints.html', 'Viewpoints', 'Saved views, the section box, view state and navigation speeds.'),
    ('ai.html',         'AI (beta)',  'A chat that writes pyNavis tools for you, and the setup behind it.'),
    ('data.html',       'Data',       'Coordinates, Revit element IDs, selection sets and viewpoints, in and out.'),
]

SECTION_RE = re.compile(r'<section\b([^>]*)>(.*?)</section>', re.DOTALL)
ATTR_RE = re.compile(r'([\w-]+)="([^"]*)"')
FIGURE_RE = re.compile(r'<figure\s+data-shot="([^"]+)"(?:\s+data-wide)?>\s*<figcaption>(.*?)</figcaption>\s*</figure>', re.DOTALL)
TAG_RE = re.compile(r'<[^>]+>')
H2_RE = re.compile(r'<h2>(.*?)</h2>')


class Tool(object):
    def __init__(self, panel, attrs, body):
        self.panel = panel
        self.slug = attrs['id']
        self.title = attrs['title']
        self.summary = attrs.get('summary', '')
        self.bundle = attrs.get('bundle', '')          # folder under pyNavis.tab, for the icon
        self.needs = attrs.get('needs', 'Nothing')
        self.shortcut = attrs.get('shortcut', '')
        self.shift = attrs.get('shift', '')
        self.kind = attrs.get('kind', 'Button')       # Button, Dock panel, Menu, Chord
        self.body = body.strip()
        self.url = self.slug + '.html'
        self.icon = None
        self.headings = []


def read_panel(fragment, panel):
    path = os.path.join(CONTENT, fragment)
    with io.open(path, encoding='utf-8') as fh:
        raw = fh.read()
    intro = raw[:raw.find('<section')].strip() if '<section' in raw else raw.strip()
    tools = []
    for match in SECTION_RE.finditer(raw):
        attrs = dict(ATTR_RE.findall(match.group(1)))
        if 'id' not in attrs or 'title' not in attrs:
            sys.exit('%s: every <section> needs id= and title=' % fragment)
        tools.append(Tool(panel, attrs, match.group(2)))
    return intro, tools


# --------------------------------------------------------------------------- #
# icons and screenshots
# --------------------------------------------------------------------------- #

def copy_icon(tool):
    """The bundle's large icon, copied beside the page. The dark variant too, so the
    page can swap it with the theme."""
    if not tool.bundle:
        return
    folder = os.path.join(EXTENSION, tool.bundle.replace('/', os.sep))
    for name in ('icon.png', 'icon.dark.png'):
        src = os.path.join(folder, name)
        if not os.path.isfile(src):
            # A *.toggle draws its art per state instead; the resting one stands for it.
            src = os.path.join(folder, name.replace('icon', 'icon.off', 1))
        if not os.path.isfile(src):
            continue
        dst = os.path.join(ICONS, tool.slug + ('.dark' if 'dark' in name else '') + '.png')
        if not os.path.isfile(dst) or open(src, 'rb').read() != open(dst, 'rb').read():
            shutil.copyfile(src, dst)
    if os.path.isfile(os.path.join(ICONS, tool.slug + '.png')):
        tool.icon = '_assets/icons/%s.png' % tool.slug


def figures(body, tool, wanted):
    def repl(match):
        name, caption = match.group(1), match.group(2).strip()
        wide = ' wide' if 'data-wide' in match.group(0) else ''
        path = os.path.join(SHOTS, name + '.png')
        if os.path.isfile(path):
            return ('<figure class="shot%s"><img src="_assets/shots/%s.png" alt="%s" loading="lazy">'
                    '<figcaption>%s</figcaption></figure>'
                    % (wide, name, html.escape(TAG_RE.sub('', caption)), caption))
        # No file yet: the figure is dropped from the page entirely (a "to come"
        # box on a public site reads as unfinished). SHOTLIST.md still lists it.
        wanted.append((tool, name, TAG_RE.sub('', caption)))
        return ''
    return FIGURE_RE.sub(repl, body)


# --------------------------------------------------------------------------- #
# rendering
# --------------------------------------------------------------------------- #

def slugify(text):
    return re.sub(r'[^a-z0-9]+', '-', TAG_RE.sub('', text).lower()).strip('-')


def headings(tool):
    out = []
    def repl(match):
        text = match.group(1)
        hid = slugify(text)
        out.append((hid, TAG_RE.sub('', text)))
        return '<h2 id="%s">%s<a class="anchor" href="#%s" aria-label="Link to this section">#</a></h2>' % (hid, text, hid)
    tool.body = H2_RE.sub(repl, tool.body)
    tool.headings = out


def _logo():
    with open(os.path.join(ROOT, 'assets', 'logo', 'pynavis-mark.transparent.svg'),
              encoding='utf-8') as f:
        svg = f.read()
    svg = re.sub(r'\s*<title>[^<]*</title>', '', svg)
    svg = re.sub(r'\s*\n\s*', '', svg)
    return svg.replace('width="512" height="512"', 'width="26" height="26" aria-hidden="true"')


LOGO = _logo()


def sidebar(panels, current):
    out = ['<nav class="pages">']
    for panel, _lead, tools in panels:
        out.append('<div class="grp">%s</div>' % html.escape(panel))
        for tool in tools:
            cls = ' class="active"' if tool is current else ''
            icon = ('<img class="nav-icon" src="%s" alt="">' % tool.icon) if tool.icon else '<span class="nav-icon"></span>'
            out.append('<a href="%s"%s>%s%s</a>' % (tool.url, cls, icon, html.escape(tool.title)))
    out.append('</nav>')
    return '\n'.join(out)


def contents_rail(tool):
    if len(tool.headings) < 2:
        return '<aside class="toc"></aside>'
    out = ['<aside class="toc"><div class="toc-head">On this page</div>']
    for hid, text in tool.headings:
        out.append('<a href="#%s">%s</a>' % (hid, html.escape(text)))
    out.append('</aside>')
    return '\n'.join(out)


def pagenav(flat, tool):
    idx = flat.index(tool)
    prev = flat[idx - 1] if idx > 0 else None
    nxt = flat[idx + 1] if idx < len(flat) - 1 else None
    out = ['<div class="pagenav">']
    if prev:
        out.append('<a href="%s"><span class="dir">Previous</span><span class="ttl">%s</span></a>'
                   % (prev.url, html.escape(prev.title)))
    else:
        out.append('<span></span>')
    if nxt:
        out.append('<a class="next" href="%s"><span class="dir">Next</span><span class="ttl">%s</span></a>'
                   % (nxt.url, html.escape(nxt.title)))
    else:
        out.append('<span></span>')
    out.append('</div>')
    return '\n'.join(out)


def facts(tool):
    rows = [('On the ribbon', '%s, %s panel' % (tool.kind, tool.panel)),
            ('Needs', tool.needs)]
    if tool.shortcut:
        rows.append(('Shortcut', tool.shortcut))
    if tool.shift:
        rows.append(('Shift+Click', tool.shift))
    cells = ''.join('<div class="fact"><span class="fact-k">%s</span><span class="fact-v">%s</span></div>'
                    % (html.escape(k), v) for k, v in rows)
    return '<div class="facts">%s</div>' % cells


SHELL = """<!doctype html>
<html lang="en" data-theme="light">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} - pyNavis tools</title>
<meta name="description" content="{description}">
<link rel="stylesheet" href="_assets/site.css">
<link rel="stylesheet" href="_assets/tools.css">
</head>
<body data-base="">
<div class="layout">
<div class="sidebar">
  <div class="brand"><a href="index.html">{logo}<span class="brand-name">pyNavis
    <span class="brand-sub">Tools guide</span></span></a></div>
  <div class="search-wrap">
    <svg class="search-icon" width="14" height="14" viewBox="0 0 14 14" fill="none">
      <circle cx="6" cy="6" r="4.2" stroke="currentColor" stroke-width="1.5"/>
      <path d="M9.2 9.2 12.5 12.5" stroke="currentColor" stroke-width="1.5"
            stroke-linecap="round"/></svg>
    <input id="search" type="search" placeholder="Search the tools" autocomplete="off"
           spellcheck="false" aria-label="Search the tools guide">
    <span class="search-kbd">/</span>
    <div id="search-results" class="search-results"></div>
  </div>
  {sidebar}
  <div class="side-foot">
    <span>{version}</span>
    <a class="side-link" href="https://docs.pynavis.com/">Authoring guide</a>
    <button id="theme-toggle" type="button" aria-label="Toggle colour theme">
      <span>Dark</span></button>
  </div>
</div>
<main class="main">
<article>
  {hero}
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


def render_tool(panels, flat, tool, version):
    icon = ('<img class="hero-icon" src="%s" alt="">' % tool.icon) if tool.icon else ''
    hero = ('<div class="hero">%s<div><p class="eyebrow">%s panel</p><h1>%s</h1>'
            '<p class="lead">%s</p></div></div>%s'
            % (icon, html.escape(tool.panel), html.escape(tool.title), tool.summary, facts(tool)))
    return SHELL.format(
        title=html.escape(tool.title),
        description=html.escape(TAG_RE.sub('', tool.summary))[:180],
        logo=LOGO,
        sidebar=sidebar(panels, tool),
        hero=hero,
        body=tool.body,
        pagenav=pagenav(flat, tool),
        toc=contents_rail(tool),
        version=html.escape(version),
    )


def render_landing(panels, intros, version):
    groups = []
    for (panel, lead, tools), intro in zip(panels, intros):
        cards = '\n'.join(
            '<a class="tool-card" href="%s">%s<span class="c-t">%s</span><span class="c-d">%s</span></a>'
            % (t.url,
               ('<img src="%s" alt="">' % t.icon) if t.icon else '<span class="no-icon"></span>',
               html.escape(t.title), html.escape(TAG_RE.sub('', t.summary)))
            for t in tools)
        groups.append('<h2 id="%s">%s<a class="anchor" href="#%s">#</a></h2>\n<p class="panel-lead">%s</p>\n%s\n<div class="tool-cards">%s</div>'
                      % (slugify(panel), html.escape(panel), slugify(panel), html.escape(lead), intro, cards))
    hero = ('<p class="eyebrow">pyNavis tools guide</p><h1>Every button on the pyNavis tab</h1>'
            '<p class="lead">What each tool does, how to use it, what it will not do, and the '
            'other jobs it turns out to be good for. One page per tool, grouped the way the '
            'ribbon is.</p>')
    body = LANDING_INTRO + '\n'.join(groups)
    return SHELL.format(
        title='Tools guide',
        description='The user manual for every tool on the pyNavis ribbon tab in Navisworks.',
        logo=LOGO,
        sidebar=sidebar(panels, None),
        hero=hero,
        body=body,
        pagenav='',
        toc='<aside class="toc"></aside>',
        version=html.escape(version),
    )


LANDING_INTRO = """
<div class="note">
<span class="note-t">How to read a tool page</span>
<p>Each page opens with the facts: where the tool sits, what it needs before its button is
live, its keyboard shortcut and what Shift+Click does. Then what it does, a walkthrough with
screenshots, the limits worth knowing before you rely on it, and other jobs it is good for.
Alt+Click on any tool opens its folder, since every tool here is an ordinary bundle you can
read and copy.</p>
</div>
"""


def render_index(panels):
    entries = []
    for panel, _lead, tools in panels:
        for tool in tools:
            entries.append({'t': tool.title, 'p': panel, 'u': tool.url,
                            'b': TAG_RE.sub('', tool.summary)})
            for hid, text in tool.headings:
                entries.append({'t': text, 'p': tool.title, 'u': '%s#%s' % (tool.url, hid),
                                'b': section_text(tool, hid)})

    def js(entry):
        return '{t:%s,p:%s,u:%s,b:%s}' % (
            jstr(entry['t']), jstr(entry['p']), jstr(entry['u']), jstr(entry['b']))

    return ('/* Generated by tools/build_tools_site.py - do not edit. */\n'
            'window.PYNAVIS_SEARCH = [\n%s\n];\n' % ',\n'.join(js(e) for e in entries))


def section_text(tool, hid):
    start = tool.body.find('id="%s"' % hid)
    if start < 0:
        return ''
    rest = tool.body[start:]
    nxt = rest.find('<h2', 5)
    chunk = rest if nxt < 0 else rest[:nxt]
    text = TAG_RE.sub(' ', chunk)
    text = re.sub(r'\s+', ' ', html.unescape(text)).strip()
    return text[:400]


def jstr(text):
    out = (text or '').replace('\\', '\\\\').replace('"', '\\"')
    out = out.replace('\n', ' ').replace('\r', ' ').replace('</', '<\\/')
    return '"%s"' % out


def read_version():
    init = os.path.join(ROOT, 'pynavislib', 'pynavis', '__init__.py')
    try:
        with io.open(init, encoding='utf-8') as fh:
            match = re.search(r"__version__\s*=\s*'([^']+)'", fh.read())
        if match:
            return 'pyNavis %s' % match.group(1)
    except IOError:
        pass
    return 'pyNavis'


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #

def build():
    check = '--check' in sys.argv
    if not os.path.isdir(CONTENT):
        sys.exit('No content folder at %s' % CONTENT)
    for folder in (ASSETS, SHOTS, ICONS):
        if not os.path.isdir(folder) and not check:
            os.makedirs(folder)

    version = read_version()
    panels = []
    intros = []
    wanted = []
    for fragment, panel, lead in PANELS:
        intro, tools = read_panel(fragment, panel)
        for tool in tools:
            if not check:
                copy_icon(tool)
            elif os.path.isfile(os.path.join(ICONS, tool.slug + '.png')):
                tool.icon = '_assets/icons/%s.png' % tool.slug
            tool.body = figures(tool.body, tool, wanted)
            headings(tool)
        panels.append((panel, lead, tools))
        intros.append(intro)
    flat = [t for _p, _l, tools in panels for t in tools]

    written = {}
    for tool in flat:
        written[tool.url] = render_tool(panels, flat, tool, version)
    written['index.html'] = render_landing(panels, intros, version)
    written[os.path.join('_assets', 'search-index.js')] = render_index(panels)
    written['SHOTLIST.md'] = shotlist(wanted)
    for shared in ('site.css', 'site.js'):
        with io.open(os.path.join(AUTHORING_ASSETS, shared), encoding='utf-8') as fh:
            written[os.path.join('_assets', shared)] = fh.read()
    with io.open(os.path.join(CONTENT, 'tools.css'), encoding='utf-8') as fh:
        written[os.path.join('_assets', 'tools.css')] = fh.read()
    with io.open(os.path.join(CONTENT, '_headers'), encoding='utf-8') as fh:
        written['_headers'] = fh.read()

    changed = []
    for rel, text in written.items():
        target = os.path.join(SITE, rel)
        old = None
        if os.path.isfile(target):
            with io.open(target, encoding='utf-8') as fh:
                old = fh.read()
        if old != text:
            changed.append(rel)
            if not check:
                with io.open(target, 'w', encoding='utf-8', newline='\n') as fh:
                    fh.write(text)

    if check:
        if changed:
            sys.exit('Out of date, re-run tools/build_tools_site.py:\n  ' + '\n  '.join(sorted(changed)))
        print('Tools guide is up to date (%d tools).' % len(flat))
        return
    print('Built %d tool pages + landing; %d screenshot(s) still to capture.' % (len(flat), len(wanted)))
    if changed:
        print('Updated: %s' % ', '.join(sorted(changed)))


def shotlist(wanted):
    out = ['# Screenshots the tools guide is waiting for', '',
           'Save each as `docs/tools/_assets/shots/<name>.png` (PNG, about 1200 px wide, '
           'light Navisworks theme) and re-run `python tools/build_tools_site.py`. '
           'A page shows a labelled placeholder until its file exists.', '']
    current = None
    for tool, name, caption in wanted:
        if tool is not current:
            out.append('## %s (%s)' % (tool.title, tool.panel))
            out.append('')
            current = tool
        out.append('- [ ] `%s.png`: %s' % (name, caption))
    out.append('')
    return '\n'.join(out)


if __name__ == '__main__':
    build()
