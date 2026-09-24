using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PyNavis.Runtime.Output;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The frame the three viewpoint dialogs share: a ViewpointBrowser on the
    /// left, the tool's own column on the right, a selection strip along the
    /// bottom, and one primary action. Each tool stays its own modal dialog
    /// (its own ribbon button), and this is only what they have in common.
    /// </summary>
    public sealed class ViewpointDialogShell
    {
        public ViewpointDialogShell(string title, string caption,
            System.Collections.Generic.IList<ViewpointRow> rows, Tokens t)
        {
            T = t;
            Browser = new ViewpointBrowser(rows, t);

            Window = new Window
            {
                Title = title,
                Width = 1000,
                Height = 660,
                // 468 of browser + 32 of seam + 240 of panel is 740 of panes,
                // and 32 more of margin. Narrower than that and the columns no
                // longer fit, which a Grid answers by overflowing and letting
                // WPF clip the overflow - not by laying the panes out smaller.
                MinWidth = 800,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Background = DesignSystem.Brush(t.Paper),
                ShowInTaskbar = false,
            };
            FluentChrome.Apply(Window);

            var body = new Grid { Margin = new Thickness(16, 14, 16, 12) };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(DesignSystem.Text(title, 16, t.Ink, FontWeights.SemiBold));
            var sub = DesignSystem.Text(caption, 12, t.Muted);
            sub.Margin = new Thickness(0, 4, 0, 0);
            header.Children.Add(sub);
            body.Children.Add(header);

            var split = new Grid();
            // 468 is the browser's own minimum: 140 of rail, 8 of seam, 320 of
            // list. Less than that and the browser overflows its column.
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 468 });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _panelColumn = new ColumnDefinition { Width = new GridLength(326), MinWidth = 240 };
            split.ColumnDefinitions.Add(_panelColumn);
            split.Children.Add(Browser.View);

            DesignSystem.AddColumnSplitter(split, 1, t, 12);
            DesignSystem.KeepPaneInside(split, _panelColumn);

            Panel = new StackPanel();
            var panelScroll = new ScrollViewer
            {
                Content = Panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            DesignSystem.SlimScroll(panelScroll);
            Grid.SetColumn(panelScroll, 2);
            split.Children.Add(panelScroll);
            Grid.SetRow(split, 1);
            body.Children.Add(split);

            var strip = BuildStrip();
            Grid.SetRow(strip, 2);
            body.Children.Add(strip);

            Window.Content = body;
            Browser.SelectionChanged += () => RefreshStrip();
            RefreshStrip();

            // Restore on Loaded and save on Closed, so a window that is only built
            // (the tests) never touches the user's config.json.
            var shown = false;
            Window.Loaded += (s, e) => { shown = true; RestoreLayout(); };
            Window.Closed += (s, e) => { if (shown) SaveLayout(); };
        }

        /// <summary>config.json section the pane widths live under ("layout.viewpoints").
        /// Null skips both restore and save.</summary>
        public string LayoutKey { get; set; } = "viewpoints";

        /// <summary>Width of the operation pane on the right; see Browser.RailWidth.</summary>
        public double PanelWidth
        {
            get => _panelColumn.Width.IsAbsolute ? _panelColumn.Width.Value : _panelColumn.ActualWidth;
            set
            {
                if (value > 0 && !double.IsInfinity(value))
                    _panelColumn.Width = new GridLength(Math.Max(value, _panelColumn.MinWidth));
            }
        }

        private readonly ColumnDefinition _panelColumn;

        private void RestoreLayout()
        {
            if (LayoutKey == null) return;
            try
            {
                var layout = Config.PyNavisConfig.Load(RuntimeHost.UserConfigPath).Layout;
                double w;
                if (layout.TryGetValue(LayoutKey + "/tree", out w)) Browser.RailWidth = w;
                if (layout.TryGetValue(LayoutKey + "/panel", out w)) PanelWidth = w;
            }
            catch (Exception ex)
            {
                Log.Error("Could not restore dialog layout.", ex);
            }
        }

        private void SaveLayout()
        {
            if (LayoutKey == null) return;
            try
            {
                Config.PyNavisConfig.SaveLayout(RuntimeHost.UserConfigPath, LayoutKey,
                    new Dictionary<string, double>
                    {
                        ["tree"] = Browser.RailWidth,
                        ["panel"] = PanelWidth,
                    });
            }
            catch (Exception ex)
            {
                Log.Error("Could not save dialog layout.", ex);
            }
        }

        public Window Window { get; }
        public ViewpointBrowser Browser { get; }
        public StackPanel Panel { get; }
        public Button Primary { get; private set; }
        public Button Close { get; private set; }
        public TextBlock Note { get; private set; }
        public Tokens T { get; }

        /// <summary>Raised whenever the selection changes, after the strip updates.</summary>
        public event Action Changed;

        private TextBlock _count, _breakdown;

        private FrameworkElement BuildStrip()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };

            _count = DesignSystem.Text("0 selected", 12.5, T.Ink);
            _count.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(_count);

            _breakdown = DesignSystem.Text("", 11.5, T.Muted);
            _breakdown.VerticalAlignment = VerticalAlignment.Center;
            _breakdown.Margin = new Thickness(10, 0, 0, 0);
            bar.Children.Add(_breakdown);

            var picks = new StackPanel
            { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
            picks.Children.Add(DesignSystem.Quiet(T, "All",
                () => { Browser.Selection.SetAll(Browser.Visible, true); }));
            picks.Children.Add(DesignSystem.Quiet(T, "None",
                () => { Browser.Selection.Clear(); }));
            picks.Children.Add(DesignSystem.Quiet(T, "Invert",
                () => { Browser.Selection.Invert(Browser.Visible); }));
            bar.Children.Add(picks);

            var actions = new StackPanel
            { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Note = DesignSystem.Text("", 11.5, T.Muted);
            Note.VerticalAlignment = VerticalAlignment.Center;
            Note.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(Note);
            Close = DesignSystem.Secondary(T, "Cancel", () => Window.Close());
            Close.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(Close);
            Primary = DesignSystem.Primary(T, "Apply", null);
            actions.Children.Add(Primary);
            DockPanel.SetDock(actions, Dock.Right);
            bar.Children.Add(actions);

            return new Border
            {
                BorderBrush = DesignSystem.Brush(T.Line),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0),
                Child = bar,
            };
        }

        private void RefreshStrip()
        {
            var count = Browser.Selection.Count;
            _count.Text = string.Format("{0:n0} selected", count);
            _breakdown.Text = Browser.Selection.Summary();
            Changed?.Invoke();
        }

        /// <summary>Swaps the primary button for a destructive one.</summary>
        public void MakePrimaryDangerous(string label, Action onClick)
        {
            var actions = (StackPanel)Primary.Parent;
            var index = actions.Children.IndexOf(Primary);
            actions.Children.RemoveAt(index);
            Primary = DesignSystem.Danger(T, label, onClick);
            actions.Children.Insert(index, Primary);
        }

        // ---- test seams --------------------------------------------------------

        public string CountTextForTest => _count.Text;
        public string BreakdownTextForTest => _breakdown.Text;
    }
}
