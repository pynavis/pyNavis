"""Atomic text-file writes (internal): temp file beside the target, then replace.

A plain open(path, 'w') truncates first, so a crash, a full disk or a scan
mid-write leaves an empty or half-written file where the user's data was.
Pure stdlib so it tests anywhere.
"""
import os


def write_text(path, text):
    """Replaces path with text, or leaves path exactly as it was on failure."""
    temp = path + '.tmp'
    try:
        handle = open(temp, 'w')
        try:
            handle.write(text)
        finally:
            handle.close()
        try:
            os.replace(temp, path)
        except AttributeError:      # very old runtimes have no os.replace
            if os.path.exists(path):
                os.remove(path)
            os.rename(temp, path)
    finally:
        if os.path.exists(temp):
            try:
                os.remove(temp)
            except OSError:
                pass
