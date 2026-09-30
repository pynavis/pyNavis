using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Prebuilt list picker dialogs for scripts (pynavis.forms): a v2 dialog with
    /// a real Windows titlebar, a search box that filters by substring, and a
    /// virtualized list of rows (checkbox rows in multiselect, plain rows with
    /// native selection otherwise). Build constructs without showing - the test
    /// seam, mirroring Dialogs.cs and ClashGrouperDialog.
    /// </summary>
    public static class Pickers
    {
        /// <summary>The tick inside a checked box, drawn in a 9x7 box.</summary>
        private static readonly Geometry CheckMark = Geometry.Parse("M 0,3.6 L 3.3,6.8 L 9,0.6");

        /// <summary>
        /// One pickable row. PROPERTIES, not fields: the row template binds Label
        /// and Checked, and WPF resolves CLR properties only - a field binds to
        /// nothing and renders blank without so much as a warning.
        /// </summary>
        private sealed class Row
        {
            public int Index { get; set; }     // index into the ORIGINAL items list
            public string Label { get; set; }
            public bool Checked { get; set; }
        }

        private sealed class Parts
        {
            public List<Row> All;
            public ObservableCollection<Row> Visible;
            public ListBox List;
            public TextBox Search;
            public bool Multiselect;
            public bool Accepted;
            public TextBlock Count;
            public Button Ok;
            public TextBlock EmptyText;
            public TextBlock SearchHint;
        }

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        private sealed class SwitchParts
        {
            public List<Button> Buttons = new List<Button>();
            public int Result = -1;
            public bool Closing;
        }

        private static SwitchParts SwitchPartsOf(Window window) => (SwitchParts)window.Tag;

        private sealed class NumberParts
        {
            public Tokens T;
            public TextBox Input;
            public Border Shell;
            public TextBlock Warn;
            public Button Ok;
            public double? Min;
            public double? Max;
        }

        private static NumberParts NumberPartsOf(Window window) => (NumberParts)window.Tag;

        // ---- script-facing API -------------------------------------------------

        public static IList<int> SelectFromList(string title, IList<string> items, bool multiselect, string prompt,
            IList<int> preChecked = null)
        {
            var window = BuildSelectFromList(title, items, multiselect, prompt, preChecked);
            FluentChrome.Apply(window);
            window.ShowDialog();
            var parts = PartsOf(window);
            return parts.Accepted ? ResultOf(window) : null;
        }

        /// <summary>One-click choice between a few options: clicking a button IS
        /// the answer, no OK/Cancel footer. Returns the clicked index, or -1 if
        /// the user cancelled (Esc or the titlebar close button).</summary>
        public static int CommandSwitch(string title, string prompt, IList<string> options)
        {
            var window = BuildCommandSwitch(title, prompt, options);
            FluentChrome.Apply(window);
            window.ShowDialog();
            return SwitchResultOf(window);
        }

        /// <summary>Numeric prompt with live range validation. Returns null on
        /// cancel; OK is disabled whenever the text is not a number in range, so
        /// a true result is always in [min, max].</summary>
        public static double? AskNumber(
            string prompt, double? min = null, double? max = null, double? initial = null, string title = "pyNavis")
        {
            var window = BuildAskNumber(prompt, min, max, initial, title);
            FluentChrome.Apply(window);
            if (window.ShowDialog() != true) return null;
            return ParseValidNumber(NumberPartsOf(window), out var value) ? value : (double?)null;
        }

        // ---- construction (test seam) ------------------------------------------

        /// <summary>preChecked: original indices to open with ticked (multiselect) or
        /// picked (single-select), so a picker for a saved choice shows that choice.
        /// Indices that name no row are ignored.</summary>
        public static Window BuildSelectFromList(string title, IList<string> items, bool multiselect, string prompt,
            IList<int> preChecked = null)
        {
            var t = Tokens.Current;
            var accent = DesignSystem.Accent;

            var parts = new Parts
            {
                Multiselect = multiselect,
                All = new List<Row>(),
                Visible = new ObservableCollection<Row>(),
            };
            for (var i = 0; i < items.Count; i++)
                parts.All.Add(new Row
                {
                    Index = i,
                    Label = items[i] ?? "",
                    Checked = multiselect && IsPreChecked(preChecked, i, items.Count),
                });

            var window = new Window
            {
                Title = title,
                Width = 420,
                Height = 480,
                MinWidth = 320,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = DesignSystem.Brush(t.Paper),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Tag = parts,
                ShowInTaskbar = false,
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // prompt
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // search
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // footer

            if (!string.IsNullOrEmpty(prompt))
            {
                var promptText = DesignSystem.Text(prompt, 13, t.Ink);
                promptText.Margin = new Thickness(20, 16, 20, 0);
                Grid.SetRow(promptText, 0);
                root.Children.Add(promptText);
            }

            var searchField = BuildSearchField(parts, t);
            Grid.SetRow(searchField, 1);
            root.Children.Add(searchField);

            var listHost = new Grid();
            Grid.SetRow(listHost, 2);
            parts.List = BuildList(window, parts, t, accent);
            listHost.Children.Add(parts.List);

            parts.EmptyText = DesignSystem.Text("Nothing to pick", 13, t.Muted);
            parts.EmptyText.HorizontalAlignment = HorizontalAlignment.Center;
            parts.EmptyText.VerticalAlignment = VerticalAlignment.Center;
            parts.EmptyText.Visibility = Visibility.Collapsed;
            listHost.Children.Add(parts.EmptyText);
            root.Children.Add(listHost);

            var footer = BuildFooter(window, parts, t);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            window.Content = root;

            Recompute(parts);
            // Single-select: the pre-checked row is the pick, as if it had been clicked.
            if (!multiselect)
            {
                var pick = parts.All.FirstOrDefault(r => IsPreChecked(preChecked, r.Index, items.Count));
                if (pick != null)
                {
                    parts.List.SelectedItem = pick;
                    UpdateFooter(parts);
                }
            }
            return window;
        }

        private static bool IsPreChecked(IList<int> preChecked, int index, int count) =>
            preChecked != null && index >= 0 && index < count && preChecked.Contains(index);

        private static FrameworkElement BuildSearchField(Parts parts, Tokens t)
        {
            parts.Search = new TextBox();
            parts.Search.TextChanged += (s, e) => Recompute(parts);
            var shell = DesignSystem.InputField(t, parts.Search);
            shell.Margin = new Thickness(20, 14, 20, 12);

            parts.SearchHint = DesignSystem.Text("Search", 13, t.Muted);
            parts.SearchHint.VerticalAlignment = VerticalAlignment.Center;
            parts.SearchHint.Margin = new Thickness(29, 14, 0, 12);
            parts.SearchHint.IsHitTestVisible = false;

            var field = new Grid();
            field.Children.Add(shell);
            field.Children.Add(parts.SearchHint);
            return field;
        }

        private static FrameworkElement BuildFooter(Window window, Parts parts, Tokens t)
        {
            parts.Count = DesignSystem.Text("", 12, t.Muted);
            parts.Count.VerticalAlignment = VerticalAlignment.Center;
            parts.Count.Visibility = parts.Multiselect ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(parts.Count, 0);

            parts.Ok = DesignSystem.Primary(t, "OK", () =>
            {
                parts.Accepted = true;
                window.DialogResult = true;
            });
            parts.Ok.IsEnabled = false;
            parts.Ok.IsDefault = true;

            var cancel = DesignSystem.Secondary(t, "Cancel", () => window.DialogResult = false);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            buttons.Children.Add(parts.Ok);
            buttons.Children.Add(cancel);
            Grid.SetColumn(buttons, 1);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(parts.Count);
            row.Children.Add(buttons);

            return new Border
            {
                Background = DesignSystem.Brush(t.Surface),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 12, 20, 12),
                Child = row,
            };
        }

        // ---- command switch (test seam) ----------------------------------------

        /// <summary>The quick-choice idiom: a prompt above a wrapping row
        /// of Secondary buttons. There is no OK - clicking a button IS the answer -
        /// and Esc is the only other way out. Build constructs without showing.
        ///
        /// title is the window's titlebar text and prompt is the question above
        /// the buttons; they are separate arguments because they are separate
        /// things (this used to take one string and use it for both, which threw
        /// the caller's title away).
        ///
        /// An EMPTY prompt asks for the full quick-switch look instead:
        /// no titlebar, no question, just the buttons in a hairline shell, and
        /// clicking anywhere else dismisses the window like Esc does.
        /// </summary>
        public static Window BuildCommandSwitch(string title, string prompt, IList<string> options)
        {
            var t = Tokens.Current;
            var parts = new SwitchParts();
            var chromeless = string.IsNullOrEmpty(prompt);

            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 320,
                MaxWidth = 520,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = DesignSystem.Brush(t.Paper),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Tag = parts,
                ShowInTaskbar = false,
            };

            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            if (!chromeless)
                body.Children.Add(DesignSystem.Text(prompt, 13, t.Ink));

            // Every button carries an 8px gap on its right and bottom so the
            // row spaces itself when it wraps. The last column and the last
            // row carry that gap too, which used to land against the body
            // padding and leave the window visibly heavier at the bottom and
            // the right (28 against 20, 32 against 24). The panel absorbs the
            // trailing gaps with a matching negative margin, so the padding
            // the body declares is the padding the eye sees on all four sides.
            var row = new WrapPanel { Margin = new Thickness(0, chromeless ? 0 : 16, -8, -8) };
            for (var i = 0; i < options.Count; i++)
            {
                var index = i;
                var button = DesignSystem.Secondary(t, options[i] ?? "", () => ChooseSwitch(window, parts, index));
                button.Margin = new Thickness(0, 0, 8, 8);
                parts.Buttons.Add(button);
                row.Children.Add(button);
            }
            body.Children.Add(row);

            if (chromeless)
            {
                window.WindowStyle = WindowStyle.None;
                window.Content = new Border
                {
                    Background = DesignSystem.Brush(t.Paper),
                    BorderBrush = DesignSystem.Brush(t.LineStrong),
                    BorderThickness = new Thickness(1),
                    Child = body,
                };
                // No titlebar means no close button, so losing focus is the
                // third way out beside a click and Esc, like a quick switcher.
                // The window also deactivates WHILE closing (a click or Esc
                // already called Close), and a reentrant Close throws inside
                // the host's message pump - which killed Navisworks outright,
                // so the guard here is load-bearing, not defensive fluff.
                window.Deactivated += (s, e) =>
                {
                    if (parts.Closing) return;
                    parts.Closing = true;
                    try { window.Close(); }
                    catch (InvalidOperationException) { /* already closing */ }
                };
            }
            else
            {
                window.Content = body;
            }

            // No IsCancel button to carry it (there is no footer), so Esc is
            // wired directly. Close(), not DialogResult: the test seam clicks a
            // button on a window that was never shown, and DialogResult can only
            // be set while actually showing as a dialog.
            window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                parts.Closing = true;
                window.Close();
            };

            return window;
        }

        private static void ChooseSwitch(Window window, SwitchParts parts, int index)
        {
            parts.Result = index;
            parts.Closing = true;
            window.Close();
        }

        // ---- ask number (test seam) --------------------------------------------

        /// <summary>Numeric prompt: a DesignSystem.InputField with live
        /// validation - red border and message, OK disabled - reusing
        /// ClashGrouperDialog's tolerance-field approach. Build constructs
        /// without showing.</summary>
        public static Window BuildAskNumber(
            string prompt, double? min = null, double? max = null, double? initial = null, string title = "pyNavis")
        {
            var t = Tokens.Current;
            var parts = new NumberParts { T = t, Min = min, Max = max };

            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 380,
                MaxWidth = 520,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = DesignSystem.Brush(t.Paper),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Tag = parts,
                ShowInTaskbar = false,
            };

            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            body.Children.Add(DesignSystem.Text(prompt ?? "", 13, t.Ink));

            parts.Input = new TextBox
            {
                Text = initial.HasValue ? initial.Value.ToString("0.###", CultureInfo.InvariantCulture) : "",
                Height = 32,
            };
            parts.Shell = DesignSystem.InputField(t, parts.Input);
            parts.Shell.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(parts.Shell);

            parts.Warn = DesignSystem.Text(NumberRangeMessage(min, max), 12, t.Error);
            parts.Warn.Margin = new Thickness(0, 6, 0, 0);
            parts.Warn.Visibility = Visibility.Collapsed;
            body.Children.Add(parts.Warn);

            parts.Ok = DesignSystem.Primary(t, "OK", () => window.DialogResult = true);
            parts.Ok.IsDefault = true;
            var cancel = DesignSystem.Secondary(t, "Cancel", () => window.DialogResult = false);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            buttons.Children.Add(parts.Ok);
            buttons.Children.Add(cancel);
            var footer = new Border
            {
                Background = DesignSystem.Brush(t.Surface),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 12, 24, 12),
                Child = buttons,
            };

            var layout = new StackPanel();
            layout.Children.Add(body);
            layout.Children.Add(footer);
            window.Content = layout;

            parts.Input.TextChanged += (s, e) => RefreshNumber(parts);
            window.ContentRendered += (s, e) => { parts.Input.Focus(); parts.Input.SelectAll(); };

            RefreshNumber(parts);
            return window;
        }

        private static string NumberRangeMessage(double? min, double? max)
        {
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            if (min.HasValue && max.HasValue)
                return "Enter a number between " + F(min.Value) + " and " + F(max.Value) + ".";
            if (min.HasValue)
                return "Enter a number of at least " + F(min.Value) + ".";
            if (max.HasValue)
                return "Enter a number of at most " + F(max.Value) + ".";
            return "Enter a number.";
        }

        private static bool ParseValidNumber(NumberParts parts, out double value)
        {
            if (!double.TryParse(parts.Input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            if (parts.Min.HasValue && value < parts.Min.Value) return false;
            if (parts.Max.HasValue && value > parts.Max.Value) return false;
            return true;
        }

        private static void RefreshNumber(NumberParts parts)
        {
            var valid = ParseValidNumber(parts, out _);
            parts.Warn.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            parts.Shell.BorderBrush = valid ? DesignSystem.Brush(parts.T.LineStrong) : DesignSystem.Brush(parts.T.Error);
            parts.Ok.IsEnabled = valid;
        }

        // ---- list construction (mirrors ViewpointBrowser's virtualization) ----

        private static ListBox BuildList(Window window, Parts parts, Tokens t, Color accent)
        {
            var list = new ListBox
            {
                ItemsSource = parts.Visible,
                SelectionMode = SelectionMode.Single,
                BorderThickness = new Thickness(1),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                Background = DesignSystem.Brush(t.Paper),
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
            list.ItemTemplate = new DataTemplate { VisualTree = RowFactory(t, accent, parts.Multiselect) };
            list.ItemContainerStyle = RowContainerStyle(t, accent, parts.Multiselect);

            if (parts.Multiselect)
            {
                list.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    var row = RowOf(e.OriginalSource as DependencyObject);
                    if (row == null) return;
                    row.Checked = !row.Checked;
                    RefreshRows(parts);
                    UpdateFooter(parts);
                    e.Handled = true;
                };
            }
            else
            {
                list.SelectionChanged += (s, e) => UpdateFooter(parts);
                list.MouseDoubleClick += (s, e) =>
                {
                    if (list.SelectedItem == null) return;
                    parts.Accepted = true;
                    window.DialogResult = true;
                };
            }
            DesignSystem.SlimScroll(list);
            return list;
        }

        private static Row RowOf(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            return (source as ListBoxItem)?.DataContext as Row;
        }

        private static FrameworkElementFactory RowFactory(Tokens t, Color accent, bool multiselect)
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            if (multiselect)
            {
                var railCol = new FrameworkElementFactory(typeof(ColumnDefinition));
                railCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(3));
                grid.AppendChild(railCol);
                var checkCol = new FrameworkElementFactory(typeof(ColumnDefinition));
                checkCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(38));
                grid.AppendChild(checkCol);
            }
            var labelCol = new FrameworkElementFactory(typeof(ColumnDefinition));
            labelCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(labelCol);
            var labelColumn = multiselect ? 2 : 0;

            if (multiselect)
            {
                var rail = new FrameworkElementFactory(typeof(Border));
                rail.SetValue(Grid.ColumnProperty, 0);
                rail.SetBinding(Border.BackgroundProperty,
                    new Binding("Checked") { Converter = new CheckedFillConverter(accent) });
                grid.AppendChild(rail);

                var check = new FrameworkElementFactory(typeof(Border));
                check.SetValue(Grid.ColumnProperty, 1);
                check.SetValue(FrameworkElement.WidthProperty, 16.0);
                check.SetValue(FrameworkElement.HeightProperty, 16.0);
                check.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                check.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                check.SetValue(Border.BorderBrushProperty, DesignSystem.Brush(t.LineStrong));
                check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                check.SetBinding(Border.BackgroundProperty,
                    new Binding("Checked") { Converter = new CheckedFillConverter(accent) });

                // Drawn, not the MDL2 check glyph: a font glyph brings its baseline
                // and side bearings into a 16px box and sits low and off-centre
                // however it is aligned (the house rule - see the grouper chevrons).
                var glyph = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
                glyph.SetValue(System.Windows.Shapes.Path.DataProperty, CheckMark);
                glyph.SetValue(System.Windows.Shapes.Shape.StrokeProperty,
                    DesignSystem.Brush(DesignSystem.OnAccent(accent)));
                glyph.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.6);
                glyph.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
                glyph.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
                glyph.SetValue(System.Windows.Shapes.Shape.StrokeLineJoinProperty, PenLineJoin.Round);
                glyph.SetValue(FrameworkElement.WidthProperty, 9.0);
                glyph.SetValue(FrameworkElement.HeightProperty, 7.0);
                glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                glyph.SetBinding(UIElement.VisibilityProperty,
                    new Binding("Checked") { Converter = new BoolVisibilityConverter() });
                check.AppendChild(glyph);
                grid.AppendChild(check);
            }

            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetValue(Grid.ColumnProperty, labelColumn);
            label.SetValue(TextBlock.FontSizeProperty, 13.0);
            label.SetValue(TextBlock.ForegroundProperty, DesignSystem.Brush(t.Ink));
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty,
                new Thickness(multiselect ? 0 : 12, 0, 12, 0));
            label.SetBinding(TextBlock.TextProperty, new Binding("Label"));
            grid.AppendChild(label);

            return grid;
        }

        /// <summary>32px rows, quiet hover, and either a bound accent rail
        /// (multiselect, driven by Row.Checked in the data template) or a
        /// container-owned rail that lights on native ListBoxItem selection
        /// (single-select).</summary>
        private static Style RowContainerStyle(Tokens t, Color accent, bool multiselect)
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.HeightProperty, 32.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, DesignSystem.FocusRing(accent)));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));

            var template = new ControlTemplate(typeof(ListBoxItem));
            FrameworkElementFactory root;
            if (multiselect)
            {
                var shell = new FrameworkElementFactory(typeof(Border));
                shell.Name = "shell";
                shell.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                shell.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
                shell.SetValue(Border.BorderBrushProperty, DesignSystem.Brush(t.Line));
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                shell.AppendChild(presenter);
                root = shell;
            }
            else
            {
                var dock = new FrameworkElementFactory(typeof(DockPanel));
                var rail = new FrameworkElementFactory(typeof(Border));
                rail.Name = "rail";
                rail.SetValue(FrameworkElement.WidthProperty, 3.0);
                rail.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                rail.SetValue(DockPanel.DockProperty, Dock.Left);
                dock.AppendChild(rail);

                var shell = new FrameworkElementFactory(typeof(Border));
                shell.Name = "shell";
                shell.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                shell.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
                shell.SetValue(Border.BorderBrushProperty, DesignSystem.Brush(t.Line));
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                shell.AppendChild(presenter);
                dock.AppendChild(shell);
                root = dock;
            }
            template.VisualTree = root;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, DesignSystem.Brush(t.Surface), "shell"));
            template.Triggers.Add(hover);

            if (!multiselect)
            {
                var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
                selected.Setters.Add(new Setter(Border.BackgroundProperty, DesignSystem.Brush(accent), "rail"));
                selected.Setters.Add(new Setter(Border.BackgroundProperty, DesignSystem.Brush(t.Selection), "shell"));
                template.Triggers.Add(selected);
            }

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        // ---- filtering / footer state -------------------------------------------

        private static void Recompute(Parts parts)
        {
            // ObservableCollection.Clear() raises a Reset notification, and
            // WPF's Selector unconditionally drops SelectedItem on Reset - so a
            // search edit after picking a row would otherwise silently lose the
            // pick even though the row is still visible. Multiselect does not
            // need this: Checked lives on the Row model, not on the ListBox's
            // own selection.
            var previouslySelected = parts.Multiselect ? null : parts.List.SelectedItem as Row;

            var query = (parts.Search.Text ?? "").Trim();
            parts.Visible.Clear();
            foreach (var row in parts.All)
                if (query.Length == 0 || row.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    parts.Visible.Add(row);

            if (previouslySelected != null && parts.Visible.Contains(previouslySelected))
                parts.List.SelectedItem = previouslySelected;

            parts.SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var hasAny = parts.All.Count > 0;
            var hasVisible = parts.Visible.Count > 0;
            parts.List.Visibility = hasVisible ? Visibility.Visible : Visibility.Collapsed;
            parts.EmptyText.Visibility = hasVisible ? Visibility.Collapsed : Visibility.Visible;
            parts.EmptyText.Text = hasAny ? "No matches" : "Nothing to pick";

            UpdateFooter(parts);
        }

        private static void UpdateFooter(Parts parts)
        {
            if (parts.Multiselect)
            {
                var checkedCount = parts.All.Count(r => r.Checked);
                parts.Count.Text = checkedCount == 1 ? "1 selected" : checkedCount + " selected";
                parts.Ok.IsEnabled = checkedCount > 0;
            }
            else
            {
                parts.Ok.IsEnabled = parts.List.SelectedItem != null;
            }
        }

        private static void RefreshRows(Parts parts) => parts.List.Items.Refresh();

        // ---- test seams ----------------------------------------------------------

        /// <summary>Checked/selected original indices, regardless of Accepted -
        /// tests never accept, they inspect state directly. Single-select with
        /// no pick returns null (nothing to report), not an empty list.</summary>
        public static IList<int> ResultOf(Window window)
        {
            var parts = PartsOf(window);
            if (parts.Multiselect)
                return parts.All.Where(r => r.Checked).Select(r => r.Index).ToList();
            var selected = parts.List.SelectedItem as Row;
            return selected != null ? new List<int> { selected.Index } : null;
        }

        public static void SetSearchForTest(Window window, string text)
        {
            PartsOf(window).Search.Text = text ?? "";
        }

        public static int VisibleCountOf(Window window) => PartsOf(window).Visible.Count;

        /// <summary>A visible row as the ListBox hands it to the template, for
        /// tests that check the row bindings resolve against it.</summary>
        public static object RowForTest(Window window, int visibleRow) =>
            PartsOf(window).Visible[visibleRow];

        public static void SetCheckedForTest(Window window, int visibleRow, bool value)
        {
            var parts = PartsOf(window);
            var row = parts.Visible[visibleRow];
            row.Checked = value;
            if (!parts.Multiselect)
                parts.List.SelectedItem = value ? row : null;
            RefreshRows(parts);
            UpdateFooter(parts);
        }

        /// <summary>Single-select test seam: picks a visible row the way a
        /// click would, without going through mouse input.</summary>
        public static void SetSelectedForTest(Window window, int visibleRow)
        {
            var parts = PartsOf(window);
            parts.List.SelectedItem = parts.Visible[visibleRow];
            UpdateFooter(parts);
        }

        /// <summary>Raises the Nth choice button's real Click event - the same
        /// path a mouse click takes - and reports what it left behind.</summary>
        public static void ClickForTest(Window window, int index) =>
            SwitchPartsOf(window).Buttons[index].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        public static int SwitchResultOf(Window window) => SwitchPartsOf(window).Result;

        public static void SetNumberTextForTest(Window window, string text) =>
            NumberPartsOf(window).Input.Text = text ?? "";

        public static bool OkEnabledOf(Window window) => NumberPartsOf(window).Ok.IsEnabled;

        /// <summary>The number currently in the field, valid or not - tests read
        /// this only after confirming OkEnabledOf, mirroring how AskNumber only
        /// ever returns a value OK could actually commit.</summary>
        public static double NumberResultOf(Window window)
        {
            ParseValidNumber(NumberPartsOf(window), out var value);
            return value;
        }

        // ---- converters -----------------------------------------------------------

        private sealed class CheckedFillConverter : IValueConverter
        {
            private readonly Brush _on;
            public CheckedFillConverter(Color accent) { _on = DesignSystem.Brush(accent); }
            public object Convert(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                value is bool b && b ? _on : Brushes.Transparent;
            public object ConvertBack(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                throw new NotSupportedException();
        }

        private sealed class BoolVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
            public object ConvertBack(object value, Type type, object p, System.Globalization.CultureInfo c) =>
                throw new NotSupportedException();
        }
    }
}
