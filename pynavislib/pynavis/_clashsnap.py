"""Pure caching helpers for building clash snapshots. No Navisworks imports.

A test with tens of thousands of results reads the same things over and over:
67,000 clashes usually touch far fewer distinct elements, and their centres
fall into far fewer cells (the caller picks the edge length) than there are
clashes. These memos turn those repeats into dictionary hits. Nothing here knows what a ModelItem is,
which is what makes it testable outside a Navisworks session (pynavis.clash
itself cannot even be imported there).
"""


class Memo(object):
    """Calls read(subject) once per key, then replays the answer.

    key_of returns None for a subject that cannot be keyed (an unknown
    element, a result with no centre). Those always fall through to read, so a
    missing key never poisons the cache with one wrongly shared answer.
    """

    def __init__(self, key_of, read):
        self._key_of = key_of
        self._read = read
        self._values = {}
        self.hits = 0
        self.misses = 0

    def get(self, subject):
        key = self._key_of(subject)
        if key is None:
            self.misses += 1
            return self._read(subject)
        if key in self._values:
            self.hits += 1
            return self._values[key]
        self.misses += 1
        value = self._read(subject)
        self._values[key] = value
        return value


def cell_key(center, size):
    """Quantises a centre to an integer cell of the given edge length.

    Two centres in the same cell are at most size*sqrt(3) apart, which is why
    callers share only coarse-but-local answers this way. Returns None for a
    missing centre or a non-positive size, so the caller falls through to a
    real lookup instead of sharing one bogus cell.
    """
    if not center or not size or size <= 0:
        return None
    x, y, z = center
    return (int(x // size), int(y // size), int(z // size))
