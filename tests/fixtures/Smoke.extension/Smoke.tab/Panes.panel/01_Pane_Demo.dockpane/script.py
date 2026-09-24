"""Smoke test: proves a dockpane's XAML, script wiring and keyboard all work."""
import datetime

pane = __pane__

pane.Find('Built').Text = 'Built at ' + datetime.datetime.now().strftime('%H:%M:%S')

typed = [0]


def on_key(sender, args):
    typed[0] += 1
    pane.Find('Keys').Text = 'Keys seen: %d (last %s)' % (typed[0], args.Key)


def on_size(sender, args):
    pane.Find('Sizes').Text = 'Size: %d x %d' % (args.NewSize.Width, args.NewSize.Height)


def on_close(sender, args):
    pane.Visible = False


pane.Find('Typing').PreviewKeyDown += on_key
pane.Content.SizeChanged += on_size
pane.Find('Close').Click += on_close
