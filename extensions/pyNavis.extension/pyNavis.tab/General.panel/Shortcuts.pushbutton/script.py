"""View and rebind pyNavis keyboard shortcuts (writes config.json overrides)."""
import clr

clr.AddReference('PyNavis.Runtime')
from PyNavis.Runtime.Forms import ShortcutsDialog

from pynavis import toast

if ShortcutsDialog.ShowAndSave():
    __pynavis__.Reload()
    toast.success('Shortcuts saved and applied.')
