# Changelog

All notable changes to pyNavis, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the numbers follow
[Semantic Versioning](https://semver.org/): PATCH for fixes, MINOR for anything new (a tool,
a `bundle.yaml` or `config.json` key, a `pynavis` function, a Navisworks year), MAJOR when
something existing changes meaning or goes away. The number in each heading is the one that
`pynavis.__version__`, the Settings window and the installer file name show.

## [1.4.0] - 2026-10-11

### Added

- Telemetry, off by default: with it on, pyNavis writes one record for every tool run (a
  click, a Shift+Click, a `pynavis run` script, and hooks when `includeHooks` is on) to a
  shared folder, an HTTP endpoint, or both, never to the pyNavis developers. A record holds
  the user, the machine, the model's file name, the tool, how long it took, whether it
  worked and one line of any error. Records queue in memory and a background worker sends
  them, keeping what it cannot deliver in a spool to retry, so a click never waits. Set it
  in the new Telemetry section of Settings, which applies a change at once and has a Send
  test record button, or in the `telemetry` section of `config.json`. A firm can deploy
  `%PROGRAMDATA%\pyNavis\telemetry.json`, which overrides every user's settings when only
  administrators can change it.
- `pynavis.telemetry.note(**values)` adds a tool's own values, such as items changed or
  minutes saved, to its run's record. Outside a run, or with telemetry off, it does nothing.
- `pynavis run` results.json carries `cancelled: true` when Ctrl+C ended the run, and models
  that never started say so instead of blaming an earlier model.
- Memory entries record the model's full file path beside its file name, so two appended
  models with the same name stay distinct through Remember, Add, Subtract, Intersect and
  Recall. Memory files written before this release still load and resolve as before.

### Changed

