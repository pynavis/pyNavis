# Writing pyNavis tools

pyNavis turns folders on disk into ribbon buttons inside Autodesk Navisworks. A folder
named `Thing.pushbutton` containing `script.py` is a working command: no manifest, no
registration, no build step. Scripts run on IronPython 3.4 (Python 3 syntax, .NET
interop through `clr`) on the Navisworks UI thread, with the `pynavis` library on the
path and the Navisworks .NET API available as `Autodesk.Navisworks.Api`.

## A pushbutton bundle

```
<Name>.pushbutton/
    bundle.yaml     title, tooltip, optional shortcut, keytip, engine, context
    script.py       runs on click; top-level code, no __main__ guard
    config.py       optional; Shift+Click runs it (settings for the tool)
    icon.png ...    pyNavis generates these for AI-made tools
```

`bundle.yaml` is not real YAML: one `key: value` per line, the first colon splits the
line, everything after it is the value, indentation is meaningless.

```yaml
title: Isolate Level
tooltip: Hides everything that is not on the selected items' level.
shortcut: Ctrl+Shift+L        # optional; needs Ctrl or Alt
context: selection            # optional; greys the button when false
```

`context:` is a boolean expression over `doc`, `selection`, `clash-tests`,
`clash-results`, `viewpoints`, `selection-sets` and `multi-model`, combined with `&`,
`|` and `!` (`&` binds tighter, no parentheses).

## The script contract

- `script.py` runs top to bottom on every click. Put the work at top level or in a
  function you call at the end. `if __name__ == '__main__':` never runs in pyNavis.
- Globals available: `__file__`, `__commandpath__` (the bundle folder), `__title__`.
- Everything the person sees goes through `pynavis.toast` (one line), `pynavis.forms`
  (a question), `pynavis.banner` (one result on a bar) or `pynavis.output` (a table or
  report). `print()` opens an output window: never use it for status.
- The Navisworks API is not thread-safe. No threads, no async, no timers touching the
  model.
- Wrap bulk edits in one undo step (`pynavis.viewpoints.transaction` or the module's own
  batching) so the person gets one Ctrl+Z, not thousands.
- Every outcome reports: success, failure and "nothing to do" all toast. Cancelling a
  dialog is the one silent exit.
- No em dashes in any user-facing text. Sentence case, capitalised first letter.

## Rules and traps

### Hard rules

These are project rules, not preferences. Violating them produces tools that look broken.

- **Never `print()` for status.** The output window is created lazily on first write, so
  a bare `print()` opens a window containing one line. Use `pynavis.toast` for one-line
  results. Use `pynavis.output` only when there is genuinely a table or a report to show.
- **Never put work inside `if __name__ == '__main__':`.** It never runs. Measured:
  `__name__` is `'builtins'` on IronPython and `''` on CPython, never `'__main__'`. The
  script silently does nothing and reports no error.
- **Cancelling is silent.** `forms.ask_string`, `save_file` and `open_file` return `None`
  on cancel; `forms.confirm` returns `False`. Exit without a toast. Never say "cancelled".
- **Report every outcome.** A tool that finishes with no visible result reads as broken.
  Success, failure, and "nothing to do" all get a toast.
- **One undo step per user action.** Wrap bulk edits in `pynavis.viewpoints.transaction`,
  or the module's own batching. Thousands of undo entries is a bug.
- **No threads touching the model.** The Navisworks API is not thread-safe and your script
  runs on the UI thread.
- **Never launch Navisworks yourself.** Ask the user to click. Verify from the log instead.
- **No em dashes** in any user-facing string, and none in these docs either.

### Splitting pure logic from API calls

The library's own modules do this and so should any tool of real size: a pure half that
takes plain dicts and lists and is unit-testable outside Navisworks, plus a thin API half
that reads the document and calls it. `clashgroup` is entirely pure; `section` and
`viewpoints` each expose a pure planner (`fit`, `plan_renames`) beside an API applier.

Put the pure half in the bundle folder or in `<extension>/lib/` and import it. Both are on
the module search path and both are re-read on every run.

### What needs what to take effect

| Changed | Needed |
|---|---|
| `script.py`, `config.py`, bundle-local modules, `<extension>/lib/` modules, files already inside an existing `*.lib` folder | Nothing, just run it again |
| `bundle.yaml`, icons, folder names, shortcuts, new bundles, `hooks/*.py`, `startup.py` | Click Reload |
| `pynavislib/` | Click Reload |
| A new `*.dockpane` claiming one of the free panel slots | Click Reload |
| More panel slots (the Panel slots button, `pynavis.panes.add_slots`) | Restart Navisworks |
| A `*.lib` folder added or removed | Restart Navisworks |
| C# under `src/` | Rebuild and restart Navisworks |

### Failure modes that are silent

Check these first when a bundle does not appear or does nothing:

- A `.pushbutton` with no `script.py` is dropped with no message.
- A `.stack` needs exactly 2 or 3 pushbuttons; otherwise it is skipped.
- A `.pulldown` with no usable pushbuttons is skipped.
- `.extension` folders are found only directly under a root, never nested deeper.
- A tool in the wrong place is missing from its container's `layout:` list, or the entry
  does not match the folder name. Unlisted folders go last, in name order. A misspelt
  entry is reported in the log and the problem list.
- A single-digit prefix is not stripped from the title: `1_Keep` displays as "1 Keep".
- A bad `engine:` value fails at click time, not load time.
- Icon variants fall back to `icon.png` only. Shipping only `icon.dark.png` gives you
  no icon at all.
- A hook that fails 3 times in a row is silently disabled until Reload; the log has every
  traceback, the toast only fires once, on the 3rd failure.
- A `context:` button that never enables usually has a misspelled condition name (logged) or
  a rule that fails to parse (also logged, and the button is left always-enabled, not
  always-disabled).
- `pynavis.script.get_toggle_state`/`set_toggle_state` only work called from that toggle's own
  `script.py`/`config.py`; a hook, `startup.py`, or a smartbutton's `__selfinit__` run cannot
  target a specific toggle.
- A `.dockpane` with no `pane.xaml` is dropped (logged); its `script.py` is optional, the
  reverse of a pushbutton.
- A dockpane toggle that toasts "No panel slot free" lost the scramble for a slot: add slots
  with Panel slots, then restart Navisworks. "Panel slot ready after restart" is not an
  error, just the restart that is already pending.
- `shortcut:` on a `*.dockpane` is parsed but bound to nothing in this release; `keytip:`
  works normally.
- A new `*.lib` folder needs a Navisworks restart, not a Reload, before anything can import
  from it.

the sections below has the full list with the log line for each.

## Templates
Copy-paste starting points. Every one of these runs as written. Replace the parts in
angle brackets and delete what you do not need.

### Minimal script.py

The smallest useful tool: act on the selection, report the outcome, handle the empty case.

```python
"""<One sentence. This becomes the tooltip when there is no bundle.yaml.>"""
from pynavis import selection, toast

items = selection.get_items()

if not items:
    toast.info('Nothing selected', 'Select something in the model and run this again.')
else:
    # <do the work>
    toast.success('<Verb>ed %d item(s)' % len(items))
```

Top-level code only. Do not wrap this in `if __name__ == '__main__':`, which never runs.

### script.py with a file export

Note the silent exit on cancel: no toast, because cancelling is not an error.

```python
"""Exports the current selection to CSV."""
import csv

from pynavis import forms, selection, toast

items = selection.get_items()

if not items:
    toast.info('Nothing selected', 'Select something first.')
else:
    path = forms.save_file(default_name='selection.csv')
    if path is not None:                       # None means cancelled: say nothing
        try:
            with open(path, 'wb') as handle:
                writer = csv.writer(handle)
                writer.writerow(['Name', 'Type'])
                for item in items:
                    writer.writerow([item.DisplayName, item.ClassDisplayName])
        except (IOError, OSError) as ex:
            toast.error('Could not write the file', str(ex))
        else:
            toast.success('Exported %d row(s)' % len(items), path)
```

### script.py with settings

Pairs with the `config.py` below. `TOOL` and `DEFAULTS` are module constants so both
files agree on them; for a bigger tool put them in a shared module in the bundle folder.

```python
"""Reports the selection size and warns when it is unusually large."""
from pynavis import selection, settings, toast

TOOL = '<tool_key>'
DEFAULTS = {'warn_above': 1000}

values = settings.load(TOOL, DEFAULTS)         # never raises; bad file yields defaults
items = selection.get_items()

if not items:
    toast.info('Nothing selected', 'Select something first.')
elif len(items) > values['warn_above']:
    toast.warning('%d item(s) selected' % len(items), 'That is a large selection.')
else:
    toast.success('%d item(s) selected' % len(items))
```

### config.py (the Shift+Click secondary action)

```python
"""Options for <Tool name>."""
from pynavis import forms, settings, toast

TOOL = '<tool_key>'
DEFAULTS = {'warn_above': 1000}

values = settings.load(TOOL, DEFAULTS)
answer = forms.ask_string(
    'Warn when the selection is larger than:',
    default=str(values['warn_above']),
    title='<Tool name>',
)

if answer is not None:                         # None means cancelled: change nothing
    try:
        values['warn_above'] = int(answer)
    except ValueError:
        toast.error('That is not a whole number')
    else:
        settings.save(TOOL, values)
        toast.success('Saved', 'Warning above %d items.' % values['warn_above'])
```

### script.py with progress over a long loop

Counted progress, not indeterminate, because the total is known.

```python
"""<What it does.>"""
from pynavis import output, selection, toast

items = selection.get_items()

if not items:
    toast.info('Nothing selected', 'Select something first.')
else:
    total = len(items)
    rows = []
    for index, item in enumerate(items):
        rows.append([item.DisplayName, item.ClassDisplayName])
        if index % 100 == 0:
            output.progress(float(index) / total, 'Reading %d of %d' % (index, total))
    output.progress(1.0)

    output.print_table(rows, ['Name', 'Type'])
    toast.success('Listed %d item(s)' % total)
```

`output.progress` and `output.print_table` both write to the output window, so the window
opens. That is correct here, because there is a table to show. If the tool only produced a
count, it should toast and open nothing.

### script.py with a bulk edit in one undo step

```python
"""Renames the checked saved viewpoints."""
from pynavis import toast, viewpoints

rows = viewpoints.snapshot()
checked = [r['key'] for r in rows if not r['is_folder']]

plan = viewpoints.plan_renames(rows, checked, {
    'type': 'replace', 'find': '<old>', 'replace': '<new>', 'regex': False,
})

if plan['problems']:
    toast.error('Cannot rename', plan['problems'][0])
elif plan['collisions']:
    toast.error('Name collision', plan['collisions'][0])
elif not plan['renames']:
    toast.info('Nothing to rename', 'No viewpoint matched.')
else:
    applied, errors = viewpoints.apply_renames(plan['renames'])
    if errors:
        toast.warning('Renamed %d, %d failed' % (applied, len(errors)), errors[0])
    else:
        toast.success('Renamed %d viewpoint(s)' % applied)
```

`apply_renames` wraps itself in a transaction, so this is one undo entry. Edits resolve by
GUID, never by index path, because index paths go stale the moment anything moves.

### Splitting pure logic from the API

Put the pure half in the bundle folder or `<extension>/lib/`, import it from `script.py`.
Both locations are on the module search path and both are re-read on every run.

`rules.py`, testable with plain CPython outside Navisworks:

```python
"""Pure grouping rules. No Navisworks import, so this is unit-testable anywhere."""

def group_by_prefix(names, separator='-'):
    """Groups names by their leading segment. Returns {prefix: [name, ...]}."""
    groups = {}
    for name in names:
        prefix = name.split(separator)[0].strip() or '(none)'
        groups.setdefault(prefix, []).append(name)
    return groups
```

`script.py`, the thin API half:

```python
"""Groups the selection by name prefix and reports the counts."""
from pynavis import output, selection, toast

import rules                                   # bundle-local, re-read every run

items = selection.get_items()

if not items:
    toast.info('Nothing selected', 'Select something first.')
else:
    groups = rules.group_by_prefix([i.DisplayName for i in items])
    output.print_table(
        sorted([[prefix, len(names)] for prefix, names in groups.items()]),
        ['Prefix', 'Count'],
    )
    toast.success('%d group(s)' % len(groups))
```

### bundle.yaml

Eight keys are read on a pushbutton-shaped bundle. Anything else is parsed and ignored.

```yaml
title: <Button caption>          # a literal \n breaks a long caption onto two lines
tooltip: <One or two sentences. The shortcut is appended automatically.>
shortcut: Ctrl+Shift+<K>
keytip: <XY>
engine: ironpython
context: selection & clash-tests    # optional: & | !, & binds tighter, no parens
min_host_version: 2024              # optional: bare year, either or both
max_host_version: 2026
```

