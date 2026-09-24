"""Smoke fixture, PARKED: fails on purpose, so it lives outside hooks\\ where
nothing scans it.

To re-run the hook-health check, copy this file into Smoke.extension\\hooks\\ and
Reload, then save the document three times: the third consecutive failure toasts
"disabled after 3 failures", the log holds all three tracebacks, and further saves
stay silent until the next Reload. Delete it from hooks\\ again afterwards.
"""
1 / 0