- `pynavis.xl.write` refuses a sheet name Excel would reject (empty, over 31 characters,
  containing `[ ] : * ? / \`, or starting or ending with an apostrophe) with a `ValueError`
  before writing anything, writes infinite and NaN numbers as the text `inf`, `-inf` and `nan`
  instead of producing a workbook Excel has to repair, and replaces or reads a sheet whose
  name differs only in case, as Excel does.
- `pynavis.script.clipboard_copy` returns `True` or `False` instead of raising when another
  program holds the clipboard, and the tools that copy text toast the failure.
- `pynavis.memory.load` treats a corrupt, truncated or newer-version memory file as empty,
  logs why, and lets Show Contents say the file was ignored, instead of failing every memory
  button until the file is deleted.
- `pynavis extensions add` refuses to touch a `config.json` it cannot read and writes the file
  atomically, instead of replacing a hand-edited or damaged file with only the extension list.
- Selection-set folders whose names contain a slash export with the slash escaped as `\/`, so
  Excel Sets round-trips them to one folder instead of two.

### Fixed

- The uninstaller's backup path carried a stray control character, which made the backup
  step fail for anyone who had edited the shipped extension; a test now scans every script
  under `tools` for control characters.
- `install.ps1` backs up and keeps user additions to `pyNavis.extension` the way the setup
  program does, and writes the manifest, instead of deleting the folder.
- Clash Grouper: Shift+Click failed with a NameError once a custom rule chain had been saved;
  an invalid cluster distance now blocks Preview and Apply instead of silently using 2 m;
  closing the window stops its background counting; a failed preview is logged with its
  traceback; Ungroup reports through toasts.
- The viewpoint Deleter, Reset, Renamer and Manager dialogs and the Clash Grouper refuse to
  open under `pynavis run` instead of hanging the hidden Navisworks on an invisible window.
- Viewpoint dialogs: selecting a whole folder raises one change instead of one per row, and
  the folder rail refreshes in one pass, roughly ten times faster on large viewpoint trees.
- Export Viewpoints CSV and Clash Report write UTF-8 with a byte order mark, so names outside
  the Windows code page no longer stop the export part way through.
- A script that calls `script.reload_pynavis()` on IronPython keeps its own search path and
  output window after the Reload; before, a nested run reset both.
- Dock pane slots survive a satellite DLL that fails to load, instead of being reassigned and
  the original claims lost.
- Printing tens of thousands of lines no longer slows the output transcript quadratically.
- Update check: Windows credentials go to the proxy rather than to GitHub, superseded
  installers are deleted from the updates folder, and Check now waits for a check already
  running instead of showing stale text.
- A hooks folder that cannot be listed costs only its hooks, not the whole extension root;
  a non-string shortcut binding in `config.json` is reported instead of silently disabling
  the tool.
- Settings saves `config.json` before the API key, so a failed save cannot leave the key
  bound to settings that were never written.
- Ask AI: a server that never answers is reported as a failure and keeps your question,
  instead of being treated as a cancel; Send last error can be retried after a failed send;
  the pane follows a theme or accent change on Reload.
- Toasts and banners no longer leave a timer running after a click dismissed them; message
  dialogs with long text scroll instead of pushing their buttons off screen; relative image
  paths in bundle XAML resolve against the bundle folder; nested progress scopes keep
  Navisworks disabled until the outer one ends; the output window's Copy and Save report a
  failure instead of raising on the Navisworks thread.
- Markdown in the output window keeps asterisks inside inline code and escapes quotes in
  link URLs; `pynavis.xl.write` and `_query.validate` report a bad value instead of raising
  a TypeError; several strings move to sentence case.
- `deploy-dev.ps1` merges extension roots instead of overwriting the list; the installer no
  longer ships `__pycache__` folders.

## [1.3.0] - 2026-10-10

### Added

- `pynavis run`: runs a script in a hidden Navisworks from the command line, once per model
  you list, and writes a results summary. `pynavis run script.py model.nwd` is the whole
  command; `--save` writes each `.nwd` or `.nwf` model back, `--show` shows the window, `--timeout` guards
  against a stuck dialog, `--results` copies the summary. Scripts see `__runner__`,
  `__model__`, `__models__`, `__args__` and `__run_dir__`, may set `__result__` to report a
  value, and under the runner toasts and output go to a transcript file while dialogs
  raise instead of waiting for a click nobody can give. A Navisworks the runner starts skips
  the ribbon, hooks, notices and update check; `"automatedBoot": "full"` in `config.json`
  boots it like a normal session instead.
- `status: experimental` in `bundle.yaml` marks a tool as experimental on any bundle with a
  caption, pulldowns and dock panes included: a Greek beta (β) joins the ribbon caption and
  the tooltip opens with "Experimental:". Any other value is reported as a bundle problem
  and ignored.
- Playground on the pyNavis panel browses pyNavis Playground, a separate catalog of
  experimental tools: search it, filter by status (untested, launched, verified, known issue),
  read the request behind each tool, and install only the tools you tick into
  `%APPDATA%\pyNavis\extensions\Playground.extension`, or remove them again. The download is
  checked against the catalog's SHA-256, and a stack member brings the rest of its stack.
  `playground.catalog` in `config.json` (set with Shift+Click) points it at another catalog
  address or a local `catalog.json`.

### Fixed

- `pynavis.pick` refuses under `pynavis run`, as the dialogs already did: `point`, `point_then`,
  `measure_point` and `measure_points` raise "Dialogs are not available under pynavis run"
  instead of putting the hidden Navisworks on a pick tool and waiting for a click until the
  timeout (found running the Playground tools headless).
- Lines and labels drawn in the view (`pynavis.overlay`: the True Distance dimension, the Go
  to Coordinates marker) land on their points on every machine and on models far from the
  origin. Before, a label could sit mirrored about the middle of the view, and on a model in
  shared coordinates the lines could be missing altogether.
- Navisworks no longer crashes as it closes after a script has shown the output window.
  The window's browser is now released when the window closes, or when Navisworks does,
  instead of being left for Windows to clean up at exit.
- The Ask AI key is only sent to the endpoint it was saved for, and only over https or to a
  server on this computer. Before, switching provider or pointing the panel at a local model
  server on another machine sent the saved key there, in clear text over http. A key saved
  by 1.2.0 is kept for the endpoint your settings already use. Settings has a new Remove
  button that deletes the key.
- `%PROGRAMDATA%\pyNavis\extensions` and a machine-wide runtime in `%PROGRAMDATA%` load only
  when no one but administrators can write to them. Any user can create folders there, so on
  a shared PC one user could otherwise make code run in everyone's Navisworks.
- An update no longer fails, with an error box, when you close one of two Navisworks
  windows: it waits for the last one, only counts Navisworks in your own Windows session,
  and stops after three failed attempts with a toast pointing at the release page.
- A hook that causes its own event (a `viewpoints-changed` hook renaming a viewpoint, a
  `camera-moved` hook setting the view) no longer runs itself again. Events a hook causes
  run no hooks, so it cannot loop or overflow the stack and take Navisworks down.
- Uninstalling backs up your own work in `pyNavis.extension` first, as an update does, and
  copies the buttons and panels you added to `My pyNavis.extension`, which uninstall keeps.
- The installer's check for an old copy in the Navisworks program folder now covers 2027.
- `pynavis.props.get` and `props.all_of` read length, area, volume and angle properties,
  which most Revit geometry carries, as floats in document units. Before, they raised
  "Not supported if '!IsDouble'" on any item with one. `pynavis.sets` reads a search
  condition's value the same way.

## [1.2.0] - 2026-09-29

### Added

- Updates. Once a day pyNavis asks GitHub for its latest release, in the background and
  with one anonymous request. When a newer one is out it downloads the setup, keeps it only
  when its SHA-256 matches the release's, and installs it quietly when Navisworks closes.
  Settings has a new Updates section with Check now, the release page, Skip this version,
  and switches for the daily check and for installing on close (`updates` in
  `config.json`).
- Your work in `pyNavis.extension` survives an update. Before the installer replaces that
  folder it backs up anything you added or edited to `%APPDATA%\pyNavis\backups`, and moves
  the buttons, panels, `lib` modules and hooks you added to `My pyNavis.extension`, where
  they stay on the ribbon. A toast after the update says what was kept, and a warning at
  startup says when there is work of yours in the folder updates replace. The command line
  does the work (`pynavis protect-extension`, `pynavis write-manifest`), and every release
  now lists its files in `.pynavis-manifest`.
- Tabs and panels with the same name in two extensions join on the ribbon, so your own
  extension can add a panel to the pyNavis tab, or buttons to one of its panels. The
  shipped pyNavis extension's items come first; the same extension found in two roots
  still loads once.
- Hide Tabs (pyNavis panel): takes the tabs you choose off the ribbon for a clean screen or
  a recording, add-in tabs included, which the ribbon's own Show Tabs cannot keep hidden.
  Click again, or close Navisworks, and they come back where they were.
- Clear Clash works with pipes and conduits. Against a flat face, the round object is
  measured to its real surface where it is in front of that face; two straight pipes are
  measured axis to axis, less both radii, so the gap between two pipes on a rack is the
  air between them.
- Every length field reads lengths the way Revit does: `1' 6"`, `1'-6 1/2"`, `6 1/2"`,
  `3/4"`, `1 6`, `1-6`, a bare fraction as inches, a bare number in the document's unit,
  and a value with its unit such as `25mm` in any document. That covers Clear Clash's
  gap, Section Fit's padding, Section Nudge's step, the Clash Grouper's cluster distance
  and Go to Coordinates. For scripts: `pynavis.lengths` and `forms.ask_length`.
- `title_on` and `tooltip_on` in a toggle's `bundle.yaml`: the caption and tooltip while
  it is on. Hide Tabs reads Show Tabs while pressed.
- `forms.select_from_list(..., checked=[...])` opens with those items ticked.
- `faces.surface_under`, `faces_under(..., floor=)`, `geometry.float_noise` and
  `geometry.world_triangles(..., stats=)`.

### Changed

- Clear Clash and Set Gap are one button, Clear Clash, and it works the way Set Gap did.
  After the two faces, a prompt shows the gap as it is now and asks for the gap to leave,
  opening on the last one you gave, so a clash review stays at two clicks and Enter. The
  object slides the exact difference, tighter or apart, clash or no clash. The first
  prompt opens on the clearance you set with Shift+Click, if you set one.
- The face finder behind True Distance and Clear Clash works to each object's own
  precision. Far from the origin it used to count every face within several inches of a
  click; it now works to fractions of a millimetre wherever the geometry allows.
- Delete's description no longer claims Navisworks deletes viewpoints one at a time.

### Removed

- Set Gap, now part of Clear Clash.
- Clear Clash's Shift+Click. The prompt sets the gap on every run.
- Section Clear. Navisworks' own Enable Sectioning toggle does the same; `section.clear()`
  stays for scripts.

## [1.1.0] - 2026-09-28

### Added

- `layout:` lists. A container's own yaml (`bundle.yaml` in a tab, panel, stack, pulldown,
  split button or slideout; `extension.yaml` for the tabs) names its children in the order
  they should render. Unlisted children follow in name order, and an entry with no matching
  folder is reported and skipped.
