"""Puts back any tabs Hide Tabs took off the ribbon before Navisworks closes,
so its own saved ribbon layout never records them as missing."""

import tabhider

if tabhider.hidden():
    tabhider.show()