Remember it is not real YAML: a trailing `# comment` is kept as part of the value,
indentation is meaningless, the first colon splits the line, and duplicate keys mean the
last one wins.

A pulldown, splitbutton or splitpushbutton reads only `title`, `tooltip` and `keytip`. A
`.stack` folder reads no yaml at all. A urlbutton reads `title`, `tooltip`, `keytip` and
requires `url`; a linkbutton reads the same three and requires `plugin` (`Id.DeveloperId`
form). A dockpane reads `title`, `tooltip`, `engine` and `keytip`, and requires `pane.xaml`
in the folder rather than any yaml key; `shortcut:` on a dockpane is parsed but bound to
nothing in this release.

### extension.yaml

```yaml
name: <Extension name>
engine: ironpython
```

Two keys, no more. `engine` here becomes the default for every pushbutton in the
extension, and each bundle can still override it.

### Guarded import

Use this when a script must still do something useful outside a live Navisworks session,
for example so a dialog can open while a model is loading. `app`, `doc`, `selection` and
`clash` import the Navisworks API at module load and raise `ImportError` without it.

```python
try:
    from pynavis import clash
except ImportError:
    clash = None
```

### hooks/\<event\>.py

Filename minus `.py` must be one of the fourteen exact event names (see
the sections below), and note the three viewpoint ones are easy to confuse:
`viewpoints-changed` (tree edited), `viewpoint-recalled` (saved view activated),
`camera-moved` (view moved, debounced). Three globals, not four: no `__commandpath__`, no
`__title__`. Output goes to the log, never a window, so use `toast` for anything the user
should see.

```python
from pynavis import doc, toast

toast.info('Opened ' + (doc.get_title() or 'untitled'))
```

Keep it fast: hooks run synchronously on the UI thread with a 500ms soft budget (over-budget
runs are logged, not killed), and 3 consecutive failures disable the hook until Reload.

### startup.py

One per extension, at the extension root, beside `extension.yaml`. Runs at boot and every
Reload, before that extension's ribbon builds. Same two globals as a hook minus `__event__`.
A raised exception is logged and toasted, but the extension's buttons still load.

```python
"""Confirms a shared helper resolved before anyone clicks a button."""
from pynavis import toast

try:
    import acme_common
except ImportError:
    toast.warning('acme_common not found', 'Check the Shared.lib folder is installed.')
```

### \*.smartbutton script.py

Runs twice: once at ribbon build with `__selfinit__` True and a `__button__` proxy, once per
click with `__selfinit__` False and no `__button__` at all. Always guard on `__selfinit__`.

```python
if __selfinit__:
    __button__.Title = 'Clash (0)'
    __button__.Enabled = True
else:
    from pynavis import toast
    toast.info('Clicked!')
```

### \*.toggle script.py

Ships `icon.on(.dark).png` / `icon.off(.dark).png` beside the usual pair. `icon.off.png` falls
back to `icon.png`, and `icon.off.dark.png` falls back to `icon.off.png` first, then `icon.png`
(the `.on` pair follows the same chain, not an independent fallback to `icon.png`). State is
session-scoped and keyed to this bundle only -
`get_toggle_state`/`set_toggle_state` do not work called from anywhere else.

```python
"""Turns grid snapping on and off."""
from pynavis import script, toast

on = not script.get_toggle_state()
script.set_toggle_state(on)
toast.info('Snap ' + ('on' if on else 'off'))
```

### \*.dockpane bundle

A dock panel, not a script that runs and returns. `pane.xaml` is required and its root is a
plain element (a `Grid` or `StackPanel`, never a `Window`); `script.py` is optional and runs
once, when the panel's content is built, with `__pane__` on top of the usual globals. The
ribbon button is a toggle that follows the panel's visibility; `icon.on(.dark).png` beside
the standard four is the pressed art while the panel is open.

```
<NN>_<Name>.dockpane/
    pane.xaml             # required; plain element root
    script.py             # optional; runs once at content build, gets __pane__
    bundle.yaml           # optional; title, tooltip, engine, keytip
    icon.png              # 96x96, plus the dark and small variants as usual
    icon.on.png           # pressed state while the panel is open
    icon.on.dark.png
```

`pane.xaml`:

```xml
<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Margin="16">
  <TextBlock x:Name="Headline" FontSize="16" FontWeight="SemiBold" Text="Panel title" />
  <TextBlock x:Name="Status" FontSize="11.5" Margin="0,4,0,0" Text="Ready" />
  <Button    x:Name="Close" Margin="0,12,0,0" Padding="8,4,8,4" Content="Hide this panel" />
</StackPanel>
```

`script.py`:

```python
"""<What the panel shows.>"""
import datetime

pane = __pane__

pane.Find('Status').Text = 'Built at ' + datetime.datetime.now().strftime('%H:%M:%S')

def on_close(sender, args):
    pane.Visible = False

pane.Find('Close').Click += on_close
```

Handlers wired here fire long after this run finished, so inside them act through `__pane__`
(held in the `pane` variable) or pass a bundle key to `pynavis.panes` explicitly; the
no-argument `panes.show()`/`hide()`/`toggle()` target whichever command ran last, not this
panel.

### Folder layout for a finished bundle

```
<NN>_<Name>.pushbutton/
    script.py             # required; primary action
    config.py             # optional; Shift+Click
    bundle.yaml           # optional; title, tooltip, shortcut, keytip, engine
    rules.py              # optional; bundle-local pure module
    icon.png              # 96x96
    icon.dark.png         # 96x96
    icon.small.png        # 32x32
    icon.small.dark.png   # 32x32
```

## Design rules
The full design system is `docs/design-system/` (v2.2). This is the subset that applies
when you are writing a bundle script, plus the behavioural rules that decide whether a
tool feels finished.

### Choosing a surface

Pick the lightest surface that carries the information. Most tools need only the first.

| Surface | Use it for | Blocks? |
|---|---|---|
| `toast` | The result of a finished action, one line | No |
| `forms` | A question you must have answered to continue | Yes |
| `output` | Results too big for a line: tables, lists, links, progress | No |
| A `*.dockpane` panel | Live state the user keeps docked and glances at while working | No |
| Custom WPF dialog | A real tool with many settings; built on `src/PyNavis.Runtime/Forms/DesignSystem.cs` | Yes |

Rules that follow from this:

- **Never open a window to say one line.** `print()` opens the output window; a bare
  `print('Done')` gives the user a window containing "Done". Toast it instead.
- **Never toast a table.** A toast is one line plus an optional muted detail line.
- **Never ask a question in a toast.** Toasts carry no buttons and dismiss themselves.

### Toast levels

```python
toast.success(message, detail=None)   # green   the thing worked
toast.error(message, detail=None)     # red     the thing failed
toast.info(message, detail=None)      # blue    nothing to do, or neutral fact
toast.warning(message, detail=None)   # amber   worked, but with a caveat
toast.show(level, message, detail=None)
```

Several library functions return a `Result` with exactly matching fields, so the canonical
line is:

```python
result = memory.memorize()
toast.show(result.level, result.message, result.detail)
```

Note there are three separate `Result` classes, `memory.Result`, `section.Result` and
`viewstate.Result`, with identical shape. Do not try to import one from another.

### Behavioural rules

**Report every outcome.** Success, failure, and "nothing to do" all produce a toast. A
tool that finishes silently reads as broken, and the user will click it again.

**Cancelling is silent.** `ask_string`, `save_file` and `open_file` return `None` on
cancel; `confirm` returns `False`. Exit with no message. Never toast "cancelled".

Test for `None` explicitly, because an empty string is a different answer:

```python
## right
if path is not None:
    ...

## wrong: an empty string from ask_string is a real answer, not a cancel
if path:
    ...
```

**Empty states are real states.** "Nothing selected" gets a toast that says what to do
about it, not a silent success.

**Handle predictable failures, let genuine bugs raise.** A missing file or a bad number is
a toast with a plain sentence. A programming error should raise, so the traceback lands in
the output window where it can be read.

**One undo step per user action.** Wrap bulk edits in `viewpoints.transaction(name)` or
use a module that batches for you. Note that `viewpoints.apply_sort` is deliberately not
wrapped, so each move is its own undo step.

**Counted progress, not indeterminate,** whenever the total is known. Offer cancellation
for anything that can run more than a few seconds. Cancellation conventions differ by
module: `clash.snapshot_results` stops when the progress callback returns exactly `False`
and then raises `clash.Cancelled`, while `viewpoints.apply_*` take a separate `cancelled()`
predicate and just stop.

**Speak the user's language.** Say what they see in Navisworks, not the API type name.
"3 viewpoints" not "3 SavedItems".

### Copy rules

- Sentence case everywhere. Not Title Case, not ALL CAPS.
- **No em dashes or en dashes.** Use a comma, a colon, or two sentences.
- No emoji in any UI string.
- Plain verbs: "Export", "Group", "Rename". Not "Let's export!".
- Numbers get thousands separators when they can get large.
- Name the thing that failed and what to do next, in one sentence.

### Visual rules, if you write your own dialog

Read `docs/design-system/` properly before building a dialog. The short version:

- The Windows accent colour appears in exactly three places: the primary action, the
  selection rail, and the focus ring. Nowhere else.
- Selection background is a neutral wash, never an accent tint.
- Solid surfaces only. No gradients, no glass, no translucency, no alpha-tinted fills.
- Hairlines and whitespace carry structure, not shadows. The single exception is a popup
  floating over its own window.
- Native affordances: real checkboxes, real toggles, a segmented control for 2 to 4
  exclusive modes.
- 32px control height, 32px list row height, 4px corner radius, 8px control gap, 24px
  section gap. 24px page padding for prose and settings windows, 16px for data-dense ones.
- Segoe UI Variable; 13px body, 11.5px floor. Consolas for script output.
- Numbers are quiet tabular text, not coloured chips. The only chip in the system is a
  status tag, because status is categorical.
- Read `pynavis.script.is_dark_theme()` and theme accordingly.

Banned outright, from the design system's "slop test": gradients, glassmorphism,
accent-alpha washes, pill soup, badge marketing, emoji, hero cards for a two-way choice,
centered content in rows and forms, hover-only affordances, decorative motion,
opacity-as-disabled, mouse-only controls, a control per row instead of virtualising,
blank waiting states, and hand-drawn OS chrome.

### The HTML output window

The one CSS surface in the project. Its custom properties are emitted server-side per
theme, so use the real emitted names in custom markup:

```
--bg  --fg  --err  --surface  --line  --frame  --muted  --row-hover  --accent
--err-wash  --err-line  --chart-1 .. --chart-6
```

Classes you can target: `.pynavis-card`, `.pynavis-card.err`, `pre.pynavis-text`,
`pre.codeblock`, `table.pynavis` (with `td.num` / `th.num`), `a.pynavis-el`,
`#pynavis-progress`.

Notes:

- `print_md` HTML-escapes its input before converting, so raw markup in your data can
  never become live HTML.
- `table_html` decides numeric alignment from the data, not the column position: a column
  is numeric when every non-empty cell parses as a float after stripping commas.
- `element_link(item, label)` returns a string; embed it yourself with `print_html`.
- Charts go through `output.chart_bar`, `chart_line`, `chart_pie` and `chart_doughnut`:
  theme-aware SVG cards whose series colours are `var(--chart-N)`. Hand-write markup into
  `print_html` only for a shape those four do not cover.
- The window degrades to plain text without the WebView2 runtime. Never branch on this.

### Icons

Four PNGs per bundle: `icon.png` and `icon.dark.png` at 96x96, `icon.small.png` and
`icon.small.dark.png` at 32x32. Every variant falls back to `icon.png` only, so shipping
just a dark variant gives you nothing. A `*.toggle` adds `icon.on(.dark).png` and
`icon.off(.dark).png` for its two states; a `*.dockpane` adds `icon.on(.dark).png` as the
pressed art while its panel is open. All of it comes from the same generator.

Generated by `tools/make_icons.py`, never hand-edited. Register the bundle path in its
`ICONS` dict with a draw function and re-run it. The drawing language: neutral ink draws
the thing, a solid accent mark draws the action, so colour always means "what this tool
does". Small glyphs are separate simplified drawings with a heavier relative stroke, not
resized copies. Alert red appears exactly once in the whole shipped set, on Purge, the only
destructive tool. Do not add a second.

## The pynavis library
Every signature here was read from `pynavislib/pynavis/*.py`. Defaults are transcribed
literally.

### Module index

