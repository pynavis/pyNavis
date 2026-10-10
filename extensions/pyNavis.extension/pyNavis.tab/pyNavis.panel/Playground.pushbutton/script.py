# -*- coding: utf-8 -*-
"""Browse pyNavis Playground and install only the tools you tick.

The catalog (catalog.json, schema 1) comes from GitHub, or from the path or
address in config.json under playground.catalog. Fetching runs off the UI
thread so the window stays responsive; nothing here touches the Navisworks
API. What to copy and what to delete is decided by lib/playground.py, which is
unit-tested; this file only fetches, shows and calls in.

A tick is the state you want: installed tools open ticked, Install adds the
ticked ones that are not there yet, Remove deletes the installed ones you
unticked. Both close the window, reload pyNavis and toast the result, so the
ribbon is never rebuilt underneath an open dialog.
"""

import datetime
import io
import os

import clr

clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')
clr.AddReference('System.Net.Http')
clr.AddReference('PyNavis.Runtime')

from System import Action, TimeSpan
from System.IO import File
from System.Net import SecurityProtocolType, ServicePointManager
from System.Net.Http import HttpClient, HttpClientHandler
from System.Threading import Thread, ThreadStart
from System.Windows import (
    CornerRadius, FontWeights, GridLength, GridUnitType, TextWrapping,
    Thickness, VerticalAlignment, Visibility, Window, WindowStartupLocation,
)
from System.Windows.Controls import (
    Border, CheckBox, ColumnDefinition, ComboBox, Grid, Orientation,
    RowDefinition, ScrollViewer, ScrollBarVisibility, StackPanel, TextBlock, TextBox,
)
from System.Windows.Documents import Hyperlink, Run
from System.Windows.Media import FontFamily

from PyNavis.Runtime import RuntimeHost
from PyNavis.Runtime.Forms import DesignSystem, FluentChrome

import pynavis
from pynavis import forms, script, toast

try:
    import playground as pg       # extension lib/, which pynavis run does not put on the path
except ImportError:
    pg = None

_T = DesignSystem.Tokens.Current
_STATUS_LABELS = [('All statuses', 'All'), ('Untested', 'untested'), ('Launched', 'launched'),
                  ('Verified', 'verified'), ('Known issue', 'known-issue')]
_CHIP = {'untested': ('Untested', _T.Muted), 'launched': ('Launched', _T.Info),
         'verified': ('Verified', _T.Success), 'known-issue': ('Known issue', _T.Warning)}
_CATALOG_TIMEOUT = TimeSpan.FromSeconds(15)
_ZIP_TIMEOUT = TimeSpan.FromMinutes(10)


# ---- network and files (run off the UI thread) ---------------------------------

def _client(timeout):
    """The same client UpdateService uses: TLS 1.2, the user's proxy credentials,
    redirects followed (release downloads redirect to GitHub's CDN)."""
    ServicePointManager.SecurityProtocol = (ServicePointManager.SecurityProtocol
                                            | SecurityProtocolType.Tls12)
    handler = HttpClientHandler()
    handler.UseDefaultCredentials = True
    handler.AllowAutoRedirect = True
    client = HttpClient(handler)
    client.Timeout = timeout
    client.DefaultRequestHeaders.UserAgent.ParseAdd('pyNavis/' + pynavis.__version__)
    return client


def _reason(error):
    """One readable line from a Python or .NET failure."""
    inner = getattr(error, 'clsException', None)
    if inner is not None:
        try:
            base = inner.GetBaseException()
            if 'TaskCanceled' in base.GetType().Name:
                return 'No answer within the time limit.'
            return base.Message
        except Exception:
            pass
    return str(error) or error.__class__.__name__


def _config():
    try:
        return pg.read_config(RuntimeHost.UserConfigPath)
    except Exception:
        return {}


def _fetch_catalog():
    """(kind, location, text) for the configured catalog. Raises on failure."""
    kind, where = pg.catalog_url(_config())
    if kind == 'file':
        with io.open(where, 'r', encoding='utf-8-sig') as f:
            return kind, where, f.read()
    client = _client(_CATALOG_TIMEOUT)
    try:
        return kind, where, client.GetStringAsync(where).Result
    finally:
        client.Dispose()


