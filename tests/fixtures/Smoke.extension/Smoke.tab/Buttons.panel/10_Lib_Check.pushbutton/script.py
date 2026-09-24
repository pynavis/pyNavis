"""Proves a sibling *.lib folder is on the import path for this extension."""
from pynavis import toast

import smoke_libcheck

toast.success(smoke_libcheck.greeting())