| Module | Purpose | Import-time requirement |
|---|---|---|
| `clashgroup` | Pure clash-grouping engine: rules, clustering, naming | None (stdlib only) |
| `clashtest` | Create/edit/run/delete clash tests, batch status writes | None (stdlib only); calls need Navisworks |
| `export` | Save/publish NWD, best-effort viewpoint image | None (stdlib only); calls need Navisworks |
| `memory` | Selection memory register, pure half plus API half | None (stdlib only) |
| `output` | Output-window HTML, tables, charts, images, progress | None (stdlib only); calls need the runtime |
| `props` | Read item properties as plain values, write custom tabs | None (stdlib only); calls need Navisworks |
| `geometry` | World-space triangles of a model item via the COM primitives walk | **Live Navisworks** |
| `section` | Oriented section-plane fitting, pure half plus API half | None (stdlib only) |
| `separation` | How far one mesh must slide along an axis to stop touching another | None (stdlib only) |
| `sets` | Fluent query builder, run queries, read/write search sets | None (stdlib only); calls need Navisworks |
| `settings` | Per-tool JSON settings under `%APPDATA%` | None (stdlib only) |
| `view` | Zoom onto the selection, isolate items, unhide everything | None (stdlib only); calls need Navisworks |
| `viewpoints` | Saved-viewpoint rename/delete/sort/move, pure half plus API half | None (stdlib only) |
| `viewstate` | Copy/paste section, hidden and override state per document, pure half plus API half | None (stdlib only) |
| `xl` | Read/write `.xlsx` workbooks, no Excel required | None (stdlib only) |
| `forms` | Modal dialogs, pickers, cancellable progress, XAML windows | `PyNavis.Runtime` only |
| `overlay` | Draw labelled world-space lines and dimensions in the 3D view | `PyNavis.Runtime` only; drawing needs Navisworks |
| `pick` | Ask the user to click a point in the view, with measure-tool snapping | `PyNavis.Runtime` only; picking needs Navisworks |
| `panes` | Show/hide dock panels, slot counts, generate more slots | `PyNavis.Runtime` only; calls need Navisworks |
| `script` | Running-command context, session vars, per-command settings/data, logger, theme, reload | `PyNavis.Runtime` only |
| `toast` | Corner status toasts | `PyNavis.Runtime` only |
| `banner` | One result on a full-width bar along the bottom of the window (same calls as toast, plus `prompt()` that stays and `clear()`); for the one value a tool exists to produce or the instruction a pick is waiting on, never routine status | `PyNavis.Runtime` only |
| `faces` | Faces under a measured point from the item's own triangles, the parallel pair two points share, `side_of` for which way is into a body; vector helpers on 3-tuples | `PyNavis.Runtime` only; `faces_under` needs Navisworks |
| `app` | Application, documents, GUI, API version | **Live Navisworks** |
| `clash` | Clash Detective tests, results, grouping writes | **Live Navisworks** |
| `doc` | Active-document metadata, saved viewpoints, properties | **Live Navisworks** |
| `selection` | Read, replace, clear the current selection | **Live Navisworks** |

`PyNavis.Runtime` is always loaded wherever a pyNavis script runs, so that tier never fails
in a bundle. The **live Navisworks** tier imports `pynavis._api` at module load; outside a
session that raises:

```
ImportError: pynavis: could not load the Navisworks .NET API (Autodesk.Navisworks.Api).
This module only works inside a Navisworks session running pyNavis.
```

Use a guarded import when a tool must still open a dialog without a model:

```python
from pynavis import forms, settings, toast
try:
    from pynavis import clash
except ImportError:
    clash = None
```

`import pynavis` exposes exactly one public name, `pynavis.__version__`, the release number as a string (the same number Settings and the installer show).
There are no re-exports, no `__all__`, no automatic submodule import. `pynavis.clash` after a
bare `import pynavis` is an `AttributeError`. House style, used by every shipped bundle, is
`from pynavis import ...` with modules in alphabetical order, at module level, immediately
after the docstring:

```python
from pynavis import forms, output, toast
```

**Off limits:** `_api`, `_charts`, `_clashapply`, `_clashsnap`, `_com`, `_datafiles`,
`_history`, `_log`, `_markdown`, `_query`, `_repl`, `_util`, `_xlsx`. They are implementation
and change without notice. `_query` is the pure query model (`Condition`, `Query`, `Builder`)
behind `sets.query()`; you hold a `Builder` the moment you call it, but never import `_query`
by name.

### Cross-cutting conventions

**`doc=None` means the active document.** Every function taking `doc` resolves `None` to
`Application.ActiveDocument` (or `pynavis.doc.get_doc()`, the same thing). Pass a document
only when you genuinely have a non-active one.

**There are THREE Result classes, and they are not interchangeable:** `memory.Result`,
`section.Result` and `viewstate.Result`. Identical shape, separate classes, no shared base:

```python
Result(level, message, detail='', count=0)
##   .level    'success' | 'error' | 'info' | 'warning'
##   .message  str
##   .detail   str
##   .count    int
```

Both map straight onto a toast, which is the whole point:

```python
result = memory.memorize()
toast.show(result.level, result.message, result.detail)
```

Never `isinstance` one module's result against another module's class, and never import one
from another module.

**Cancellation conventions differ per module.**

| Function | Mechanism | On cancel |
|---|---|---|
| `clash.snapshot_results` | the `progress` callback returns exactly `False` | **raises `clash.Cancelled`**; discard the partial read |
| `viewpoints.apply_renames`, `viewpoints.apply_deletes` | a separate `cancelled()` predicate returning truthy | breaks the loop, returns `(applied_so_far, errors)`, no exception |
| everything else | none | not cancellable |

`clash.snapshot_results` tests `progress(n) == False`, not `not progress(n)` and not
`progress(n) is False`. A callback written for side effects returns `None`, and
`None == False` is `False`, so it carries on. Interop booleans compare equal but are not
always the `False` singleton, which rules out `is False`.

**Four progress callback shapes.**

| Function | Callback shape | Cadence |
|---|---|---|
| `clash.snapshot_results(progress=...)` | `progress(results_read_so_far)` | every 500 results, plus once at the end |
| `clash.apply_plan(progress=...)`, `clash.ungroup(progress=...)` | `progress(fraction_0_to_1)` | roughly every 500 results, plus `1.0` at the end. Return value ignored |
| `viewpoints.apply_renames(progress=...)`, `viewpoints.apply_deletes(progress=...)` | `progress(done, total)` | every 200 items, plus a final `(total, total)` |
| `props.set_custom(progress=...)`, `props.remove_custom(progress=...)`, `sets.import_dict(progress=...)` | `progress(done, total)` | after every item (props, 1-based) or every entry (import_dict) |
| `output.progress(fraction, label='')` | not a callback, a sink you call | whenever you like |

**Error reporting styles, by module.**

| Style | Where |
|---|---|
| Returns a `Result` describing the failure | `memory.*` action functions, `section.fit_to_selection` / `clear` / `plan_to_selection` |
| Returns `(count, errors_list_of_str)` | `viewpoints.apply_renames`, `apply_deletes`, `apply_sort`, `apply_moves` |
| Returns a plan dict with `problems` and `collisions` lists | `viewpoints.plan_renames` |
| Raises | `clash.Cancelled`, `clash` `ValueError` on a test not in the tree, `clashgroup` `ValueError` on an unknown rule id, `memory.MemoryFormatError` on a corrupt memory file, `viewpoints` `LookupError` from `_index_in`, `ImportError` from the live-Navisworks tier |
| Never raises, never reports | `settings.load` |

`ProgressScope` is **not** a `pynavis` module. It is the C# type
`PyNavis.Runtime.Forms.ProgressScope`, imported directly:

```python
from PyNavis.Runtime.Forms import ProgressScope
scope = ProgressScope.Begin('Grouping clashes', 'Starting')   # modal-ish: disables the host window
try:
    scope.Report(0.5, '5,000 of 10,000')                      # pumps the UI at most every UiPump.IntervalMs
finally:
    scope.Dispose()                                           # re-enables the host window
```

Always dispose in a `finally`. `ScriptExecutor` calls `ProgressScope.CloseAll()` in its own
`finally` as a backstop, but a live scope means an unclickable Navisworks until then.

### app

Import raises `ImportError` outside a live Navisworks session.

```python
app.get_doc()           # -> Document.  Application.ActiveDocument. Never None while
                        #    Navisworks runs; may be an empty document.
app.get_main_doc()      # -> Document.  Application.MainDocument.
app.get_gui()           # -> GuiApplication. Main window handle etc.
app.get_api_version()   # -> str, e.g. '23.0.x.x'. Version of the loaded
                        #    Autodesk.Navisworks.Api assembly.
```

None of these block. None raise on their own.

### clash

Import raises `ImportError` outside a live Navisworks session. It also loads
`Autodesk.Navisworks.Clash` and calls `clr.ImportExtensions`, which is what binds
`Document.GetClash()`.

Reading never touches the document. Writing rebuilds the test on a detached copy and commits
it in a single `TestsReplaceWithCopy`.

```python
clash.Cancelled                      # Exception subclass
clash.GROUP_MARKER                   # 'Created by pyNavis Smart Clash Grouper'

clash.get_clash(doc=None)            # -> DocumentClash
clash.walk_tests(doc=None)           # -> generator of ClashTest, test folders flattened
clash.walk_results(test)             # -> generator of ClashResult, result groups flattened
clash.units_to_meters(doc=None)      # -> float, 1.0 for unknown units
clash.has_grid(doc=None)             # -> bool, True when doc.Grids.ActiveSystem is not None
clash.count_results(test)            # -> (total_leaf_results, how_many_sit_inside_a_group)
clash.summarize(doc=None)            # -> [{'name': str, 'total': int, 'by_status': {str: int}}]

clash.snapshot_results(test, doc=None, progress=None)
    # -> list of plain dicts, one per leaf ClashResult, in walk order. Read-only.
    # Raises clash.Cancelled when progress(n) == False.

clash.apply_plan(test, plan, doc=None, keep_existing=True, progress=None)
    # -> None. One document edit.

clash.ungroup(test, doc=None, ours_only=True, progress=None)
    # -> int, how many top-level groups were dissolved. 0 means nothing matched
    #    and no edit was made.
```

Snapshot row keys, exactly:

| Key | Type |
|---|---|
| `id` | `int`, stable index in the test's walk order |
| `name` | `str`, clash display name |
| `center` | `(x, y, z)` in model units, or `None` |
| `item_a`, `item_b` | `str`, stable element key (`''` if unknown) |
| `item_a_name`, `item_b_name` | `str`, element display name |
| `level` | `str` or `None`, nearest level display name |
| `grid` | `str` or `None`, nearest grid intersection display name |
| `model_a`, `model_b` | `str`, source file name without path (`''` if unknown) |
| `status` | `str`, e.g. `'New'`, `'Active'`, `'Resolved'` |
| `assigned` | `str`, `''` when unassigned |
| `group` | `str` or `None`, name of the existing group holding it |

These are exactly the keys `clashgroup` consumes. Feed one straight into the other.

**Memoisation, and the accuracy cost you are buying.** Element name/model/source-file reads
are memoised per element identity (guid first, instance hash as fallback). Grid and level
lookups (`ClosestIntersection`, the single most expensive call in the read) are memoised per
**0.25 m cell**, converted to model units via `units_to_meters`. A clash within 0.25 m of a
level or grid boundary can therefore be attributed to the neighbouring level or grid. This is
not display-only: it moves the `level` and `grid` grouping rules and the Smart cluster names.
A document with no grid system skips the lookup entirely.

**TRAP: `apply_plan` and `ungroup` leave the passed-in `test` wrapper dead.** The commit is
`TestsReplaceWithCopy`, which swaps the live test for the rebuilt copy, so the old wrapper is
disposed. Read `test.DisplayName` and anything else you still need **before** the call:

```python
name = test.DisplayName                 # read BEFORE, the wrapper dies in apply_plan
clash.apply_plan(test, plan, progress=lambda f: scope.Report(f, name))
toast.success('Grouped %s' % name)      # test.DisplayName here would fail
```

`apply_plan` raises `ValueError('clash test %r is not in the tests tree')` when the test
cannot be located in `TestsData`.

`plan` ids refer to `snapshot_results` walk order **of this same test**; `_read_tree` assigns
leaf ids in that identical depth-first order, which is what makes the ids mean the same thing
on both sides. With `keep_existing=False` planned leftovers land back at the test root and
pre-existing groups dissolve. Individual clashes are never renamed. New groups carry
`GROUP_MARKER` as a comment, which is how `ungroup(ours_only=True)` tells them from hand-made
groups. Only top-level groups are considered by `ungroup`; a dissolved group's nested
subgroups dissolve with it.

Both writes block for as long as the rebuild takes (about 12 seconds on a 350,000-clash
model). Do not reach for the per-edit API methods: `TestsMove` measured about 1130 ms in and
3020 ms out per clash.

