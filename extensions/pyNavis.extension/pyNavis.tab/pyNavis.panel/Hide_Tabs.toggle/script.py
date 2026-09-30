"""Takes the chosen tabs off the ribbon, or puts them back.

What is off the ribbon decides, not the button's own on/off state, so the
two can never disagree: tabs off means this click puts them back, anything
else means it takes them off. With no chosen tab on the ribbon, the picker
opens first (the Shift+Click one). The tabs stay off for the session and
go back when Navisworks closes (hooks/app-closing.py). See lib/tabhider.py.
"""

import tabhider

from pynavis import script, settings, toast


def run():
    if tabhider.hidden():
        count = tabhider.show()
        script.set_toggle_state(False)
        toast.info(tabhider.shown_message(count))
        return

    chosen = settings.load(tabhider.TOOL, tabhider.DEFAULTS)['tabs']
    if not tabhider.to_hide(tabhider.tab_list(), chosen):
        chosen = tabhider.choose()
        if chosen is None:
            script.set_toggle_state(False)
            return                          # cancelled: say nothing
    titles = tabhider.hide(chosen)
    script.set_toggle_state(bool(titles))
    if titles:
        title, detail = tabhider.hidden_message(titles)
        toast.success(title, detail)


run()
