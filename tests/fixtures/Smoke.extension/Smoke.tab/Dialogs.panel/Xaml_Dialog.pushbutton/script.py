"""XAML window demo: loads layout.xaml from this bundle, wires events in
python, and shows it modal over Navisworks."""
from pynavis import forms, toast

win = forms.WPFWindow('layout.xaml')


def _ok(sender, args):
    win.close(True)


win['OkButton'].Click += _ok

if win.show_dialog():
    text = win['NameBox'].Text
    toast.success('XAML dialog OK: %s' % (text or '(empty)'))