### clashgroup

**Entirely pure.** No Navisworks import anywhere, so it unit-tests with plain CPython. Import
never fails.

```python
clashgroup.RULES
    # [(id, label)] in display order:
    #   ('item',      'Root-cause element (auto side)')
    #   ('item_a',    'Element (side A)')
    #   ('item_b',    'Element (side B)')
    #   ('proximity', 'Proximity cluster')
    #   ('level',     'Nearest level')
    #   ('grid',      'Grid intersection')
    #   ('model',     'Source model')
    #   ('status',    'Status')
    #   ('assigned',  'Assigned to')

clashgroup.plan(results, rule_ids, keep_existing=True, tolerance=6.0, min_size=1)
clashgroup.smart_plan(results, keep_existing=True, tolerance=6.0)
```

`results` is the `clash.snapshot_results` list. `tolerance` is in **model units** and is only
used by proximity rules. `rule_ids` chain: each rule subdivides the previous rule's groups,
and labels join with `' / '` with adjacent repeats collapsed. Groups smaller than `min_size`
fall into `ungrouped_ids`. Every candidate lands in exactly one group or in `ungrouped_ids`;
rules never drop a clash.

Both return the same dict shape:

```python
{
  'groups': [{'name': str, 'ids': [int]}, ...],   # LARGEST FIRST, earliest id breaks ties.
                                                  # Names are deduped by appending ' (2)', ' (3)'.
  'ungrouped_ids': [int],                         # sorted
  'skipped_existing': int,                        # left alone because they were already grouped
  'explanation': str,                             # one line, ready to print
}
```

`_split` raises `ValueError('unknown rule: %r')` for a rule id outside `RULES`.

`plan` with an empty `rule_ids` puts everything in `ungrouped_ids` and explains
`'No rules selected.'`

`smart_plan` picks the side (A or B) whose elements collapse the clashes into the fewest
distinct elements, groups by that side, then proximity-clusters the single-clash leftovers and
leaves anything still alone individual. Cluster names come from the dominant grid, then the
dominant level, then `'Cluster N'`. Results with no `center` form one `'Unlocated'` bucket.

### clashtest

Import needs nothing beyond the stdlib. Each function lazy-imports `clash`/`viewpoints`/`doc`,
so importing this module never needs a live session (`clash` itself cannot be imported outside
one - see its own `ImportError` note above).

```python
clashtest.create(name, a, b, tolerance_m=0.001, test_type='hard', doc=None)
    # a, b: ModelItemCollection, iterable of ModelItem, OR a set name/path string resolved
    #   via sets.find + sets.items_of (ValueError if it does not resolve).
    # tolerance_m: METERS, converted to model units via clash.units_to_meters(doc).
    # test_type: 'hard' | 'clearance' | 'duplicate'. ValueError on anything else, checked
    #   BEFORE any Navisworks import.
    # -> the tree-resident ClashTest, resolved by GUID DIFF after TestsAddCopy. NOT the
    #   object you built: TestsAddCopy copies the definition, it does not take ownership,
    #   and the tests root can also hold test FOLDERS so "assume it's last" would be wrong.

clashtest.edit(test, name=None, tolerance_m=None, doc=None)
    # test: must be LIVE, tree-resident (clash.walk_tests() or create()'s return value).
    # tolerance_m: METERS, same conversion as create(). Neither arg given is a no-op.
    # -> test, mutated in place. name-only -> TestsEditDisplayName (O(1), single call).
    #   tolerance touched -> CreateCopy + TestsEditTestFromCopy (DEFINITION-only; see the
    #   TRAP under clash above - it ignores the results tree, which is correct HERE).

clashtest.run(test, doc=None)              # -> None. TestsRunTest(test).
clashtest.run_all(doc=None)                # -> None. TestsRunAllTests().
clashtest.clear_results(test, doc=None)    # -> None. TestsClearResults(test); test survives.
clashtest.delete(test, doc=None)
    # -> None. ROOT-LEVEL tests only: a test with a Parent raises ValueError up front rather
    #   than letting TestsRemove fail with an opaque .NET error.

clashtest.set_status(results, status, doc=None)
    # status: 'new' | 'active' | 'reviewed' | 'approved' | 'resolved'. ValueError otherwise,
    #   checked BEFORE any Navisworks import.
    # -> int, how many results were edited. Preserves each result's OWN AssignedTo:
    #   TestsEditResultStatus takes an Assignee alongside the status and there is no
    #   status-only overload, so a status batch never clobbers an existing assignment.

clashtest.summary(doc=None)   # -> clash.summarize(doc). Reused, not duplicated.
```

**TRAP: TestsEditTestFromCopy is definition-only, same as `clash.py`'s own trap.** Never use it,
or anything in this module, to restructure results (grouping/ungrouping). That work is
`clash.apply_plan`/`clash.ungroup`, which rebuild a detached copy and commit through the
different, results-aware `TestsReplaceWithCopy`. `edit()` only ever touches `DisplayName` and
`Tolerance`.

### doc

Import raises `ImportError` outside a live Navisworks session.

```python
doc.get_doc()                                     # -> Document (active)
doc.get_title(doc=None)                           # -> str, d.Title
doc.get_filename(doc=None)                        # -> str, full path; '' for an unsaved document
doc.walk_saved_viewpoints(doc=None)               # -> generator of (folder_names_list, SavedItem)
doc.get_property(item, category_name, property_name)
                                                  # -> VariantData, or None when not found
```

`walk_saved_viewpoints` is depth-first and **descends only `FolderItem`s**. A
`SavedViewpointAnimation` is a `GroupItem` too but comes out as one item, never as its cuts.
`folder_names` is the list of ancestor folder display names, empty at top level.

`get_property` looks up by **display** names via `FindPropertyByDisplayName` and returns the
`.Value`, so callers get a `VariantData`, not a raw Python value. Writing properties is not in
the public surface.

### export

Import needs nothing beyond the stdlib. Each function lazy-imports `doc`/`_api`/`_com`.

```python
export.save_nwd(path, doc=None, version=None)
    # version=None (default) -> d.SaveFile(path), CURRENT format, DocumentFileVersion
    #   untouched. version=2023 etc -> d.SaveFile(path, DocumentFileVersion.Navisworks2023).
    #   Unsupported year -> ValueError naming every year the LIVE enum actually supports
    #   (reflected each time, not a hardcoded range), before any file is written.

export.publish_nwd(path, doc=None, **props)
    # props (all optional): keywords, comments, published_for, copyright (str, default None -
    #   omitted from PublishProperties entirely when unset, not written as ''); allow_resave
    #   (default True); display_on_open (default False); expiry (datetime.datetime or None,
    #   default None); exclude_hidden (default False); embed_xrefs (default True).
    #   Unknown kwarg, or expiry that is not a datetime.datetime -> ValueError, checked
    #   BEFORE any Navisworks import. No publish-time NWD version kwarg; use save_nwd first
    #   if a specific file-format version is required.
    # -> None. Document.PublishFile(path, NwdExportOptions, PublishProperties).

export.viewpoint_image(path, doc=None, width=1920, height=1080)
    # -> None (writes path). NO .NET API ROUTE AT ALL: drives the COM 'lcodpimage' plugin via
    #   state.DriveIOPlugin(name, path, options), state from pynavis._com.get_state().
    #   RAISES RuntimeError naming path on ANY failure - a raised exception, or
    #   DriveIOPlugin returning anything other than eExport_OK.
```

**TRAP: `viewpoint_image`'s `width`/`height` option names are a best-effort GUESS, SMOKE-VERIFIED
ONLY.** `InwOaProperty` is a schemaless name/value pair, so there is nothing to reflect against;
this module's guess has not been confirmed against a live export. If an exported image comes out
at the plugin's own default size instead of `width` x `height`, re-check the option names via
`state.GetIOPluginOptions('lcodpimage')` in-app and fix `_IMAGE_OPTION_NAMES` in `export.py`.

### forms

Import needs `PyNavis.Runtime` only, never the Navisworks API, so dialogs open while a model
is still loading. **Every one of these blocks** on a modal WPF dialog.

```python
forms.alert(message, title='pyNavis', copy=None)          # -> None. copy=str adds a Copy
                                                          #    button (clipboard, dialog stays)
forms.confirm(message, title='pyNavis')                   # -> bool. True on Yes.
                                                          #    FALSE ON CANCEL/No.
forms.ask_string(prompt, default='', title='pyNavis')     # -> str, or NONE on cancel
forms.save_file(filter='CSV files (*.csv)|*.csv|All files (*.*)|*.*',
                default_name='', title='Save file')       # -> str path, or NONE on cancel
forms.open_file(filter='All files (*.*)|*.*',
                title='Open file')                        # -> str path, or NONE on cancel

forms.select_from_list(items, title='Select', multiselect=False, prompt=None)
    # items: strings, or (label, value) pairs. Searchable list.
    # -> picked value, or [values] with multiselect=True, or NONE on cancel
    #    (a cancelled multiselect is None, never an empty list).

forms.ask_options(prompt, options, title='pyNavis')
    # -> the chosen entry from options itself (not its index), or NONE on cancel.
    #    prompt=None -> the chromeless quick switch (no titlebar, no question;
    #    Esc or clicking outside cancels). Only for self-explanatory labels.

forms.ask_number(prompt, default=None, min_value=None, max_value=None, title='pyNavis')
    # -> float, or NONE on cancel. The dialog itself refuses OK outside
    #    [min_value, max_value], so the script never sees an out-of-range answer.

forms.pick_folder(title='Select folder', initial=None)
    # -> str path, or NONE on cancel. Standard Windows folder browser.
```

`message` and `prompt` are passed through `str()`, so non-string values are safe.

**TRAP: test for `None`, not falsiness.** An empty string from `ask_string` is a real answer,
distinct from a cancel:

```python
answer = forms.ask_string('New name:')
if answer is not None:          # right
    ...
if answer:                      # wrong: swallows a deliberate empty answer
    ...
```

`confirm` cannot distinguish "No" from "cancelled". Both are `False`.

Cancelling is silent by house rule: exit without a toast, never say "cancelled".

The keyword is literally `filter`, which shadows the builtin inside the call only.

**`forms.progress` is a context manager, distinct from `output.progress`.** It shows a modal,
cancellable progress window that blocks the rest of Navisworks, which is what a batch the user
started on purpose (and may want to stop) calls for; `output.progress` never blocks and has no
Cancel button.

```python
forms.Cancelled                    # Exception subclass, raised by p.check()

with forms.progress('Exporting', 'Starting') as p:
    for index, item in enumerate(items):
        p.check()                          # raises forms.Cancelled once Cancel is clicked
        export_one(item)
        p.update(float(index) / len(items), '%d of %d' % (index, len(items)))
        # p.update(...) ALSO returns False once cancelled, for callers that would
        # rather test a return value than catch an exception.
```

The window closes on every path out of the `with` block: normal completion, a propagating
`Cancelled`, or any other exception. Catch `Cancelled` **outside** the `with`, not inside it,
or the window stays open for the rest of the script.

**`forms.WPFWindow(xaml_file)`** loads a `.xaml` file shipped in the current command's bundle
folder and wraps it as a live WPF window, for a settings form with more fields than a couple of
chained prompts can carry cleanly.

```python
win = forms.WPFWindow('layout.xaml')     # resolves against script.get_command_path();
                                         # IOError immediately if the file is missing
win.find(name)                          # -> the named XAML element, or None
win[name]                               # same lookup; RAISES KeyError instead of None
win.show_dialog()                       # modal, owned by Navisworks, runtime chrome applied
                                         # -> DialogResult
win.close(result=None)                  # True/False sets DialogResult first, then closes
```

Once loaded, every element is a live .NET object: set `.Text`, wire `.Click +=`, read
`.IsChecked`, exactly as in WPF code-behind. `WPFWindow` only solves loading the file and
applying the runtime's dialog chrome; it is not a data-binding layer. Wire every event handler
and set every initial value **before** `show_dialog()`: nothing done to the window after that
call returns takes effect until the next run.

### memory

Import needs nothing beyond the stdlib: the API half lazy-imports inside each function. The
pure half unit-tests anywhere.

```python
memory.VERSION              # 1
memory.MAX_CONTENTS_ROWS    # 500
memory.MemoryFormatError    # Exception subclass
memory.Result(level, message, detail='', count=0)
```

An entry identifies one ModelItem: `{'m': source file name, 'i': model index, 'p': PathId string}`.

Pure half:

