# Changelog

All notable changes to pyNavis, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the numbers follow
[Semantic Versioning](https://semver.org/): PATCH for fixes, MINOR for anything new (a tool,
a `bundle.yaml` or `config.json` key, a `pynavis` function, a Navisworks year), MAJOR when
something existing changes meaning or goes away. The number in each heading is the one that
`pynavis.__version__`, the Settings window and the installer file name show.

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
