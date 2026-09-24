# pyNavis authoring docs

The tool-authoring guide. Open `index.html` in a browser; it works straight from disk with
no server, no build tooling and no network access.

## Editing

**Never edit a page in this folder.** Every `*.html` here except this README is generated,
the same convention as the ribbon icons. Edit the fragment and re-run the builder:

```
docs/authoring/_content/<page>.html    <- edit this
python tools/build_docs.py             <- then run this
```

The builder supplies the shell (sidebar, contents rail, previous and next links), turns
bare `<pre>` blocks into copyable code cards, applies syntax highlighting, gives every
heading an id and anchor, and regenerates `_assets/search-index.js`.

To add a page, write the fragment and add one line to the `PAGES` list in
`tools/build_docs.py`. Page order there is the reading order and drives the numbering.

## Fragment format

Metadata comments first, then content starting at `<h2>`. The shell supplies the `<h1>`.

```html
<!-- title: Anatomy of an extension -->
<!-- eyebrow: Bundles -->
<!-- lead: One plain sentence describing the page. -->

<h2>First section</h2>
<p>...</p>
```

Markup the builder understands:

| You write | You get |
|---|---|
| `<pre data-lang="python" data-file="script.py">` | A code card with a header and a Copy button |
| `<pre data-bare="true" data-lang="python">` | Highlighted code with no card, for do/don't panels |
| `<h2>` / `<h3>` | An id, an anchor link, a contents-rail entry and a search entry |

Valid `data-lang` values: `python`, `yaml`, `json`, `tree`, `powershell`, `text`.

Available classes: `.note` plus `.tip` / `.warn` / `.trap` for callouts, `.do-dont` for
good and bad pairs, `.table-scroll` around tables, `.sig` for API signatures, `.pill` with
`.pure` / `.needs-nw` / `.blocks` for badges, `.cards` for link grids, `.fig-frame` around
figures.

## Diagrams

Inline SVG inside `<figure><div class="fig-frame">`. Use CSS variables for every colour
(`var(--ink)`, `var(--muted)`, `var(--line)`, `var(--line-strong)`, `var(--frame)`,
`var(--paper)`, `var(--accent)`, `var(--surface)`) so figures work in both themes. The lint
pass rejects hardcoded hex inside an SVG unless the element carries
`data-literal-colors="true"`, which is for palette swatches where the literal value is the
point.

Give each figure's internal `<style>` classes a per-page prefix, because all the CSS on a
page shares one namespace.

## Checking

```
python tools/build_docs.py           build, then report lint warnings and errors
python tools/build_docs.py --check   fail if anything is out of date or broken
```

The lint pass catches:

- unbalanced tags, and `<h1>` in a fragment
- links to pages or anchors that do not exist
- unknown `data-lang` values
- malformed SVG, which otherwise fails silently in the browser
- hardcoded colours in diagrams, which break dark mode
- em or en dashes, which the project style bans
- **any `pynavis.<module>.<name>` in a Python example that does not exist in
  `pynavislib`**, so the docs cannot drift away from the library

That last check is why the examples can be trusted: it parses `pynavislib/pynavis/*.py`
for public names and validates every call and constant used in a `data-lang="python"`
block against them.

## Layout

```
docs/authoring/
    index.html            generated landing page
    <page>.html           generated pages
    _content/*.html       the sources you edit
    _assets/site.css      shared stylesheet, hand-maintained
    _assets/site.js       search, contents rail, copy buttons, theme toggle
    _assets/search-index.js   generated
```
