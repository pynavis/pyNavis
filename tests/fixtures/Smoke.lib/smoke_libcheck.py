"""Lives in Smoke.lib, a sibling of Smoke.extension: importable from every
extension and both engines. Adding or removing this FOLDER needs a Navisworks
restart; edits to this file only need a Reload."""


def greeting():
    return 'smoke_libcheck imported from Smoke.lib'