```python
memory.new_state(document='', items=None, saved='')
    # -> {'version': 1, 'document': str, 'saved': str, 'cursor': -1, 'items': [...]}
memory.serialize(state)                 # -> str, json.dumps(indent=2, sort_keys=True)
memory.deserialize(text)                # -> state dict. RAISES MemoryFormatError on bad JSON,
                                        #    non-object, wrong version, or a missing item list.
memory.path_for(document_path, root)    # -> str. '<sanitised stem, 40 chars>-<sha1[:8]>.json',
                                        #    everything derived from the LOWERCASED path so a
                                        #    differently cased path hits the same file.
                                        #    Unsaved documents share the 'untitled' register.
memory.key(entry)                       # -> (model name lowercased, path id)
memory.union(a, b)                      # -> list, a then anything new in b, order preserved, deduped
memory.difference(a, b)                 # -> list, entries of a that b does not contain
memory.intersection(a, b)               # -> list, present in both, in a's order
memory.step(cursor, count, delta)       # -> int index, wrapping at both ends. -1 when count <= 0.
memory.memory_root()                    # -> '%APPDATA%\pyNavis\memory'
memory.load(document_path, root=None)   # -> state dict, or a fresh empty one when no file exists.
                                        #    RAISES MemoryFormatError on a corrupt file.
memory.save(state, root=None)           # -> str path. ATOMIC: writes '<path>.tmp' then os.replace.
memory.apply_entries(state, entries)    # -> state, mutated: replaces items, stamps 'saved', cursor = -1
```

**TRAP: `memory.load` propagates `MemoryFormatError`**, and every action function below calls
it through `_state`. A corrupt memory file therefore raises out of `memorize`, `recall`, `add`
and the rest. This is the opposite of `settings.load`, which never raises.

API half. Every action returns a `memory.Result`; none of them raise for user-level problems.

```python
memory.encode_items(items, doc)         # -> [entry]. ModelItems to entries.
memory.resolve_entries(entries, doc)    # -> (ModelItems, missing_count, missing_model_names)
                                        #    Trusts the stored model index only when the model at
                                        #    that index still has the recorded source file name;
                                        #    otherwise finds the model by name, so appending or
                                        #    reordering models does not break a memory.

memory.memorize(doc=None)               # memory := selection.   'error' when nothing is selected.
memory.recall(doc=None)                 # selection := memory.   'error' when the memory is empty or
                                        #    nothing resolves; 'info' on a partial recall.
memory.add(doc=None)                    # memory := memory + selection
memory.subtract(doc=None)               # memory := memory - selection
memory.intersect(doc=None)              # memory := memory and selection
memory.clear(doc=None)                  # memory := empty. Always 'success'.

memory.step_next(doc=None)              # -> Result('info', 'Item N of M', display name)
memory.step_prev(doc=None)

memory.contents(doc=None)               # -> (state, rows) where rows are
                                        #    (model_name, display_name, link_html_or_None),
                                        #    capped at MAX_CONTENTS_ROWS
memory.contents_html(rows, state)       # -> str, the Show table. Hand-built because
                                        #    output.table_html would escape the link markup.
memory.save_as_set(name, doc=None)      # -> Result. Promotes the memory to a native SelectionSet.
memory.purge_targets(root=None)         # -> [path]. Files DIRECTLY in the root matching
                                        #    ^.{1,40}-[0-9a-f]{8}\.json$. Never recursive.
memory.purge(root=None)                 # -> Result. Deletes every memory file, for every document.
```

`add`, `subtract` and `intersect` all return `'error'` when nothing is selected, and
`'warning'` `'Memory is now empty'` when the operation leaves nothing behind.

**`step_next` and `step_prev` change the selection and move the camera.** They replace the
selection with the single item at the new cursor, then call
`ComApiBridge.State.ZoomInCurViewOnCurSel()` to fly the camera to it. The zoom is best effort,
wrapped in a bare `except`, so a failed zoom never loses the step. They also persist the
cursor, so they write the memory file on every step. Only entries that actually resolve in the
current model are stepped onto.

TRAP: `memory.clear`, `selection.clear` and `section.clear` are three unrelated functions.
Import modules, not names.

### output

Import needs nothing beyond the stdlib. `table_html` and `format_table` are pure and
unit-testable. The rest go through `script.get_host()`.

```python
output.print_html(html)                 # -> None. Appends an HTML fragment.
output.print_md(markdown)               # -> None. Converts then appends.
output.print_table(rows, headers)       # -> None. print_html(table_html(rows, headers)).
output.progress(fraction, label='')     # -> None. fraction 0..1; 1.0 completes the bar.
output.element_link(item, label)        # -> str of HTML. A link that selects the ModelItem.
output.table_html(rows, headers)        # -> str. PURE.
output.format_table(rows, headers)      # -> str. PURE. Aligned plain text with a dashed rule.

output.chart_bar(labels, values, title=None)        # -> None. Theme-aware SVG card.
output.chart_pie(labels, values, title=None)        # -> None.
output.chart_doughnut(labels, values, title=None)   # -> None. chart_pie with a hole.
output.chart_line(labels, series, title=None)       # -> None. series: {name: [values]}
                                                    #    sharing one labels axis.
output.print_image(path, caption=None)   # -> None. png/jpg/jpeg/gif/svg as a data URI.
                                         #    RAISES ValueError for any other extension.
output.print_code(text)                 # -> None. Escaped monospace block, horizontal scroll.
output.save(path)                       # -> None. Everything printed so far, as standalone
                                         #    HTML that opens in a browser with nothing running.
output.set_title(text)                  # -> None. Retitles the window (default is
                                         #    'pyNavis - <button title>').
```

All four chart functions print through `print_html` under the hood, so calling one opens or
appends to the output window exactly like any other `print_*` call. Every series colour is
`var(--chart-N)` (N wraps 1..6), pulled from the same CSS custom properties the rest of the
window themes with, so the identical markup renders correctly light or dark.

**TRAP: `chart_line` truncates a longer series to the label count, and scales from that
truncated set.** A series with more values than `labels` only has its leading values drawn; the
vertical peak is computed from those same drawn values, never from the untruncated series, so a
long tail nobody sees cannot compress the visible lines by inflating the scale.

**Any of the first five opens the output window**, lazily, on the first write. The window is
titled `pyNavis - <button title>`. A script that writes nothing opens no window, which is why
one-line statuses belong in `toast` and never in `print()`.

**TRAP: outside a run these silently do nothing.** `PyNavisHost.WriteHtml` and `.Progress` are
`_currentOutput?.WriteHtml(...)`, a null-conditional call. `element_link` returns the plain
`label` when there is no output window. No exception, no output.

`table_html` **decides numeric alignment from the data, not the column position.** A column is
tagged `class="num"` when every non-empty cell parses as a float after `.replace(',', '')` and
at least one cell is non-empty. Mixed columns are left-aligned. Column count follows `headers`;
short rows are padded with `''`; extra cells in a row are ignored. Every header and cell is
HTML-escaped.

`print_md` HTML-escapes its input **before** converting, so raw markup in your data can never
become live HTML. It covers `#`/`##`/`###` headers, `**bold**`, `*italic*`, `` `code` ``,
```` ``` ```` blocks, `- ` lists, `[text](url)` links and paragraphs, and nothing else.

`element_link` returns a string; embed it yourself:

```python
output.print_html('Worst clash: ' + output.element_link(item, item.DisplayName))
```

TRAP: passing link HTML through `print_table` escapes it into visible markup. Build the table
by hand with `print_html` when a cell needs to be a link, the way `memory.contents_html` does.

### panes

Import needs `PyNavis.Runtime` only, but every call goes through the Navisworks plugin table,
so calls need a live session. The API half of `*.dockpane` bundles: a panel is `pane.xaml`
for content, an optional `script.py` that receives the panel as `__pane__`, and a ribbon
toggle that follows its visibility.

```python
panes.show(bundle_key=None)        # -> bool. Opens the panel and brings it to the front of
                                   #    its dock tab group. False when it has no slot.
panes.hide(bundle_key=None)        # -> bool. Closes the panel. False when it has no slot.
panes.toggle(bundle_key=None)      # -> bool. Shows when hidden, hides when shown.
panes.is_visible(bundle_key=None)  # -> bool. True while the panel is on screen.
panes.slot_of(bundle_key=None)     # -> int, the 1-based slot the panel claimed, or 0 when
                                   #    every slot was taken.
panes.slot_count()                 # -> int, slots this session ACTUALLY registered (the
                                   #    verified count, not what config asked for).
panes.add_slots(count)             # -> int, the new total. Generates a satellite DLL with
                                   #    extra slots; takes effect after a Navisworks restart.
                                   #    add_slots(0) removes the satellite (same restart).
```

**TRAP: the default `bundle_key` is the most recently run command, not "this panel".**
`bundle_key=None` resolves to `PyNavisHost.CommandBundleKey`, which the runtime sets before
every run and never un-sets (`ValueError` when nothing has run at all). That is correct while
a pane's own `script.py` is executing, because the runtime sets the context to that pane
first, but an event handler wired up in `script.py` fires long after that run finished, by
which time the user may have run other tools, so the default silently targets the wrong
panel. From inside a panel's own handler, pass the key explicitly (`__pane__.BundleKey`) or
use `__pane__.Visible` instead.

`add_slots` works once per install and then needs a manual file deletion: Navisworks maps
the satellite DLL at every startup, and a mapped file cannot be replaced from inside the
session. It raises with the exact remedy (close Navisworks, delete the folder it names,
start again and set the count); toast `str(ex)` rather than rephrasing it. `slot_count()`
probes the plugin table upward and stops at the first gap, because a satellite can fail to
load with no error raised anywhere; trust it over `config.json`.

### overlay

Draws in the 3D view on top of the model, through the loader's `PyNavis.Overlay` render
plugin. Items are world-coordinate segments (plain `(x, y, z)` tuples in model units) plus an
optional label drawn at the pixel its point projects to; they are redrawn every frame so
they follow orbit and zoom, and they draw through geometry like the native measure lines.
True Distance is the reference user.

```python
overlay.add(tag, segments, label=None, label_at=None, anchor=None)
                                   # -> bool. segments is [((x,y,z), (x,y,z), dashed), ...].
                                   #    Replaces any earlier item with the same tag.
overlay.dimension(tag, start, end, label, extension_to=None, anchor=None)
                                   # -> bool. Solid start-end with label at the midpoint,
                                   #    plus a dashed end-extension_to when given.
overlay.clear(tag=None)            # -> bool. That tag's items, or everything when None.
overlay.redraw()                   # -> bool. Repaints the overlay; call after any change.
```

Every call returns True when the overlay plugin is loaded and False (logged once) when it is
not, which is what a loader that has not been deployed looks like: the tool's own result is
unaffected, only the drawing is missing, so do not fail a tool over it.

`anchor=(first, end)` ties an item to the native point-to-point measurement with those two
points: the overlay drops it the first frame the measurement changes or is cleared, with
no event wiring in the script. Items without an anchor stay until `clear()`.

### pick

An interactive point pick that feels like the native Measure tool: the same vertex and edge
snapping, the same measure cursors, a marker under the pointer showing what would be picked,
and the view still zooms and pans while the user lines the click up. It runs on the loader's
`PyNavis.Pick` ToolPlugin, which borrows the document's tool for one click and puts the
previous tool back afterwards. Get Coordinates is the reference user.

```python
pick.point(prompt='Click a point in the view')
    # -> Hit or None. BLOCKS until the user clicks, Escapes or right-clicks.
pick.point_then(callback, prompt='Click a point in the view')
    # -> the session. Returns at once; calls callback(hit_or_None) later.
pick.cancel()                      # Ends the running pick; its callback still fires, None.
pick.measure_point(prompt)         # -> Hit (point only) or None. Native Measure tool, no plugin;
                                   #    the measurement is reset and the previous tool restored.
pick.measure_points(prompt, second_prompt='Now click the second point', notify=None, keep=True)
    # -> ((x, y, z), (x, y, z)) or None. notify(message, detail) shows each prompt (None:
    #    a toast; banner.prompt: the bar along the bottom, which the caller must then
    #    replace or banner.clear() on None). keep=False drops the measurement the moment
    #    it is complete and restores the previous tool (then draw un-anchored). Native Measure > Point to Point asked for a whole
    #    line; drops any measurement already on screen first. With keep=True the finished
    #    measurement STAYS on screen and the document stays on the Measure tool (draw beside
    #    it with an anchored overlay.dimension). A lone first point left by a cancel is dropped.
pick.DEFAULT_PROMPT, pick.CANCEL_HINT, pick.SECOND_PROMPT
```

```python
hit.point    # (x, y, z) floats, model units. Always present.
hit.normal   # (x, y, z) or None.
hit.item     # the ModelItem, or None.
hit.snap     # 'vertex' | 'edge' | 'line-vertex' | 'line-middle' | 'arc-center' | None.
```

Cancelling is never an error: Escape, a right-click and the user choosing another tool all
return `None`. The only exception is `pick.Unavailable` (a `RuntimeError`), which means the
deployed loader has no pick plugin: Navisworks scans for plugins once at startup, so the
remedy is always a restart, and the message says so. Handle it:

```python
try:
    hit = pick.point('Click a point to copy its coordinates')
