# Smoke.extension

A worked example of every bundle type and every extension-level hook, used to
exercise the loader by hand after a change. It is not installed for end users:
`tools/install.ps1` deploys `pyNavis.extension` only, and this folder lives under
`tests/fixtures` rather than `extensions` so it never becomes a real ribbon tab.
To load it, add this repository's `tests\fixtures` folder to the `extensions`
array in `config.json`, then press Reload. Take it back out when done.

Read it as a reference alongside `docs/authoring/`, which documents the same
contract in prose.

## What it covers

`Smoke.tab/Buttons.panel` holds one bundle of each ribbon type: a toggle with
on/off art, a smartbutton that sets its own title and icon at ribbon build, a
splitbutton whose header repeats the last child used, a splitpushbutton whose
header always runs the first child, a urlbutton, a linkbutton, a nobutton
reachable only by its chord, and pushbuttons that demonstrate `context:` and
`max_host_version:`.

`Smoke.tab/Dialogs.panel` loads a window from XAML. `Smoke.tab/Panes.panel`
claims a dock panel slot.

`hooks/` carries three live event hooks, `disabled-hooks/` four more that are
renamed rather than deleted so they can be swapped in, and `startup.py` runs at
boot and on every Reload. `Smoke.lib`, a sibling of this folder, is the `.lib`
example: adding or removing one needs a Navisworks restart.

## Two bundles fail on purpose

They are not bugs. Both prove the loader degrades gracefully rather than
crashing, and each says so in its tooltip.

- **Link Missing** points at a plugin id that does not exist. Clicking must
  toast a clear "Plugin not found" warning.
- **Old Only** declares `max_host_version: 2024`. On a newer host the button
  still renders, and clicking must warn and do nothing else.
