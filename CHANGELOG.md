# Changelog

All notable changes to pyNavis, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the numbers follow
[Semantic Versioning](https://semver.org/): PATCH for fixes, MINOR for anything new (a tool,
a `bundle.yaml` or `config.json` key, a `pynavis` function, a Navisworks year), MAJOR when
something existing changes meaning or goes away. The number in each heading is the one that
`pynavis.__version__`, the Settings window and the installer file name show.

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