except pick.Unavailable:
    toast.error('Pick tool not available', 'Restart Navisworks to finish updating pyNavis.')
    return
if hit is None:
    return
```

The prompt is shown as an info toast with "Esc or right-click to cancel." under it. It
dismisses itself after a few seconds, which is right: it is an instruction, not a mode
indicator, and the cursor and marker say the pick is still running.

**Two picks cannot run at once.** The document has one tool, so a second `point()` cancels
the first, whose call returns `None`. That is what makes a blocking pick safe to click twice.

`point()` blocks by pumping a nested WPF dispatcher frame, which is a Python-only decision:
the C# core is event-based (`PickSession.Ended`), so if a nested message loop ever misbehaves
in some Navisworks release, moving a tool to `point_then` is a library change and a Reload,
not a rebuild. Both forms are exercised under IronPython, the default engine; nothing in the
module is engine specific, so CPython should behave the same, and `point_then` is the
fallback there if a nested loop ever proves unsafe.

### props

Import needs nothing beyond the stdlib. Reading needs no lazy import of its own -
`PropertyCategories` access is plain attribute access on an item you already hold, same as
`doc.get_property` uses. Only `set_custom`/`remove_custom` (the write side) lazy-import the COM
bridge, because writing properties cannot go through the .NET API at all. This module
SUPERSEDES `doc.get_property` for new code: `get()` returns an already-coerced Python value
instead of a raw `VariantData`.

```python
props.categories(item)   # -> [(display_name, name), ...], item.PropertyCategories order.

props.get(item, category, prop, by_display=True)
    # by_display=True (default): FindPropertyByDisplayName, matches doc.get_property.
    # by_display=False: FindPropertyByName.
    # -> coerced value (str/int/float/bool/datetime.datetime/(x,y,z) tuple), or None when the
    #   category or property is not present.

props.all_of(item)
    # -> [{'category', 'property', 'value'}, ...] (display names), item's own
    #   PropertyCategories/Properties order. Table-ready, e.g. for output.print_table or xl.

props.value_map(item, spec)
    # spec: {key: (category, prop), ...}. -> {key: value}, by DISPLAY NAME only (call get()
    #   directly for a by_display=False lookup). A missing pair maps to None, same as get().

props.get_custom(item, tab_name)
    # -> {name: value} for a pynavis-written custom tab, or None if item has no tab_name tab.
    #   Reads through the SAME PropertyCategories access as get()/all_of() - no COM import.

props.set_custom(items, tab_name, values, doc=None, progress=None)
    # values: {name: str/int/float/bool}. ValueError for an empty/whitespace name or an
    #   unsupported value type, checked before any Navisworks import.
    # -> (written_count, errors). One transaction (one undo entry). progress(i, total) after
    #   every item, 1-based. Each item wrapped in its own try/except: a COM callback
    #   exception can vanish silently otherwise, so one bad item never aborts the rest.

props.remove_custom(items, tab_name, doc=None, progress=None)
    # -> (removed_count, errors). Same shape as set_custom. An item without tab_name is left
    #   alone and not counted.
```

**TRAP: `set_custom` REPLACES the whole tab, it does not merge.** One COM `SetUserDefined` call
per item writes EXACTLY `values`: an existing same-named tab is replaced outright, and a
property that was on the tab before but is left out of `values` this time disappears. Read the
tab back with `get_custom` first and merge your own new values in when you mean to add one
field to an existing tab.

### script

Import needs `PyNavis.Runtime` only. Never touches the Navisworks API.

```python
script.get_host()           # -> PyNavisHost. The same object as the __pynavis__ global,
                            #    available even outside a script scope.
script.get_script_path()    # -> str, the executing command's script.py
script.get_command_path()   # -> str, the executing command's bundle folder
script.get_title()          # -> str, the executing command's resolved title
script.get_host_version()   # -> str, pyNavis runtime assembly version
script.is_dark_theme()      # -> bool. Theme your own dialogs with this.
script.reload_pynavis()     # -> None. Rescans extension folders and rebuilds the ribbon.
script.get_toggle_state()   # -> bool. True when THIS *.toggle bundle is currently on.
script.set_toggle_state(on) # -> None. Sets THIS *.toggle bundle's state, flips its icon.

script.get_envvar(name, default=None)   # -> session-scoped value shared by every pyNavis
                                        #    script this session, or default if unset.
script.set_envvar(name, value)          # -> None. value=None removes it. Gone on restart.

script.clipboard_copy(text)             # -> None. Windows clipboard.
script.clipboard_text()                 # -> str. '' when empty or unreadable. NEVER raises:
                                        #    the clipboard is machine-wide, so another app
                                        #    holding it open must not kill a tool that polls it.
script.open_url(url)                    # -> None. Default browser.
script.show_in_explorer(path)           # -> None. Explorer, file selected (or a folder as-is).
script.get_bundle_file(name)            # -> str path of a file beside script.py in the
                                        #    current bundle, or None when it does not exist.

script.get_config(defaults)             # -> settings.load() keyed off the RUNNING bundle's
                                        #    own path ('cfg_' + slugified key). No shared
                                        #    TOOL constant needed between config.py/script.py.
script.save_config(values)              # -> settings.save(), same auto-derived key.
script.reset_config()                   # -> None. Deletes this bundle's saved config file.

script.get_data_file(suffix='json')             # -> str path, shared across documents.
script.get_document_data_file(suffix='json')    # -> str path, THIS document only. Needs
                                                #    doc.get_filename(): in-host only.
script.store_data(key, value, per_document=False)   # -> None. JSON-serialisable value.
script.load_data(key, default=None, per_document=False)   # -> the stored value, or default.
script.data_exists(key, per_document=False)     # -> bool.

script.get_logger()   # -> a Logger for the running code (cached by log label per run):
                      #    .debug/.info/.success/.warning/.error(message), .set_level(level)
                      #    Always writes to the pyNavis log file. warning/error ALSO print a
                      #    colored line to the output window, but only when one is already
                      #    open - a logger call never opens the window by itself.
                      #    Default threshold is 'info': debug is silent until set_level('debug').
                      #    Label is the command's title, or 'hook <ext>:<event>' /
                      #    'startup <ext>' for those (PyNavisHost.LogSource); they set no
                      #    command context, so without it they took the LAST command's title.
```

**TRAP: `get_config`/`save_config` derive their key from the running bundle's own path, so two
bundles can never share one config store.** Reach for `pynavis.settings` with an explicit
`TOOL` constant when two buttons must deliberately agree on one settings file.

**TRAP: outside a run these go stale, not blank.** The host keeps the context of the most
recent run, so from a console or a unit test you get whatever ran last, or `None` if nothing
has. Only trust them inside a command.

**TRAP: a handler that fires AFTER script.py returned is "outside a run".** A modeless
dialog's Click/Closed handler, a dock panel's handler, a timer: by then the user may have run
another tool, and `script.save_config()` / `store_data()` would silently read or overwrite
THAT tool's file. Pin the identity while the command is still running and use the pinned
object in every late callback:

```python
me = script.bind()                       # top of script.py -> BoundScript
def on_closed(sender, args):
    me.save_config({'width': window.Width})
## me.get_config / save_config / reset_config / store_data / load_data / data_exists
```

`pynavis.settings` and `pynavis._datafiles` writes are atomic (temp file, then replace), and
`store_data` over a corrupt data file keeps the old bytes as `<file>.corrupt`.

`reload_pynavis()` rebuilds the ribbon that owns the button currently executing. It is what
the shipped Reload button calls.

**TRAP: `get_toggle_state`/`set_toggle_state` only make sense inside a `*.toggle` bundle's own
run.** Both act on "whichever bundle is currently executing", which is set only by an actual
click of `script.py` or `config.py`. Calling either from a hook, `startup.py`, or a
smartbutton's build-time `__selfinit__` run has no bundle to act on and does not reliably
target any particular toggle. State lives for the Navisworks session only; a toggle that must
come back on after a restart needs `pynavis.settings`, read from its own `script.py`.

### geometry

The one route to an item's actual faces. Reuses the section tool's COM primitives walk
(matrix layout detection, per-instance fragment filtering, the swallowed-exception
callback) and hands back plain tuples so callers stay pure. True Distance is the
reference user: it asks which faces a measured point sits on.

```python
geometry.world_triangles(item, budget=200000, note=None)
                                   # -> [((x,y,z), (x,y,z), (x,y,z)), ...] in world
                                   #    coordinates, for the item and its geometry
                                   #    descendants. [] when the item declares more than
                                   #    budget primitives, the walk yields nothing, or no
                                   #    matrix layout fits; note(text) says which.
```

An empty list means "unknown", never "no faces": check the note before concluding
anything. COM vertices are float32, so a point on a face far from the origin carries
real noise; size any on-surface tolerance from the coordinate magnitude
(`faces.tolerance_for`). To ask which faces a measured point sits on, use
`faces.faces_under(view, point3d, tol, log)` rather than walking triangles yourself.

```python
geometry.translate(items, vector, doc=None, name='Move items')
                                   # -> int moved. Slides items by (dx, dy, dz) world
                                   #    units as the permanent transform Item Tools makes:
                                   #    composes with any existing one, saved in the NWF,
                                   #    ONE undo step. Never touches the source file.
geometry.reset_transform(items, doc=None, name='Reset transform')
                                   # -> int reset. Removes every permanent transform
                                   #    override from items, one undo step.
```

### section

Import needs nothing beyond the stdlib. The pure half unit-tests anywhere.

```python
section.TOOL              # 'section_planes'
section.DEFAULTS          # {'padding_mm': 150.0, 'snap_degrees': 1.5}
section.KEEP_INWARD       # True. The one empirical constant: the .NET API documents neither
                          # AlignToPick's sign nor its Custom alignment.
section.TRIANGLE_BUDGET   # 150000
section.Result(level, message, detail='', count=0)
```

Box dict, produced by `fit`:

```python
{'yaw':    float radians about Z,
 'min':    (x, y, z),      # IN THE YAW-ROTATED LOCAL FRAME
 'max':    (x, y, z),      # local
 'center': (x, y, z),      # local
 'size':   (dx, dy, dz)}   # already padded; each size gained twice the padding
```

Pure half:

```python
section.convex_hull(points)                       # -> [(x, y)] counter-clockwise, Andrew's
                                                  #    monotone chain. Collinear points dropped,
                                                  #    first point not repeated.
section.min_area_yaw(points, snap_degrees=1.5, snap_ratio=0.01)
                                                  # -> float radians in [0, pi/2). Returns 0.0
                                                  #    when the fit is not worth having.
section.fit(points, padding=0.0, snap_degrees=1.5, snap_ratio=0.01)
                                                  # -> box dict, or NONE when points is empty
section.to_world(box, point)                      # -> (x, y, z) world. Correct for direction
                                                  #    vectors too (rotation about Z, no translation).
section.faces(box, inward=None)                   # -> six (origin, normal) WORLD pairs, ordered
                                                  #    +X, -X, +Y, -Y, top, bottom. inward=None
                                                  #    uses KEEP_INWARD.
section.world_aabb(box)                           # -> (min_tuple, max_tuple) world AABB of the
                                                  #    eight corners. What ZoomBox and
                                                  #    ClipPlaneSet.Range want.
section.compact(points)                           # -> the XY hull plus the lowest and highest
                                                  #    point. Identical fit, bounded memory.
section.pick_source(primitive_count, has_com, budget=TRIANGLE_BUDGET)
                                                  # -> 'triangles' | 'fragments' | 'boxes'
section.agrees(points, box_min, box_max, slack=0.05)
                                                  # -> bool. The guard on the COM walk.
section.describe(box, count, source, unit_scale)  # -> str toast detail, sizes in metres
```

API half:

```python
section.collect_points(items, notes=None)   # -> (points, source). Degrades triangles ->
                                            #    fragments -> boxes, appending a reason to
                                            #    notes at every drop. Never raises.
