"""Console command history (pure python - unit-tested through the engine)."""


class CommandHistory(object):
    """REPL-style history: previous()/next() walk entries newest-first; walking
    past the newest returns '' (the empty draft); adding resets navigation and
    collapses consecutive duplicates.
    """

    def __init__(self):
        self._entries = []
        self._cursor = None  # None = not navigating

    def add(self, command):
        if not command:
            return
        if not self._entries or self._entries[-1] != command:
            self._entries.append(command)
        self._cursor = None

    def previous(self):
        if not self._entries:
            return ''
        if self._cursor is None:
            self._cursor = len(self._entries) - 1
        elif self._cursor > 0:
            self._cursor -= 1
        return self._entries[self._cursor]

    def next(self):
        if self._cursor is None:
            return ''
        self._cursor += 1
        if self._cursor >= len(self._entries):
            self._cursor = None
            return ''
        return self._entries[self._cursor]
