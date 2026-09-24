"""Script logger (internal): level-filtered sink writing to the pyNavis log.

Pure at the core - the sink is injectable so it unit-tests without a host.
script.get_logger() wires the real sink (PyNavisHost.LogMessage plus a
colored output-window line for warning/error).
"""

_LEVELS = {'debug': 0, 'info': 1, 'warning': 2, 'error': 3}


class Logger(object):
    def __init__(self, source, sink):
        self._source = source
        self._sink = sink
        self._threshold = _LEVELS['info']

    def set_level(self, level):
        """Lowest level that gets through: 'debug', 'info', 'warning', 'error'."""
        self._threshold = _LEVELS[level]

    def _emit(self, level, message):
        if _LEVELS[level] >= self._threshold:
            self._sink(level, self._source, str(message))

    def debug(self, message):
        self._emit('debug', message)

    def info(self, message):
        self._emit('info', message)

    def success(self, message):
        # success is info-level with its own tag so sinks can style it
        if _LEVELS['info'] >= self._threshold:
            self._sink('success', self._source, str(message))

    def warning(self, message):
        self._emit('warning', message)

    def error(self, message):
        self._emit('error', message)