section.fit_to_selection(items, values, doc=None)   # -> Result
section.clear(doc=None)                             # -> Result. 'info' when already off.
section.plan_to_selection(items, values, doc=None)  # -> Result
```

`values` is a settings dict, normally `settings.load(section.TOOL, section.DEFAULTS)`.
`fit_to_selection` and `plan_to_selection` return `Result('error', 'Nothing selected', ...)` on
an empty `items`, and `Result('error', 'Nothing to section', ...)` when nothing has geometry.
Both are **one undo step**: the viewpoint is `CreateCopy()`d, edited detached, then written
back with a single `CopyFrom`.

A fit that fell back to bounding boxes reports as a **warning**, not a success, because
axis-aligned box corners are themselves axis-aligned so the yaw comes out 0.0 and the tool has
done nothing Navisworks' own Fit Section to Selection does not already do. Fewer than six
available planes is also a warning.

**TRAP: the triangle source requires IronPython.** `_primitive_callback` builds a
`class Collector(InwSimplePrimitivesCB)`, which subclasses a COM interface, and only the
IronPython engine can do that. The extension defaults to `engine: ironpython`; the module
docstring says plainly not to run these bundles under CPython. Under CPython the subclassing
failure is caught inside `collect_points`, a note is appended, and the walk degrades to
fragment boxes and then to item boxes, so the tool appears to work while silently producing an
unturned box.

Related trap already handled inside the module, worth knowing if you extend it: an exception
raised inside a COM callback cannot travel back out through the COM boundary.
`GenerateSimplePrimitives` returns as if nothing happened and the walk reports zero vertices,
which is exactly how a broken vertex read once hid. The first failure is captured into a list
instead of vanishing.

### selection

Import raises `ImportError` outside a live Navisworks session.

```python
selection.get_items(doc=None)         # -> list of ModelItem. Empty list when nothing is selected.
selection.set_items(items, doc=None)  # -> None. REPLACES the selection (builds a
                                      #    ModelItemCollection and calls CurrentSelection.CopyFrom).
selection.clear(doc=None)             # -> None
```

`get_items` materialises the list, so it is a snapshot: mutating the selection afterwards does
not change it. None of these block.

### separation

Pure throughout: triangle tuples as `geometry.world_triangles` returns them in, plain dicts
out, no Navisworks import. Clear Clash is the reference user. Exact for the chosen axis
and correct for any shape: every triangle pair is in contact for one closed range of travel
along the axis, and the move is the first travel past every range reaching back to zero.
Touching counts as contact (a hard clash at zero tolerance); one solid entirely inside
another shares no surface point and reads as clear, as in Clash Detective.

```python
separation.AXES                      # ('+x', '-x', '+y', '-y', '+z', '-z')
separation.TooDetailed               # Exception subclass, raised past budget_seconds

separation.resolve(mover, obstacle, axes=None, clearance=0.0, eps=None, budget_seconds=None)
    # -> {'status': 'clash' | 'tight' | 'clear',
    #     'axis': str, 'move': float (clearance included; 0 for 'clear'),
    #     'vector': (dx, dy, dz) for geometry.translate,
    #     'gap': float before moving, 0 when touching, None when they never meet along axis,
    #     'candidates': [(axis, move, exact), ...] shortest first}
    # 'clash': touching or crossing now. 'tight': apart, but by less than clearance along
    # some axis. 'clear': nothing to do; axis and gap then name the nearest contact.
    # axes=None tries all six, tightest bounding-box bound first so later axes abort early
    # (their candidates carry exact=False and a lower-bound move).
    # Raises ValueError on an empty mesh.

separation.along(mover, obstacle, axis, eps=None, clearance=0.0, abort_at=None, deadline=None)
    # -> {'axis', 'move', 'contact', 'gap', 'exact'} for one direction.
```

**Clearance is along the axis moved.** 25 mm means the mover ends 25 mm from the obstacle in
the direction it travelled; nothing is promised about the other two axes. Leave `eps` None
and it is scaled from the coordinate magnitude, because COM vertices are float32.

### sets

Import needs nothing beyond the stdlib. The pure half (the query model behind `query()`) has no
Navisworks dependency; the API half lazy-imports inside each function.

```python
sets.query()   # -> a NEW _query.Builder. Chain to build up AND groups ORed together:
    #   .prop(category, name) / .and_prop(category, name)   start a condition
    #   .category(name)                                     complete has_category, no op needed
    #   .equals(v) / .not_equals(v)
    #   .contains(text) / .wildcard(pattern)
    #   .gt(n) / .ge(n) / .lt(n) / .le(n)
    #   .exists() / .missing()                               has_property / not_has_property
    #   .ignore_case()                                        applies to the LAST condition added;
    #                                                             ValueError if a prop() is still
    #                                                             pending (it would flag the one before)
    #   .or_group()                                           closes the current AND group, starts a new one
    #   .build()                                              -> validated Query. ValueError listing
    #                                                             every problem otherwise.

sets.compile_search(query_or_dict, doc=None, scope=None)
    # -> [Search, ...], ONE PER OR GROUP (see TRAP below).
    #   scope=iterable of ModelItem confines the walk to those items and their descendants
    #   (Selection.CopyFrom) instead of the whole model (Selection.SelectAll). A scoped
    #   Search pins today's items, so never store one as a saved set.
sets.run(query_or_dict, doc=None, prune=False, scope=None)
    # -> ModelItemCollection, the UNION of every group's FindAll matches, deduped in Python.
    #   prune=True forces PruneBelowMatch on every compiled search (only ever ON, never OFF).
sets.find_first(query_or_dict, doc=None, scope=None)
    # -> the FIRST matching ModelItem, or None. Tries OR groups in order, stops at the first
    #   group that hits.
    # COST, and the reason this exists: FindAll ALWAYS walks the whole model - it cannot know
    #   it has seen the last match. Measured on a 1.75M-item federation: 3.0s per sets.run
    #   against a mean 0.80s per sets.find_first. If one hit is enough, or if a cheap scoped
    #   sweep can finish the job, this is 3-4x cheaper per call and the gap grows with model
    #   size. There is no bulk shortcut to fall back on: OR costs one full walk PER TERM
    #   (20 ids in one StartGroup search measured 56s, exactly what 20 searches cost), and
    #   PruneBelowMatch makes no difference.
    # ONLY use this where ONE hit genuinely answers the question, and make sure the
    #   condition really identifies one thing first. A Revit "Element ID" tab does NOT:
    #   the nested parts under a family instance carry it with the TYPE's id (Revit Type
    #   > Id), the same number on every instance of that type - one type id matched 16
    #   nested parts under 16 different fixtures, none of them an element. AND in
    #   has_category('LcRevitData_Element', by_display=False) to match real elements
    #   only (see Select by IDs). Scoping a follow-up sweep to the found item's
    #   ancestors does not rescue an ambiguous condition either: measured level by
    #   level the count went 1 -> 6 -> 16 and only the document root collected every
    #   match, i.e. the full walk again. If the caller needs every match, pay sets.run.

sets.snapshot(doc=None)
    # -> [{'guid', 'key' ('0/2/1'), 'parent_key', 'name', 'depth', 'is_folder', 'has_search'}],
    #   depth-first tree order. Same shape family as viewpoints.snapshot.

sets.find(name_or_path, doc=None)
    # bare name (no '/'): first match anywhere, depth-first. 'A/B/Name': walks named folders,
    #   matches the last segment among THAT folder's direct children only.
    #   Literal '/' in a name is written 'A\/B' (backslash then slash = literal char).
    # -> SavedItem (SelectionSet or FolderItem), or None.

sets.items_of(saved_set, doc=None)   # -> ModelItemCollection. ValueError for a folder.
sets.select(saved_set_or_items, doc=None)
    # -> None. Accepts a SavedItem (resolved via items_of) OR a ModelItemCollection you
    #   already have (e.g. from run()), passed straight through.

sets.create(name, source, folder=None, doc=None)
    # source: a query (single OR group only, see TRAP) OR an iterable of ModelItem /
    #   ModelItemCollection (becomes a static/explicit set instead).
    # folder: '/'-separated, missing segments created. name deduped against EVERY existing
    #   set/folder name in the WHOLE document (not just the target folder's siblings).
    # -> the new SelectionSet, TREE-RESIDENT (re-resolved after the add, since AddCopy
    #   copies rather than moves) - safe to pass straight to rename/update/delete.
sets.update(name_or_item, source, doc=None)
    # Same source rule as create(). Replaces content in place: same name, position, GUID.
    # -> the updated set, tree-resident. ValueError for a name that does not resolve, or a folder.
sets.rename(name_or_item, new_name, doc=None)   # -> renamed item, tree-resident, same position/GUID.
sets.delete(name_or_item, doc=None)             # -> None. Deletes a set OR a folder (+ contents).
sets.create_folder(path, doc=None)
    # -> the deepest (leaf) FolderItem. Existing segments reused (checked against the LIVE
    #   tree at each step, not a stale snapshot); missing segments created.

sets.to_dict(saved_item, doc=None)
    # search set -> {'or': [{'and': [condition, ...]}]} (ALWAYS one group - a native set holds
    #   one condition list). static/explicit set -> {'items': N} instead (a count, NOT
    #   portable - import_dict rejects it).
sets.export_all(doc=None, include_static=False)
    # -> {'sets': [{'name', 'folder', 'query'}, ...]} - SEARCH sets only, so the result feeds
    #   import_dict unfiltered. include_static=True adds the non-importable
    #   {'name', 'folder', 'items'} entries back, for exports meant to SHOW the tree.
sets.import_dict(data, doc=None, replace=False, progress=None)
    # data: export_all()'s shape. Validated WHOLE-PAYLOAD BEFORE touching the document - one
    #   bad entry aborts the whole import, never a half-applied batch. An EMPTY condition
    #   group is a bad entry too (it would match the WHOLE model). replace=True updates
    #   an existing same-named set in place (via update()) instead of creating a deduped
    #   '(2)' sibling. progress(done, total) after each entry.
    # -> (created_count, errors). A per-entry exception inside the transaction is recorded
    #   and skipped, not fatal to the rest of the batch.
```

**TRAP: a raw `Api.Search()` matches NOTHING until you scope it.** A fresh `Search` carries an
empty selection (`Selection.IsClear` True), so `FindAll` returns 0 for every predicate - even
`HasPropertyByDisplayName('Item', 'Name')`, which every item in every model satisfies. The
fix is `search.Selection.SelectAll()`, and `compile_search` does it for you; this only bites
if you build a `Search` by hand. Two more defaults on that object are not what you would
guess: `PruneBelowMatch` starts **True**, and `Search.Clear()` clears the selection scope as
well as the conditions, so a `Search` reused across a loop must either be re-`SelectAll()`ed
or reset with `SearchConditions.Clear()` instead (that one keeps the scope). A shipped
commercial add-in gets this exact loop wrong and silently resolves only the first id of a
paste. Field-measured 2026-09-20.

**TRAP: OR groups become MULTIPLE searches, and a saved set holds only ONE AND group.** The
native search API ANDs every condition inside one `SearchConditionCollection`; it has no OR of
its own. `compile_search` emits one `Search` per OR group, and `run()` unions their `FindAll`
results in Python - that is how `run()` can express an OR at all. But a native
`DocumentSelectionSets` entry has exactly one `SearchConditionCollection`, so `create()` and
`update()` accept only a query with exactly one OR group and raise
`ValueError('search sets hold one condition list...: this query has %d OR groups')` for
anything wider. Split a multi-group query into one set per group instead.

**TRAP: `ignore_case` and `prune` do not survive a round trip.** A query you build yourself
keeps `.ignore_case()` through its OWN `to_dict()`. But `sets.to_dict`/`export_all` read an
already-compiled, LIVE search condition back out of the document, and there is nothing on a
compiled condition to tell whether `IgnoreStringValueCase()` was applied. A case-insensitive
condition therefore round-trips through `export_all` -> `import_dict` as a plain,
case-sensitive one. `Query.prune` and `Query.locations` are the same gap one level up and lose
the flag even earlier: `Query.to_dict()` serialises `{'or': [...]}` and NOTHING else, so prune
and locations are dropped by the query's own `to_dict`/`from_dict` round trip, and therefore by
every set export/import too. Pass `prune=True` to `sets.run` at the call site instead of
expecting it to travel with the data. Known gaps, not workaround targets.

**The `'A\/B'` escape is a `find()`-only convention** (plus the `folder` argument of
`create`/`create_folder`, which is parsed the same way). Every other path is RAW: `export_all`
emits `folder` and `name` exactly as they read in the tree, unescaped, and `import_dict` reads
them back the same way - so a set genuinely named `A/B` exports as `A/B` and re-imports as two
folder levels, not as one slash-carrying name.

GUIDs survive `rename()`/`update()`: both go through create-copy-and-swap (no single-call
rename/replace on `DocumentSelectionSets`), and the original item's GUID is restored onto the
replacement before swapping in, best-effort (silently skipped if the API refuses the
reassignment).

### settings

Import needs nothing beyond the stdlib. Works in any engine and outside Navisworks entirely.
One JSON file per tool under `%APPDATA%\pyNavis\settings\`.

```python
settings.path_for(tool)              # -> str, '<%APPDATA%>\pyNavis\settings\<tool>.json'
settings.merge(defaults, stored)     # -> dict
settings.load(tool, defaults)        # -> dict
settings.save(tool, values)          # -> None
settings._APPDATA_OVERRIDE           # module-level test seam, points the store elsewhere
```

**`settings.load` NEVER raises.** A missing file, a corrupt file, a non-object JSON body, an
unreadable path: every one of them is caught by a bare `except Exception` and returns
`dict(defaults)`. There is no way to tell "no saved settings" from "the file is broken".

**`settings.merge` drops keys outside `defaults`.** It iterates `defaults`, not `stored`, so
stale entries from an old tool version never leak back in. A key you add to `DEFAULTS` later
simply starts appearing.

**`settings.save` is NOT atomic.** It creates the directory when missing and writes straight to
the target with `json.dump(values, f, indent=2, sort_keys=True)`. An interrupted write leaves a
truncated file, which `load` then treats as absent. Contrast `memory.save`, which writes a
`.tmp` file and `os.replace`s it. `save` propagates `IOError`/`OSError` from the write.

Values must be JSON-serialisable. There is no schema and no validation.

### toast

Import needs `PyNavis.Runtime` only. **Never blocks**, never takes focus, dismisses itself,
opens no window.

```python
toast.show(level, message, detail=None)   # level: 'success' | 'error' | 'info' | 'warning'
toast.success(message, detail=None)       # green
toast.error(message, detail=None)         # red
toast.info(message, detail=None)          # blue
toast.warning(message, detail=None)       # amber
```

`level`, `message` and `detail` are all passed through `str()`; `detail=None` stays `None`.

A toast is one line plus an optional muted detail line. It carries no buttons, so it can never
ask a question, and it cannot hold a table.

### view

Import needs nothing beyond the stdlib; every Navisworks import is deferred into the function
bodies, so the module loads outside a session even though calling into it does not.

```python
view.zoom_selected(doc=None)      # -> bool. Zooms onto the CURRENT selection. False (plus a log
                                  #    line) when the host refuses, never an exception: a tool
                                  #    that already selected the right things should not fail
                                  #    over the camera. COM only, there is no .NET route.