def _ensure_zip(catalog, kind, where):
    """A local path to the release zip, checked against the catalog's SHA-256.
    A cached zip that fails the check is downloaded again once."""
    expected = catalog['download']['sha256']
    source_kind, source = pg.zip_source(catalog, kind, where)
    if source_kind == 'file':
        if not os.path.isfile(source):
            raise IOError('The zip the catalog names is not there: %s' % source)
        if not pg.verify_sha256(source, expected):
            raise IOError('The zip does not match the checksum in the catalog.')
        return source

    target = pg.zip_cache_path(catalog)
    if os.path.isfile(target) and pg.verify_sha256(target, expected):
        return target
    folder = os.path.dirname(target)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    part = target + '.part'
    client = _client(_ZIP_TIMEOUT)
    try:
        File.WriteAllBytes(part, client.GetByteArrayAsync(source).Result)
    finally:
        client.Dispose()
    if not pg.verify_sha256(part, expected):
        os.remove(part)
        raise IOError('The download does not match the checksum in the catalog.')
    os.replace(part, target)
    return target


# ---- the window ------------------------------------------------------------------

class Browser(object):

    def __init__(self):
        self.catalog = None
        self.kind = None
        self.where = None
        self.installed = []
        self.rows = {}            # id -> (row border, checkbox)
        self.busy = False
        self.outcome = None
        self.window = self._build()

    # -- layout --

    def _build(self):
        window = Window()
        window.Title = 'pyNavis Playground'
        window.Width = 860
        window.Height = 680
        window.MinWidth = 580
        window.MinHeight = 420
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen
        window.Background = DesignSystem.Brush(_T.Surface)
        window.FontFamily = FontFamily('Segoe UI Variable Text, Segoe UI')
        window.FontSize = 13
        window.Foreground = DesignSystem.Brush(_T.Ink)

        grid = Grid()
        grid.Margin = Thickness(16)
        for height in (GridLength.Auto, GridLength.Auto,
                       GridLength(1, GridUnitType.Star), GridLength.Auto):
            row = RowDefinition()
            row.Height = height
            grid.RowDefinitions.Add(row)

        grid.Children.Add(self._toolbar())

        self.status = DesignSystem.Text('Loading the catalog...', 12, _T.Muted)
        self.status.Margin = Thickness(0, 8, 0, 8)
        Grid.SetRow(self.status, 1)
        grid.Children.Add(self.status)

        self.list = StackPanel()
        self.empty = DesignSystem.Text('', 13, _T.Muted)
        self.empty.Margin = Thickness(12)
        self.empty.Visibility = Visibility.Collapsed
        holder = StackPanel()
        holder.Children.Add(self.empty)
        holder.Children.Add(self.list)
        scroll = ScrollViewer()
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        scroll.Content = holder
        DesignSystem.SlimScroll(scroll)
        frame = Border()
        frame.BorderBrush = DesignSystem.Brush(_T.LineStrong)
        frame.BorderThickness = Thickness(1)
        frame.CornerRadius = CornerRadius(4)
        frame.Background = DesignSystem.Brush(_T.Paper)
        frame.Child = scroll
        Grid.SetRow(frame, 2)
        grid.Children.Add(frame)

        footer = self._footer()
        Grid.SetRow(footer, 3)
        grid.Children.Add(footer)

        window.Content = grid
        window.Closing += self._closing
        window.ContentRendered += lambda s, e: self.search.Focus()
        return window

    def _toolbar(self):
        bar = Grid()
        fill = ColumnDefinition()
        fill.Width = GridLength(1, GridUnitType.Star)
        side = ColumnDefinition()
        side.Width = GridLength.Auto
        bar.ColumnDefinitions.Add(fill)
        bar.ColumnDefinitions.Add(side)

        self.search = TextBox()
        field = DesignSystem.InputField(_T, self.search)
        field.Height = 32
        hint = DesignSystem.Text('Search titles, summaries, tabs and panels', 13, _T.Muted)
        hint.Margin = Thickness(9, 0, 0, 0)
        hint.VerticalAlignment = VerticalAlignment.Center
        hint.IsHitTestVisible = False
        holder = Grid()
        holder.Children.Add(field)
        holder.Children.Add(hint)

        def typed(sender, args):
            hint.Visibility = Visibility.Collapsed if self.search.Text else Visibility.Visible
            self._apply_filter()
        self.search.TextChanged += typed
        bar.Children.Add(holder)

        self.filter = ComboBox()
        self.filter.Width = 160
        self.filter.Height = 32
        self.filter.Margin = Thickness(8, 0, 0, 0)
        self.filter.VerticalContentAlignment = VerticalAlignment.Center
        for label, _value in _STATUS_LABELS:
            self.filter.Items.Add(label)
        self.filter.SelectedIndex = 0
        self.filter.SelectionChanged += lambda s, e: self._apply_filter()
        Grid.SetColumn(self.filter, 1)
        bar.Children.Add(self.filter)
        return bar

    def _footer(self):
        footer = Grid()
        footer.Margin = Thickness(0, 12, 0, 0)
        fill = ColumnDefinition()
        fill.Width = GridLength(1, GridUnitType.Star)
        side = ColumnDefinition()
        side.Width = GridLength.Auto
        footer.ColumnDefinitions.Add(fill)
        footer.ColumnDefinitions.Add(side)

        self.count = DesignSystem.Text('', 12, _T.Muted)
        self.count.VerticalAlignment = VerticalAlignment.Center
        footer.Children.Add(self.count)

        buttons = StackPanel()
        buttons.Orientation = Orientation.Horizontal
        self.open_button = DesignSystem.Quiet(_T, 'Open folder', None)
        self.refresh_button = DesignSystem.Quiet(_T, 'Refresh', None)
        self.remove_button = DesignSystem.Danger(_T, 'Remove', None)
        self.install_button = DesignSystem.Primary(_T, 'Install', None)
        self.open_button.Click += lambda s, e: self._open_folder()
        self.refresh_button.Click += lambda s, e: self.load()
        self.remove_button.Click += lambda s, e: self._remove()
        self.install_button.Click += lambda s, e: self._install()
        for i, button in enumerate((self.open_button, self.refresh_button,
                                    self.remove_button, self.install_button)):
            if i:
                button.Margin = Thickness(8, 0, 0, 0)
            buttons.Children.Add(button)
        Grid.SetColumn(buttons, 1)
        footer.Children.Add(buttons)
        return footer

    def _row(self, tool, ticked, installed):
        title = TextBlock()
        title.TextWrapping = TextWrapping.Wrap
        name = Run(tool['title'])
        name.FontWeight = FontWeights.SemiBold
        name.Foreground = DesignSystem.Brush(_T.Ink)
        title.Inlines.Add(name)
        place = Run('   %s > %s' % (tool['tab'], tool['panel']))
        place.Foreground = DesignSystem.Brush(_T.Muted)
        place.FontSize = 12
        title.Inlines.Add(place)

        check = CheckBox()
        check.Content = title
        check.IsChecked = ticked
        check.Foreground = DesignSystem.Brush(_T.Ink)
        check.VerticalContentAlignment = VerticalAlignment.Top
        check.Checked += lambda s, e: self._update_counts()
        check.Unchecked += lambda s, e: self._update_counts()

        text = StackPanel()
        text.Children.Add(check)
        if tool['summary']:
            summary = DesignSystem.Text(tool['summary'], 12.5, _T.Muted)
            summary.Margin = Thickness(20, 2, 0, 0)
            text.Children.Add(summary)
        meta = self._meta(tool, installed)
        if meta is not None:
            text.Children.Add(meta)

        label, color = _CHIP.get(tool['status'], _CHIP['untested'])
        chip = Border()
        chip.BorderBrush = DesignSystem.Brush(color)
        chip.BorderThickness = Thickness(1)
        chip.CornerRadius = CornerRadius(4)
        chip.Padding = Thickness(6, 1, 6, 1)
        chip.Margin = Thickness(12, 0, 0, 0)
        chip.VerticalAlignment = VerticalAlignment.Top
        chip.Child = DesignSystem.Text(label, 11.5, color)

        grid = Grid()
        fill = ColumnDefinition()
        fill.Width = GridLength(1, GridUnitType.Star)
        side = ColumnDefinition()
        side.Width = GridLength.Auto
        grid.ColumnDefinitions.Add(fill)
        grid.ColumnDefinitions.Add(side)
        grid.Children.Add(text)
        Grid.SetColumn(chip, 1)
        grid.Children.Add(chip)

        row = Border()
        row.Padding = Thickness(12, 8, 12, 8)
        row.BorderBrush = DesignSystem.Brush(_T.Line)
        row.BorderThickness = Thickness(0, 0, 0, 1)
        row.Child = grid
        return row, check

    def _meta(self, tool, installed):
        """The quiet line under a summary: installed, stack mates, a known
        issue's note, and the Ask link to the request behind the tool."""
        parts = []
        if installed:
            parts.append('Installed')
        note = pg.sibling_note(self.catalog, tool)
        if note:
            parts.append(note)
        if tool['status'] == 'known-issue' and tool.get('status_note'):
            parts.append(tool['status_note'])
        source = tool.get('source') if isinstance(tool.get('source'), dict) else {}
        url = pg.web_url(source.get('url'))
        if not parts and not url:
            return None
        line = TextBlock()
        line.TextWrapping = TextWrapping.Wrap
        line.FontSize = 12
        line.Foreground = DesignSystem.Brush(_T.Muted)
        line.Margin = Thickness(20, 4, 0, 0)
        if parts:
            text = '. '.join(p.strip().rstrip('.') for p in parts) + '.'
            line.Inlines.Add(Run(text + ('   ' if url else '')))
        if url:
            link = Hyperlink(Run('Ask'))
            link.Foreground = DesignSystem.Brush(_T.Ink)
            quote = source.get('quote') or ''
            where = ', '.join([p for p in (source.get('site'), source.get('date')) if p])
            link.ToolTip = ('"%s"\n%s' % (quote, where)).strip() if quote else (where or url)
            link.Click += lambda s, e: script.open_url(url)
            line.Inlines.Add(link)
        return line

    # -- state --

    def _set_busy(self, busy, message=None):
        self.busy = busy
        if message is not None:
            self.status.Text = message
        for button in (self.refresh_button, self.install_button, self.remove_button):
            button.IsEnabled = not busy
        if not busy:
            self._update_counts()

    def _ticked(self):
        return set(tid for tid, (row, check) in self.rows.items() if check.IsChecked)

    def _update_counts(self):
        if self.catalog is None:
            self.count.Text = ''
            self.install_button.IsEnabled = False
            self.remove_button.IsEnabled = False
            return
        ticked = self._ticked()
        installed = set(self.installed)
        adding = pg.install_plan(self.catalog, ticked, installed)['tools']
        removing = pg.remove_plan(self.catalog, installed - ticked, installed)['tools']
        bits = ['%d selected' % len(ticked)]
        if adding:
            bits.append('%d to install' % len(adding))
        if removing:
            bits.append('%d to remove' % len(removing))
        self.count.Text = ', '.join(bits)
        blocked = pg.needs_newer_pynavis(self.catalog, pynavis.__version__)
        self.install_button.IsEnabled = bool(adding) and not self.busy and not blocked
        self.remove_button.IsEnabled = bool(removing) and not self.busy

    def _apply_filter(self):
        if self.catalog is None:
            return
        status = _STATUS_LABELS[max(0, self.filter.SelectedIndex)][1]
        shown = set(t['id'] for t in pg.filter_tools(self.catalog['tools'], self.search.Text, status))
        for tid, (row, check) in self.rows.items():
            row.Visibility = Visibility.Visible if tid in shown else Visibility.Collapsed
        self.empty.Text = 'No tool matches the search.'
        self.empty.Visibility = Visibility.Collapsed if shown else Visibility.Visible

    def _show(self, catalog, kind, where, note):
        keep = self._ticked() if self.catalog is not None else None
        self.catalog, self.kind, self.where = catalog, kind, where
        self.installed = pg.installed_on_disk(catalog, pg.extension_dir(catalog))
        self.rows = {}
        self.list.Children.Clear()
        for tool in catalog['tools']:
            installed = tool['id'] in self.installed
            ticked = installed if keep is None else (tool['id'] in keep or installed)
            row, check = self._row(tool, ticked, installed)
            self.rows[tool['id']] = (row, check)
            self.list.Children.Add(row)
        summary = '%s %s, %d tools, %d installed. Every tool here is experimental.' % (
            catalog['name'], catalog['version'], len(catalog['tools']), len(self.installed))
        if pg.needs_newer_pynavis(catalog, pynavis.__version__):
            summary = ('This catalog needs pyNavis %s or newer to install from. Update pyNavis first.'
                       % catalog.get('min_pynavis'))
        self.status.Text = (note + ' ' + summary) if note else summary
        self._apply_filter()
        self._update_counts()

    # -- actions --

    def load(self):
        self._set_busy(True, 'Loading the catalog...')

        def done(result, error):
            self._set_busy(False)
            if error is None:
                kind, where, text = result
                try:
                    catalog = pg.parse_catalog(text)
                except pg.CatalogError as bad:
                    self._fallback('The catalog could not be used', str(bad))
                    return
                try:
                    pg.save_cached(text)
                except Exception as cache_error:
                    script.get_logger().warning('catalog not cached: %s' % _reason(cache_error))
                self._show(catalog, kind, where, '')
                return
            kind, where = pg.catalog_url(_config())
            if kind == 'file':
                self._fallback('The catalog file could not be read', _reason(error))
            else:
                self._fallback('GitHub could not be reached', _reason(error))

        self._background(_fetch_catalog, done)

    def _fallback(self, message, detail):
        script.get_logger().warning('%s: %s' % (message, detail))
        toast.error(message, detail)
        cached = pg.load_cached()
        if cached is not None:
            self._show(cached, 'url', pg.DEFAULT_CATALOG_URL,
                       'Showing the catalog saved on this PC.')
        elif self.catalog is None:
            self.status.Text = 'No catalog yet. Check the connection, then click Refresh.'
            self._update_counts()

    def _install(self):
        installed = set(self.installed)
        plan = pg.install_plan(self.catalog, self._ticked(), installed)
        if not plan['tools']:
            return
        catalog, kind, where = self.catalog, self.kind, self.where
        target = pg.extension_dir(catalog)
        self._set_busy(True, 'Downloading and installing %s...' % _tools(len(plan['tools'])))

        def work():
            zip_path = _ensure_zip(catalog, kind, where)
            pg.install_files(zip_path, plan, target)
            state = os.path.join(pg.data_dir(), 'installed.json')
            pg.write_installed(state, pg.record_install(
                pg.read_installed(state), catalog, plan['tools'],
                datetime.date.today().isoformat()))
            return plan

        def done(result, error):
            self._set_busy(False)
            if error is not None:
                script.get_logger().error('playground install failed: %s' % _reason(error))
                toast.error('The tools could not be installed', _reason(error))
                self._show(self.catalog, self.kind, self.where, '')
                return
            self.outcome = ('installed', result)
            self.window.Close()

        self._background(work, done)

    def _remove(self):
        installed = set(self.installed)
        plan = pg.remove_plan(self.catalog, installed - self._ticked(), installed)
        if not plan['tools']:
            return
        if not forms.confirm('Remove %s? Their folders are deleted, with any changes '
                             'you made to them.' % _tools(len(plan['tools'])),
                             'pyNavis Playground'):
            return                                  # cancelled: say nothing
        try:
            pg.remove_files(pg.extension_dir(self.catalog), plan)
            state = os.path.join(pg.data_dir(), 'installed.json')
            pg.write_installed(state, pg.record_remove(pg.read_installed(state), plan['tools']))
        except Exception as error:
            script.get_logger().error('playground remove failed: %s' % _reason(error))
            toast.error('The tools could not be removed', _reason(error))
            self._show(self.catalog, self.kind, self.where, '')
            return
        self.outcome = ('removed', plan)
        self.window.Close()

    def _open_folder(self):
        target = pg.extension_dir(self.catalog)
        if os.path.isdir(target):
            script.show_in_explorer(target)
        else:
            toast.info('Nothing is installed yet', 'Tick a tool and click Install')

    def _closing(self, sender, args):
        if self.busy and self.outcome is None:
            args.Cancel = True

    def _background(self, work, done):
        """Runs work() off the UI thread, then done(result, error) back on it."""
        dispatcher = self.window.Dispatcher

        def body():
            result, error = None, None
            try:
                result = work()
            except Exception as ex:
                error = ex
            dispatcher.BeginInvoke(Action(lambda: self._finish(done, result, error)))

        thread = Thread(ThreadStart(body))
        thread.IsBackground = True
        thread.Start()

    def _finish(self, done, result, error):
        # Raised here, a bug would surface far from its cause and leave the
        # window stuck busy, so it is logged, toasted and the window freed.
        try:
            done(result, error)
        except Exception as bug:
            script.get_logger().error('playground: %s' % _reason(bug))
            toast.error('Playground hit an error', _reason(bug))
            try:
                self._set_busy(False)               # Refresh too, not just Install and Remove
            except Exception:
                self.busy = False

    def show(self):
        FluentChrome.Apply(self.window)
        self.window.Loaded += lambda s, e: self.load()
        self.window.ShowDialog()
        return self.outcome


def _tools(count):
    return '%d tool%s' % (count, '' if count == 1 else 's')


def _tabs(catalog, ids):
    by_id = pg.tool_by_id(catalog)
    tabs = []
    for tid in ids:
        tab = by_id[tid]['tab']
        if tab and tab not in tabs:
            tabs.append(tab)
    return tabs


def run():
    if globals().get('__runner__') or script.is_runner():
        toast.info('Playground opens a window', 'Run it from the ribbon, not from pynavis run')
        return
    browser = Browser()
    outcome = browser.show()
    if outcome is None:
        return                                      # closed: say nothing
    kind, plan = outcome
    script.reload_pynavis()
    if kind == 'installed':
        tabs = _tabs(browser.catalog, plan['tools'])
        detail = 'Find them on the %s tab%s. They are experimental.' % (
            pg.join_and(tabs), '' if len(tabs) == 1 else 's') if tabs else 'They are experimental.'
        toast.success('Installed %s' % _tools(len(plan['tools'])), detail)
    else:
        toast.success('Removed %s' % _tools(len(plan['tools'])))


if '__commandpath__' in globals():
    run()
