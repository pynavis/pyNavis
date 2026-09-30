"""pynavis - the pyNavis runtime library for Navisworks python scripts.

Deliberately light at import time: no Navisworks API references live here, so the
package imports (and unit-tests) outside Navisworks. API-bound helpers live in the
submodules (pynavis.app, pynavis.doc, ...) which import the API when first used.
"""

__version__ = '1.2.0'
