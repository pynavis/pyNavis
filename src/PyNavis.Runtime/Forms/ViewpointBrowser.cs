using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The shared saved-viewpoint browser: search, filter chips, folder rail,
    /// and a VIRTUALIZED list. Only the rows on screen become WPF elements, so
    /// a document with hundreds of thousands of viewpoints costs the same to
    /// show as one with twenty. Selection is by GUID and lives in
    /// ViewpointSelection; what to show lives in ViewpointFilter. This class
    /// is only the chrome that binds those together.
    /// </summary>
    public sealed class ViewpointBrowser
    {
        private readonly Tokens _t;
        private readonly ObservableCollection<ViewpointRow> _shown =
            new ObservableCollection<ViewpointRow>();
        private readonly StackPanel _railList;
        private ColumnDefinition _railColumn;
        private readonly ListBox _list;
        private readonly TextBox _search;
        private readonly Dictionary<string, Button> _chips = new Dictionary<string, Button>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);

        private IList<ViewpointRow> _rows;
        private List<int> _visible = new List<int>();
        private string _query = "", _scope = "", _chip;
        private int _anchor = -1;
        private bool _muted;

        public ViewpointBrowser(IList<ViewpointRow> rows, Tokens t)
        {
            _t = t;
            _rows = rows;
            ViewpointFilter.MarkDuplicates(_rows);
            Selection = new ViewpointSelection(_rows);
            Selection.Changed += () =>
            {
                RefreshRail();
                SelectionChanged?.Invoke();
            };

            _search = BuildSearch();
            _railList = new StackPanel();
            _list = BuildList();

            var body = new Grid();
            _railColumn = new ColumnDefinition { Width = new GridLength(216), MinWidth = 140 };
            body.ColumnDefinitions.Add(_railColumn);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

            var rail = BuildRail();
            body.Children.Add(rail);
            DesignSystem.AddColumnSplitter(body, 1, t);
            // A rail dragged wide must give the width back when the window
            // narrows, or the list runs off the end of the pane and the clip
            // takes its scrollbar with it.
            DesignSystem.KeepPaneInside(body, _railColumn);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.Children.Add(BuildHeaderRow());
            Grid.SetRow(_list, 1);
            right.Children.Add(_list);
            Grid.SetColumn(right, 2);
            body.Children.Add(right);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildCommandBar());
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            View = root;
            Recompute();
        }

        // ---- public surface ----------------------------------------------------

        public FrameworkElement View { get; }

        /// <summary>Width of the folder tree column, for remembering between sessions.
        /// Setting clamps to the column's minimum; non-positive values are ignored.
        /// Reading gives the width that was asked for rather than the arranged one:
        /// a narrow window caps the rail (see KeepPaneInside), and saving the capped
        /// value would quietly forget where the user had put the seam.</summary>
        public double RailWidth
        {
            get => _railColumn.Width.IsAbsolute ? _railColumn.Width.Value : _railColumn.ActualWidth;
            set
            {
                if (value > 0 && !double.IsInfinity(value))
                    _railColumn.Width = new GridLength(Math.Max(value, _railColumn.MinWidth));
            }
        }

        public ViewpointSelection Selection { get; }

        /// <summary>Raised on any selection change, once per gesture.</summary>
        public event Action SelectionChanged;

        /// <summary>Raised when the user presses Delete on the list.</summary>
        public event Action DeleteRequested;

        /// <summary>Raised on Enter or double-click: "go to this viewpoint".</summary>
        public event Action<ViewpointRow> Activated;

        /// <summary>Indexes into the current row set that are listed right now.</summary>
        public IList<int> Visible => _visible;

        public IList<ViewpointRow> Rows => _rows;

        public string Scope => _scope;

        public void SetRows(IList<ViewpointRow> rows)
        {
            _rows = rows;
            ViewpointFilter.MarkDuplicates(_rows);
            // Rebind rather than replace, so subscribers keep their handler.
            Selection.Rebind(_rows);
            _anchor = -1;
            Recompute();
        }

        // ---- test seams --------------------------------------------------------

        public int VisibleCount => _visible.Count;

        /// <summary>Containers the ListBox has actually realized. The point of
        /// the whole control: this stays small however many rows exist.</summary>
        public int RealizedRowCount
        {
            get
            {
                var generator = _list.ItemContainerGenerator;
                var realized = 0;
                for (var i = 0; i < _shown.Count; i++)
                    if (generator.ContainerFromIndex(i) != null) realized++;
                return realized;
            }
        }

        public void SearchForTest(string text) { _search.Text = text; _query = text; Recompute(); }

        public void ScopeForTest(string scope) { _scope = scope ?? ""; Recompute(); }

        public void ChipForTest(string chip) { _chip = chip; Recompute(); }

        /// <summary>Drains the dispatcher so layout and container generation
        /// finish before a test inspects them.</summary>
        public void PumpForTest() => Pump();

        internal static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        // ---- chrome ------------------------------------------------------------

        private FrameworkElement BuildCommandBar()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var searchShell = DesignSystem.InputField(_t, _search);
            searchShell.Width = 280;
            searchShell.Margin = new Thickness(0, 0, 10, 0);
            bar.Children.Add(searchShell);

            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var pair in new[]
                { ("animations", "Animations"), ("duplicates", "Duplicates") })
            {
                var chip = Chip(pair.Item2);
                var id = pair.Item1;
                chip.Click += (s, e) =>
                {
                    _chip = _chip == id ? null : id;
                    PaintChips();
                    Recompute();
                };
                _chips[id] = chip;
                chips.Children.Add(chip);
            }
            bar.Children.Add(chips);

            _showing = DesignSystem.Text("", 12, _t.Muted);
            // Last in the bar, so a narrow window leaves it a few pixels. Wrapped
            // that turns into one character per line and a tower tall enough to
            // push the list off the bottom of the dialog; trimmed it just fades.
            _showing.TextWrapping = TextWrapping.NoWrap;
            _showing.TextTrimming = TextTrimming.CharacterEllipsis;
            _showing.VerticalAlignment = VerticalAlignment.Center;
            _showing.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(_showing, Dock.Right);
            bar.Children.Add(_showing);
            return bar;
        }

        private TextBlock _showing;

        private Button Chip(string label)
        {
            var button = DesignSystem.Secondary(_t, label, null);
            button.Height = 28;
            button.MinWidth = 0;
            button.Padding = new Thickness(10, 0, 10, 0);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        private void PaintChips()
        {
            foreach (var pair in _chips)
            {
                var on = pair.Key == _chip;
                pair.Value.Background = on
                    ? DesignSystem.Brush(DesignSystem.Accent) : DesignSystem.Brush(_t.Paper);
                if (pair.Value.Content is TextBlock text)
                    text.Foreground = on ? Brushes.White : DesignSystem.Brush(_t.Ink);
            }
        }

        private TextBox BuildSearch()
        {
            var box = new TextBox { FontSize = 13 };
            box.TextChanged += (s, e) => { _query = box.Text; Recompute(); };
            return box;
        }

        private FrameworkElement BuildRail()
        {
            var panel = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 8, 4) };
            var title = DesignSystem.Text("FOLDERS", 11, _t.Muted, FontWeights.Medium);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            // Same language as the row chevrons beside them: a chevron points the
            // way the tree is about to move, and doubling it means all the way.
            // These were "−", "+", "«" and "»", which said nothing about direction
            // and left the two "all" buttons looking like text navigation.
            buttons.Children.Add(RailButton(ChevronUp, "Collapse one level", CollapseOneLevel));
            buttons.Children.Add(RailButton(ChevronDown, "Expand one level", ExpandOneLevel));
            buttons.Children.Add(RailButton(DoubleChevronUp, "Collapse all", CollapseAll));
            buttons.Children.Add(RailButton(DoubleChevronDown, "Expand all", ExpandAll));
            head.Children.Add(buttons);
            panel.Children.Add(head);
            panel.Children.Add(_railList);

            var scroll = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 6, 0),
            };
            DesignSystem.SlimScroll(scroll);
            scroll.ContextMenu = null;
            scroll.PreviewMouseRightButtonUp += (s, e) =>
            {
                ShowRailMenu(e.OriginalSource as DependencyObject);
                e.Handled = true;
            };
            return scroll;
        }

        private Button RailButton(Geometry glyph, string tip, Action action)
        {
            var button = DesignSystem.Quiet(_t, "", action);
            button.Content = new Path
            {
                Data = glyph,
                Stroke = DesignSystem.Brush(_t.Ink),
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.MinWidth = 0;
            button.Width = 22;
            button.Height = 20;
            button.Padding = new Thickness(0);
            button.ToolTip = tip;
            return button;
        }

        private FrameworkElement BuildHeaderRow()
        {
            var grid = new Grid { Height = 30 };
            AddColumns(grid);
            var line = new Border
            {
                BorderBrush = DesignSystem.Brush(_t.LineStrong),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            Grid.SetColumnSpan(line, 5);
            grid.Children.Add(line);

            AddHeader(grid, 1, "Name");
            AddHeader(grid, 2, "Folder");
            AddHeader(grid, 3, "Type");
            AddHeader(grid, 4, "Comments");
            return grid;
        }

        private void AddHeader(Grid grid, int column, string text)
        {
            var block = DesignSystem.Text(text, 11.5, _t.Muted);
            block.VerticalAlignment = VerticalAlignment.Center;
            block.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        private static void AddColumns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        }

        private ListBox BuildList()
        {
            var list = new ListBox
            {
                ItemsSource = _shown,
                SelectionMode = SelectionMode.Extended,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(list, true);          // required for virtualization
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Item);

            var panel = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
            list.ItemsPanel = new ItemsPanelTemplate(panel);
            list.ItemTemplate = new DataTemplate { VisualTree = RowFactory() };
            list.ItemContainerStyle = RowContainerStyle();

            list.PreviewMouseLeftButtonDown += OnRowMouseDown;
            list.MouseDoubleClick += (s, e) =>
            {
                if (RowOf(e.OriginalSource as DependencyObject) is ViewpointRow row)
                    Activated?.Invoke(row);
            };
            list.PreviewKeyDown += OnListKey;
            list.PreviewMouseRightButtonUp += (s, e) =>
            {
                ShowListMenu(RowOf(e.OriginalSource as DependencyObject));
                e.Handled = true;
            };
            DesignSystem.SlimScroll(list);
            return list;
        }

        private Style RowContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.HeightProperty, 32.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty,
                DesignSystem.FocusRing(DesignSystem.Accent)));

            var template = new ControlTemplate(typeof(ListBoxItem));
            var root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 1));
            root.SetValue(Border.BorderBrushProperty, DesignSystem.Brush(_t.Line));
            root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            root.Name = "shell";        // must be the factory's Name for TargetName to bind
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(presenter);
            template.VisualTree = root;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                DesignSystem.Brush(_t.Surface), "shell"));
            template.Triggers.Add(hover);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private FrameworkElementFactory RowFactory()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            foreach (var width in new[] { 34.0, -1, 210.0, 96.0, 84.0 })
            {
                var column = new FrameworkElementFactory(typeof(ColumnDefinition));
                column.SetValue(ColumnDefinition.WidthProperty,
                    width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width));
                grid.AppendChild(column);
            }

            var check = new FrameworkElementFactory(typeof(Border));
            check.SetValue(Grid.ColumnProperty, 0);
            check.SetValue(FrameworkElement.WidthProperty, 16.0);
            check.SetValue(FrameworkElement.HeightProperty, 16.0);
            check.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            check.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            check.SetValue(Border.BorderBrushProperty, DesignSystem.Brush(_t.LineStrong));
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(FrameworkElement.NameProperty, "check");
            check.SetBinding(Border.BackgroundProperty, new Binding("Guid")
            {
                Converter = new SelectedFillConverter(Selection, _t),
            });
            grid.AppendChild(check);

            var name = new FrameworkElementFactory(typeof(StackPanel));
            name.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            name.SetValue(Grid.ColumnProperty, 1);
            name.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0));
            name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var icon = new FrameworkElementFactory(typeof(Path));
            icon.SetValue(Shape.StrokeProperty, DesignSystem.Brush(_t.Muted));
            icon.SetValue(Shape.StrokeThicknessProperty, 1.2);
            icon.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            icon.SetValue(FrameworkElement.WidthProperty, 16.0);
            icon.SetValue(FrameworkElement.HeightProperty, 16.0);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0));
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetBinding(Path.DataProperty, new Binding(".") { Converter = new IconConverter() });
            name.AppendChild(icon);

            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetValue(TextBlock.FontSizeProperty, 13.0);
            label.SetValue(TextBlock.ForegroundProperty, DesignSystem.Brush(_t.Ink));
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.AppendChild(label);

            var warn = new FrameworkElementFactory(typeof(Path));
            warn.SetValue(Path.DataProperty, ViewpointIcons.Get("duplicate"));
            warn.SetValue(Shape.StrokeProperty, DesignSystem.Brush(_t.Warning));
            warn.SetValue(Shape.StrokeThicknessProperty, 1.2);
            warn.SetValue(FrameworkElement.WidthProperty, 13.0);
            warn.SetValue(FrameworkElement.HeightProperty, 13.0);
            warn.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 0, 0));
            warn.SetValue(FrameworkElement.ToolTipProperty, "Duplicate name");
            warn.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            warn.SetBinding(UIElement.VisibilityProperty, new Binding("Duplicate")
            {
                Converter = new BoolVisibilityConverter(),
            });
            name.AppendChild(warn);
            grid.AppendChild(name);

            grid.AppendChild(Cell(2, "Folder"));
            grid.AppendChild(Cell(3, "Kind"));
            grid.AppendChild(Cell(4, "Comments", true));
            return grid;
        }

        private FrameworkElementFactory Cell(int column, string path, bool numeric = false)
        {
            var block = new FrameworkElementFactory(typeof(TextBlock));
            block.SetValue(Grid.ColumnProperty, column);
            block.SetValue(TextBlock.FontSizeProperty, 12.0);
            block.SetValue(TextBlock.ForegroundProperty, DesignSystem.Brush(_t.Muted));
            block.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            block.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0));
            block.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (numeric)
                block.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            block.SetBinding(TextBlock.TextProperty, new Binding(path)
            {
                Converter = numeric ? (IValueConverter)new CountConverter() : null,
            });
            return block;
        }

        // ---- interaction -------------------------------------------------------

        private ViewpointRow RowOf(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
                source = VisualTreeHelper.GetParent(source);
            return (source as ListBoxItem)?.DataContext as ViewpointRow;
        }

        private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
        {
            var row = RowOf(e.OriginalSource as DependencyObject);
            if (row == null) return;
            var at = _shown.IndexOf(row);
            if (at < 0) return;

            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (shift && _anchor >= 0)
            {
                if (!ctrl) Selection.Clear();
                Selection.SetRange(_visible, _anchor, at, true);
            }
            else if (ctrl)
            {
                Selection.Set(row.Guid, !Selection.IsSelected(row.Guid));
                _anchor = at;
            }
            else
            {
                Selection.Clear();
                Selection.Set(row.Guid, true);
                _anchor = at;
            }
            RefreshRows();
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Selection.SetAll(_visible, true);
                RefreshRows();
                e.Handled = true;
            }
            else if (e.Key == Key.Space && _list.SelectedItem is ViewpointRow current)
            {
                Selection.Set(current.Guid, !Selection.IsSelected(current.Guid));
                _anchor = _shown.IndexOf(current);
                RefreshRows();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Selection.Clear();
                RefreshRows();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteRequested?.Invoke();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _list.SelectedItem is ViewpointRow go)
            {
                Activated?.Invoke(go);
                e.Handled = true;
            }
        }

        // ---- folder tree commands ---------------------------------------------

        private IEnumerable<ViewpointRow> Folders => _rows.Where(r => r.IsFolder);

        private bool HasChildren(ViewpointRow folder) =>
            _rows.Any(r => r.ParentKey == folder.Key);

        private bool FolderVisible(ViewpointRow folder)
        {
            var parts = folder.Key.Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (_collapsed.Contains(string.Join("/", parts.Take(i)))) return false;
            return true;
        }

        public void ExpandAll() { _collapsed.Clear(); RefreshRail(); }

        public void CollapseAll()
        {
            foreach (var folder in Folders) if (HasChildren(folder)) _collapsed.Add(folder.Key);
            RefreshRail();
        }

        /// <summary>Opens the shallowest folded rank that is showing, so
        /// repeated presses walk the tree a layer at a time.</summary>
        public void ExpandOneLevel()
        {
            var candidates = Folders
                .Where(f => HasChildren(f) && _collapsed.Contains(f.Key) && FolderVisible(f))
                .ToList();
            if (candidates.Count == 0) return;
            var depth = candidates.Min(f => f.Depth);
            foreach (var folder in candidates.Where(f => f.Depth == depth))
                _collapsed.Remove(folder.Key);
            RefreshRail();
        }

        public void CollapseOneLevel()
        {
            var candidates = Folders
                .Where(f => HasChildren(f) && !_collapsed.Contains(f.Key) && FolderVisible(f))
                .ToList();
            if (candidates.Count == 0) return;
            var depth = candidates.Max(f => f.Depth);
            foreach (var folder in candidates.Where(f => f.Depth == depth))
                _collapsed.Add(folder.Key);
            RefreshRail();
        }

        public void ScopeTo(string folderPath)
        {
            _scope = folderPath ?? "";
            Recompute();
        }

        // ---- rendering ---------------------------------------------------------

        private void Recompute()
        {
            _visible = ViewpointFilter.Apply(_rows, _query, _scope, _chip);
            _shown.Clear();
            foreach (var index in _visible) _shown.Add(_rows[index]);
            _anchor = -1;
            if (_showing != null)
                _showing.Text = string.Format("Showing {0:n0} of {1:n0}",
                    _visible.Count, _rows.Count(r => !r.IsFolder));
            RefreshRail();
        }

        /// <summary>Repaints realized rows only; the check fill is bound through
        /// a converter that reads the live selection.</summary>
        private void RefreshRows()
        {
            if (_muted) return;
            var items = _list.Items;
            items.Refresh();
        }

        private void RefreshRail()
        {
            if (_railList == null) return;
            _railList.Children.Clear();
            _railList.Children.Add(RailRow(null, "All viewpoints", 0, false,
                _rows.Count(r => !r.IsFolder), 0));

            foreach (var folder in Folders)
            {
                if (!FolderVisible(folder)) continue;
                var count = _rows.Count(r => !r.IsFolder &&
                    (r.Folder == FolderPathOf(folder) ||
                     (r.Folder ?? "").StartsWith(FolderPathOf(folder) + "/", StringComparison.Ordinal)));
                _railList.Children.Add(RailRow(folder, folder.Name, folder.Depth,
                    HasChildren(folder), count, Selection.SelectedUnder(folder.Key)));
            }
        }

        private static string FolderPathOf(ViewpointRow folder) =>
            string.IsNullOrEmpty(folder.Folder) ? folder.Name : folder.Folder + "/" + folder.Name;

        // Drawn, not typed. These were the characters "›" and "˅", a
        // quotation mark and a modifier letter borrowed for their shape: both sit
        // off their baseline in a 16px column, so one read as a stray comma and the
        // other was nearly invisible. Geometry centres exactly and takes a real
        // stroke weight. Pointing right when closed and down when open is what
        // every file tree does, so it needs no explaining.
        private static readonly Geometry TwistCollapsed = Geometry.Parse("M 0,0 L 4.5,4.5 L 0,9");
        private static readonly Geometry TwistExpanded = Geometry.Parse("M 0,0 L 4.5,4.5 L 9,0");

        // The rail's four buttons, in the same language: the chevron points the way
        // the tree moves, and a second one under it means every level at once.
        private static readonly Geometry ChevronUp = Geometry.Parse("M 0,4.5 L 4.5,0 L 9,4.5");
        private static readonly Geometry ChevronDown = Geometry.Parse("M 0,0 L 4.5,4.5 L 9,0");
        private static readonly Geometry DoubleChevronUp =
            Geometry.Parse("M 0,4 L 4.5,0 L 9,4 M 0,9.5 L 4.5,5.5 L 9,9.5");
        private static readonly Geometry DoubleChevronDown =
            Geometry.Parse("M 0,0 L 4.5,4 L 9,0 M 0,5.5 L 4.5,9.5 L 9,5.5");

        private FrameworkElement RailRow(ViewpointRow folder, string label, int depth,
            bool hasKids, int count, int selected)
        {
            var path = folder == null ? "" : FolderPathOf(folder);
            var row = new DockPanel
            {
                Height = 28,
                Background = Brushes.Transparent,
                Margin = new Thickness(depth * 14, 0, 0, 0),
            };
            if (_scope == path)
                row.Background = DesignSystem.Brush(_t.Selection);

            // The whole 18px column is the hit target, not the stroke: a chevron is
            // a few pixels of line, and asking someone to hit that is why this felt
            // fiddly even once it was visible.
            var twist = new Border
            {
                Width = 18,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            if (hasKids)
            {
                twist.Child = new Path
                {
                    Data = _collapsed.Contains(folder.Key) ? TwistCollapsed : TwistExpanded,
                    Stroke = DesignSystem.Brush(_t.Ink),
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                twist.Cursor = Cursors.Hand;
                twist.MouseLeftButtonUp += (s, e) =>
                {
                    if (!_collapsed.Remove(folder.Key)) _collapsed.Add(folder.Key);
                    RefreshRail();
                    e.Handled = true;
                };
            }
            row.Children.Add(twist);

            var glyph = new Path
            {
                Data = ViewpointIcons.Get(
                    folder != null && !_collapsed.Contains(folder.Key) && hasKids
                        ? "folder-open" : "folder"),
                Stroke = DesignSystem.Brush(_t.Muted),
                StrokeThickness = 1.2,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(glyph);

            var total = DesignSystem.TabularNumber(_t == null ? "" : count.ToString("n0"), 11.5, _t.Muted);
            total.VerticalAlignment = VerticalAlignment.Center;
            total.Margin = new Thickness(6, 0, 4, 0);
            DockPanel.SetDock(total, Dock.Right);
            row.Children.Add(total);

            if (selected > 0)
            {
                var sel = DesignSystem.TabularNumber(selected.ToString("n0") + " ✓", 11,
                    DesignSystem.Accent);
                sel.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(sel, Dock.Right);
                row.Children.Add(sel);
            }

            var text = DesignSystem.Text(label, 12.5, _t.Ink);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);

            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (s, e) => ScopeTo(path);
            row.Tag = path;
            return row;
        }

        // ---- context menus -----------------------------------------------------

        private void ShowRailMenu(DependencyObject source)
        {
            var path = FolderPathAt(source);
            var menu = new ContextMenu();
            AddTreeItems(menu);
            menu.Items.Add(new Separator());
            AddItem(menu, path == null ? "Show all viewpoints" : "Show only this folder",
                () => ScopeTo(path ?? ""));
            AddItem(menu, "Select everything inside", () =>
            {
                var prefix = path + "/";
                foreach (var row in _rows)
                    if (!row.IsFolder && (row.Folder == path
                        || (row.Folder ?? "").StartsWith(prefix, StringComparison.Ordinal)))
                        Selection.Set(row.Guid, true);
                RefreshRows();
            }, path != null);
            menu.IsOpen = true;
        }

        private string FolderPathAt(DependencyObject source)
        {
            while (source != null)
            {
                if (source is DockPanel panel && panel.Tag is string path)
                    return path.Length == 0 ? null : path;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void ShowListMenu(ViewpointRow row)
        {
            if (row != null && !Selection.IsSelected(row.Guid))
            {
                Selection.Clear();
                Selection.Set(row.Guid, true);
                RefreshRows();
            }
            var menu = new ContextMenu();
            AddItem(menu, "Go to this viewpoint", () => Activated?.Invoke(row), row != null);
            menu.Items.Add(new Separator());
            AddItem(menu, "Select all in this folder", () =>
            {
                foreach (var other in _rows)
                    if (!other.IsFolder && other.Folder == row.Folder)
                        Selection.Set(other.Guid, true);
                RefreshRows();
            }, row != null);
            AddItem(menu, "Show this folder in the tree",
                () => ScopeTo(row.Folder), row != null);
            menu.Items.Add(new Separator());
            AddTreeItems(menu);
            menu.IsOpen = true;
        }

        private void AddTreeItems(ContextMenu menu)
        {
            AddItem(menu, "Expand all", ExpandAll);
            AddItem(menu, "Collapse all", CollapseAll);
            AddItem(menu, "Expand one level", ExpandOneLevel);
            AddItem(menu, "Collapse one level", CollapseOneLevel);
        }

        private static void AddItem(ContextMenu menu, string label, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        // ---- converters --------------------------------------------------------

        private sealed class IconConverter : IValueConverter
        {
            public object Convert(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                value is ViewpointRow row ? ViewpointIcons.ForRow(row) : null;
            public object ConvertBack(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                throw new NotSupportedException();
        }

        private sealed class BoolVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                value is bool on && on ? Visibility.Visible : Visibility.Collapsed;
            public object ConvertBack(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                throw new NotSupportedException();
        }

        private sealed class CountConverter : IValueConverter
        {
            public object Convert(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                value is int n && n > 0 ? n.ToString() : "";
            public object ConvertBack(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                throw new NotSupportedException();
        }

        private sealed class SelectedFillConverter : IValueConverter
        {
            private readonly ViewpointSelection _selection;
            private readonly Brush _on;

            public SelectedFillConverter(ViewpointSelection selection, Tokens t)
            {
                _selection = selection;
                _on = DesignSystem.Brush(DesignSystem.Accent);
            }

            public object Convert(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                value is string guid && _selection.IsSelected(guid) ? _on : Brushes.Transparent;
            public object ConvertBack(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                throw new NotSupportedException();
        }
    }
}
