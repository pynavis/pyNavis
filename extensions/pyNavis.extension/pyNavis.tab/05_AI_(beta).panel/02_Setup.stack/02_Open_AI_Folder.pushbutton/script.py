# -*- coding: utf-8 -*-
"""Shows the generated-tools extension in Explorer.

Ask AI writes every tool into AI.extension under the per-user extensions
folder. Until the first tool is created there is nothing to show, and saying
so beats opening an empty Explorer window.
"""

import clr
import os

clr.AddReference('PyNavis.Runtime')
from PyNavis.Runtime.Ai import GeneratedExtensionWriter

from pynavis import script, toast

root = GeneratedExtensionWriter.DefaultRoot

if not os.path.isdir(root):
    toast.info('No AI tools yet', 'Ask AI creates this folder with your first tool')
else:
    panel = os.path.join(root, GeneratedExtensionWriter.TabFolder, GeneratedExtensionWriter.PanelFolder)
    script.show_in_explorer(panel if os.path.isdir(panel) else root)
