# Test fixtures

`Smoke.extension` and `Smoke.lib` exercise every bundle kind, hook and engine seam
pyNavis supports. They live here rather than under `extensions/` on purpose: that
folder is the scanned root, so anything in it becomes a real ribbon tab in every
user's Navisworks, and a tab full of demo buttons is not something to ship.

To run the smoke pass, point a pyNavis extension root at this folder:

```json
{ "extensions": ["...\pyNavis\extensions", "...\pyNavis\tests\fixtures"] }
```

Reload, work through the checklist, then take the root back out. `Smoke.lib` is a
`*.lib` sibling, so adding or removing it needs a Navisworks restart rather than a
Reload.