- GitHub issue forms for bug reports, feature requests and authoring questions, with
  automatic area and Navisworks-version labels.
- This changelog, and a release procedure in the README.

### Changed

- The shipped extension's folders no longer carry numeric `NN_` prefixes, so inserting a
  tool no longer means renaming its neighbours. A legacy prefix on your own bundles still
  sorts and is still stripped from the title.
- Bundle keys in `config.json` (shortcut bindings and panel slot assignments) are the
  prefix-free extension-relative path. Keys written by 1.0.0 are normalised when the file
  is read, so existing overrides keep working.
- The Ask AI authoring guide teaches `layout:` instead of numeric prefixes.
- The after-scan toast counts bundle problems rather than skipped folders, since a bad
  `layout:` entry skips nothing.
- The version must be plain MAJOR.MINOR.PATCH and this file must lead with it; the tests
  and `tools\package.ps1` refuse a mismatch.

## [1.0.0] - 2026-09-27

First public release.

- A pyNavis ribbon tab for Navisworks Manage and Simulate 2023 to 2027 with the pyNavis,
  Selection, Clash, Viewpoints, AI (beta) and Data panels.
- Bundle authoring in the pyRevit style: folders holding `bundle.yaml` and `script.py` for
  twelve kinds of button and panel, event hooks, dock panes, keyboard shortcuts, IronPython
  by default and CPython on request, and the `pynavis` library behind them.
- Ask AI (beta): a dock panel that drafts new bundles from a description, using your own
  API key.
- A per-user setup program that needs no administrator rights, and the `pynavis` command
  line.