view.isolate(items, doc=None)     # -> int kept. Hides every root item then unhides items, both
                                  #    in ONE viewpoints.transaction. Empty items is a no-op,
                                  #    not a blackout.
view.unhide_all(doc=None)         # -> None. ResetAllHidden in one undo step.
```

`isolate` works because Navisworks resolves visibility at the leaf, so unhiding a wanted item
under a hidden root shows it. Ctrl+Z and Navisworks' own Unhide All both clear it.

### viewpoints

Import needs nothing beyond the stdlib. The pure half runs anywhere.

Snapshot row:

```python
{'guid':       str, stable SavedItem identity,
 'key':        '0/2/1' index path,
 'parent_key': '0/2', '' at root,
 'folder':     owning folder path, '' at root,
 'name':       str,
 'depth':      int,
 'is_folder':  bool,
 'kind':       'folder' | 'viewpoint' | 'animation',
 'comments':   int}
```

Rows come back in tree (depth-first) order and the numbering relies on that.

**Edits resolve by GUID. Index paths describe tree SHAPE only and go stale the moment anything
moves**, so resolving an edit by path could rename or delete a different item than the user
picked. `key` is for identifying rows in your own UI, not for addressing the document.

Pure half:

```python
viewpoints.plan_renames(rows, checked_keys, op)
    # -> {'renames':    [{'key', 'guid', 'old', 'new', 'collision'}],  # only CHANGED names
    #     'collisions': [message],   # duplicate sibling, or renamed to nothing
    #     'problems':   [message]}   # bad regex, unknown op type
viewpoints.guids_for(rows, keys)        # -> [guid] in tree order; unknown keys skipped
viewpoints.plan_deletes(rows, checked_keys)
    # -> {'keys': minimal keys in tree order, 'guids': [...], 'count': total rows that vanish}
viewpoints.sort_ops(entries)            # entries are (name, is_folder). -> [(from_index, to_index)]
viewpoints.validate_move(rows, checked_keys, target_key)
                                        # -> None when legal, else a reason string
viewpoints.delete_order(keys)           # -> keys sorted so front-to-back deletion never shifts
                                        #    a later index path
```

`op` shapes for `plan_renames`:

| `type` | Keys read | Notes |
|---|---|---|
| `'replace'` | `find`, `replace`, `regex` | With `regex` truthy, `find` is compiled; a `re.error` returns `problems: ['invalid pattern: ...']` and empty renames. An empty `find` without regex is a no-op. |
| `'affix'` | `prefix`, `suffix` | Either may be absent. |
| `'number'` | `pattern` (default `'{n}'`), `pad` (default `0`), `start` (default `1`) | `{n}` is replaced with `str(number).zfill(pad)`. **Folders are skipped entirely and do not consume a number.** |
| `'case'` | `mode`: `'title'`, `'upper'`, anything else means lower | |
| anything else | | `problems: ["unknown operation: ..."]` |

`plan_renames` never auto-fixes. It flags each rename with `collision: True` and reports the
message so the caller can block Apply.

`plan_deletes`: a checked folder stands in for its contents **only when every descendant is
checked too**. A folder with an unchecked descendant survives and only its checked descendants
are deleted.

API half:

```python
viewpoints.transaction(name, doc=None)      # context manager. ONE undo entry.
viewpoints.snapshot(doc=None)               # -> [row], tree order
viewpoints.resolve(saved, guid_text)        # -> live SavedItem, or NONE when it is gone
viewpoints.apply_renames(renames, doc=None, progress=None, cancelled=None)
                                            # -> (applied_count, errors)
viewpoints.apply_deletes(guids, doc=None, progress=None, cancelled=None)
                                            # -> (removed_count, errors)
viewpoints.apply_sort(rows, folder_keys=None, doc=None)     # -> (moved_count, errors)
viewpoints.apply_moves(rows, checked_keys, target_key, doc=None)
                                            # -> (moved_count, errors)
viewpoints.create_folder(name, parent_key='', doc=None)     # -> None
```

`transaction` commits on a clean exit; on an exception it disposes without committing and
re-raises. A document already inside a transaction is left alone (no nesting), and a document
that cannot start one still runs the body, just unbatched. It yields the transaction object or
`None`.

`apply_renames` reads `entry['guid']`, so pass `plan['renames']` straight in.
`apply_deletes` takes **GUIDs**, so pass `plan_deletes(...)['guids']`, not `['keys']`.

`snapshot` descends only folders. An animation is a `GroupItem` but comes out as one row.

**TRAP: `apply_sort` is NOT wrapped in a transaction, unlike `apply_renames`, `apply_deletes`
and `apply_moves`.** Each `Move` is its own undo step, so sorting a large tree produces one
undo entry per move. It is also the one applier that resolves parents by
`ResolveIndexPath`, not by GUID: it sorts deepest folders first so the index paths of parents
still to be resolved do not shift underneath. `create_folder` also uses `ResolveIndexPath`.
Neither takes `progress` or `cancelled`.

`apply_moves` re-resolves by GUID on every step because each `Move` shifts index paths. Items
already directly in the target are skipped, and it returns `(0, [])` when nothing needs to
move. A missing target folder aborts and returns
`(moved_so_far, ['the target folder is no longer there'])`.

Errors are plain strings, already phrased for a user: `"'<old>' is no longer there"`,
`"'<old>': <exception>"`, `'an item is no longer there'`. Toast `errors[0]` with a count.

Canonical apply shape:

```python
applied, errors = viewpoints.apply_renames(plan['renames'])
if errors:
    toast.warning('Renamed %d, %d failed' % (applied, len(errors)), errors[0])
else:
    toast.success('Renamed %d viewpoint(s)' % applied)
```

### viewstate

Import needs nothing beyond the stdlib. The engine behind Copy State / Paste State: three
kinds of view state, copied to `%APPDATA%\pyNavis\viewstate` (one JSON per document, same
identity rule as `memory`) and pasted back later, including in another session of the same
document.

```python
viewstate.VERSION                  # 1. Stamped into every stored entry.
viewstate.KINDS                    # ('section', 'hidden', 'overrides')
viewstate.OVERRIDES_VIEWPOINT      # '.pyNavis Copied Overrides', the reserved saved view
viewstate.label_for(kind)          # -> 'Section State' | 'Hidden Items' | 'Appearance Overrides'
viewstate.kind_for(label)          # -> kind, or None for an unknown label
viewstate.describe(kind, payload)  # -> short toast detail: '6 planes', '12 hidden items'
viewstate.available(store)         # -> kinds a LOADED store carries, canonical order. An entry
                                   #    malformed or from a future VERSION is invisible, never
                                   #    an error.
viewstate.range_ok(rng)            # -> bool. Whether a stored clip range is a usable
                                   #    [[min x,y,z], [max x,y,z]] box. Navisworks encodes a
                                   #    never-set range as an INVERTED box, which reads False.
viewstate.state_root()             # -> '%APPDATA%\pyNavis\viewstate'
viewstate.store_path(doc_path, root=None)   # -> str, the document's store file path
viewstate.load_store(doc_path, root=None)   # -> dict, the whole store; missing or corrupt
                                            #    yields {}
viewstate.save_kind(doc_path, kind, payload, root=None)
                                   # -> str path. Writes one kind's entry, stamping VERSION
                                   #    and the save time.
viewstate.available_kinds(doc=None)   # -> kinds copied for this document, canonical order
viewstate.copy_state(kind, doc=None)  # -> Result. Reads the state, writes the store.
viewstate.paste_state(kind, doc=None) # -> Result. Applies it back, ONE undo step.
```

Has its own `Result` (level/message/detail/count) made to be toasted; never `isinstance` it
against `memory.Result` or `section.Result`.

- `'section'` records every clip plane (origin, normal, state, enabled) or the section box;
  paste realigns planes with `AlignToPick(Custom, ...)`, so a pasted plane reads as Custom
  alignment even if it was authored as Top/Front. Copying with sectioning OFF refuses with
  an info Result ("Nothing to copy") and stores nothing.
- `'hidden'` records the TOPMOST hidden items only; paste is an exact restore
  (`ResetAllHidden` first). Copying with nothing hidden is valid: pasting it unhides all.
- `'overrides'` CANNOT be read item by item - no API exposes applied overrides, and the COM
  node's `IsOverrideMaterial` stays False even for a live override (measured 2026-09-20).
  Copy therefore stores Navisworks' own opaque snapshot
  (`SavedViewpoints.CaptureRuntimeOverrides()`) INSIDE the document as a saved viewpoint
  named `viewstate.OVERRIDES_VIEWPOINT` ('.pyNavis Copied Overrides'), flagged materials-only
  via COM so applying it never touches hidden state. Paste applies it and restores the camera
  and section around the apply. Visible in the Saved Viewpoints tree; survives sessions only
  if the FILE is saved. No overrides applied = info "Nothing to copy", nothing stored.

Hidden entries reuse `memory`'s schema and resolver, so missing items degrade to an info/error
Result with counts, exactly like `memory.recall`. Same-document by design; cross-model paste
works only as far as source-file names match.

### xl

Import needs nothing beyond the stdlib: `zipfile` and `xml.dom.minidom`. Runs unmodified on
IronPython 3.4 and CPython alike, and needs neither Excel nor Navisworks installed.

```python
xl.write(path, rows, headers=None, sheet='Sheet1')
    # rows: each a list of str/int/float/bool/None.
    # headers, if given, written as a bold first row.
    # -> None. Writing to a path that already holds a workbook ADDS the named sheet, or
    #    REPLACES it if that name already exists; every other sheet keeps its name, position
    #    and cell VALUES only. Implemented by reading the whole workbook back and rebuilding
    #    the package from scratch, which is a VALUES-ONLY round trip: an untouched sheet loses
    #    its bold header row and any int written to it comes back as float.

xl.read(path, sheet=None)
    # sheet=None reads the first sheet.
    # -> [[cell, ...], ...]. Numbers as float, booleans as True/False, everything else
    #    (inline AND shared strings) as str. Row length follows the highest column used;
    #    gaps within that span normalise to ''.
    # RAISES ValueError: no sheets in the workbook, or sheet names one that is not in it.

xl.sheets(path)
    # -> [str], sheet names in workbook order.
```

**TRAP: rewriting a file loses formatting on every sheet except the one you just wrote.**
`write` rebuilds the whole package from the plain values `read` hands back for every sheet it
did not touch this call, so an untouched sheet's previously-bold header row comes back plain,
and an int written to it earlier comes back as a float. Only the sheet named in the current
call gets fresh formatting, from the `headers` you pass it this time. Pass `headers=` on every
write to every sheet that must stay bold, and do not depend on an integer round tripping
through a sheet `write` did not touch this call.
