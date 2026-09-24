using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Output;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The keyboard shortcuts editor, design system v2: one solid paper surface,
    /// panel names as hairline-divided section headers, a ChordRecorder per tool,
    /// accent only in the primary action and the recorder focus. A dumb view over
    /// ShortcutsModel; Build() constructs without showing (the unit-test seam),
    /// ShowAndSave() is the script-facing entry.
    /// </summary>
    public static class ShortcutsDialog
    {
        private sealed class RowUi
        {
            public ShortcutsModel.Row Row;
            public ChordRecorder Recorder;
            public TextBlock Badge;
            public Button Reset;
            public TextBlock ConflictCaption;
        }

        private sealed class Parts
        {
            public ShortcutsModel Model;
            public Tokens T;
            public List<RowUi> RowUis = new List<RowUi>();
            public Button Save;
            public bool Saved;
            public TextBox Search;
            public StackPanel ListHost;
        }

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        /// <summary>
        /// Shows the editor for the live runtime state; writes config.json and
        /// returns true when the user saved changes (caller triggers Reload).
        /// </summary>
        public static bool ShowAndSave()
        {
            var config = PyNavisConfig.Load(RuntimeHost.UserConfigPath);
            var model = ShortcutsModel.Build(RuntimeHost.Extensions, config);
            var window = Build(model);
            window.ShowDialog();

            var parts = PartsOf(window);
            if (!parts.Saved) return false;

            try
            {
                PyNavisConfig.SaveShortcutBindings(RuntimeHost.UserConfigPath, model.ToBindings());
                return true;
            }
            catch (Exception ex)
            {
                // Locked-down profile, a second session mid-save, or a config.json the
                // user hand-edited into invalid JSON: say so instead of throwing out of
                // the click, and report "nothing saved" so the caller skips its Reload.
                Log.Error("Could not save shortcut bindings", ex);
                Toast.Show("error", "Shortcuts were not saved", ex.Message);
                return false;
            }
        }

        public static Window Build(ShortcutsModel model)
        {
            var parts = new Parts { Model = model, T = Tokens.For(PyNavisTheme.IsDark) };
            var t = parts.T;

            var window = new Window
            {
                Title = "Keyboard Shortcuts",
                Width = 560,
                Height = 640,
                MinWidth = 480,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Background = DesignSystem.Brush(t.Paper),
                Tag = parts,
                ShowInTaskbar = false,
            };
            FluentChrome.Apply(window);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            body.Children.Add(Header(t));

            var search = SearchRow(parts);
            Grid.SetRow(search, 1);
            body.Children.Add(search);

            parts.ListHost = new StackPanel();
            parts.ListHost.Children.Add(BuildRows(parts));
            var scroll = new ScrollViewer
            {
                Content = parts.ListHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 0, 16, 0),
            };
            DesignSystem.SlimScroll(scroll);
            Grid.SetRow(scroll, 2);
            body.Children.Add(scroll);

            var footer = Footer(window, parts);
            Grid.SetRow(footer, 3);
            body.Children.Add(footer);

            window.Content = body;
            RefreshChrome(parts);
            return window;
        }

        // ---- test seams --------------------------------------------------------

        public static int RecorderCountOf(Window window) => PartsOf(window).RowUis.Count;

        public static int PanelHeaderCountOf(Window window) => PartsOf(window).RowUis
            .Select(r => r.Row.PanelTitle).Distinct().Count();

        public static bool SaveEnabledOf(Window window) => PartsOf(window).Save.IsEnabled;

        /// <summary>Types into the search box, as a user would.</summary>
        public static void SearchForTest(Window window, string query) =>
            PartsOf(window).Search.Text = query;

        public static int ConflictCaptionCountOf(Window window) => PartsOf(window).RowUis
            .Count(r => r.ConflictCaption.Visibility == Visibility.Visible);

        // ---- construction ------------------------------------------------------

        private static FrameworkElement Header(Tokens t)
        {
            var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 10) };
            stack.Children.Add(DesignSystem.Text("Keyboard Shortcuts", 16, t.Ink, FontWeights.SemiBold));
            var caption = DesignSystem.Text(
                "Click a field and press the keys. Backspace clears a shortcut, Esc leaves it as it was.",
                12, t.Muted);
            caption.Margin = new Thickness(0, 4, 0, 0);
            caption.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(caption);
            return stack;
        }

        private static FrameworkElement SearchRow(Parts parts)
        {
            parts.Search = new TextBox { FontSize = 13 };
            parts.Search.TextChanged += (s, e) => RefreshList(parts);
            var field = DesignSystem.InputField(parts.T, parts.Search);
            field.Margin = new Thickness(16, 0, 16, 10);
            return field;
        }

        /// <summary>Rebuilds the list for the current filter. Every extension's
        /// bundles land here, so a search box is the difference between finding
        /// a tool and scrolling for it.</summary>
        private static void RefreshList(Parts parts)
        {
            parts.RowUis.Clear();
            parts.ListHost.Children.Clear();
            parts.ListHost.Children.Add(BuildRows(parts));
            RefreshChrome(parts);
        }

        private static StackPanel BuildRows(Parts parts)
        {
            var t = parts.T;
            var list = new StackPanel();
            var query = (parts.Search?.Text ?? "").Trim();

            var visible = parts.Model.Rows.Where(r =>
                query.Length == 0
                || (r.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.PanelTitle ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.Current ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (visible.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(0, 40, 0, 0) };
                var big = DesignSystem.Text(
                    parts.Model.Rows.Count == 0 ? "No tools to bind yet" : "No tools match",
                    13, t.Ink, FontWeights.SemiBold);
                big.HorizontalAlignment = HorizontalAlignment.Center;
                var sub = DesignSystem.Text(
                    parts.Model.Rows.Count == 0
                        ? "Shortcuts appear here once an extension ships a tool."
                        : "Try a shorter search.",
                    12, t.Muted);
                sub.HorizontalAlignment = HorizontalAlignment.Center;
                sub.Margin = new Thickness(0, 4, 0, 0);
                empty.Children.Add(big);
                empty.Children.Add(sub);
                list.Children.Add(empty);
                return list;
            }

            foreach (var group in visible.GroupBy(r => r.PanelTitle))
            {
                var header = new Border
                {
                    BorderBrush = DesignSystem.Brush(t.Line),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 14, 0, 4),
                    Padding = new Thickness(0, 0, 0, 4),
                    Child = DesignSystem.Text(group.Key, 12, t.Muted, FontWeights.Medium),
                };
                list.Children.Add(header);

                foreach (var row in group)
                    list.Children.Add(BuildRow(parts, row));
            }
            return list;
        }

        private static FrameworkElement BuildRow(Parts parts, ShortcutsModel.Row row)
        {
            var t = parts.T;
            var ui = new RowUi { Row = row };
            parts.RowUis.Add(ui);

            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = DesignSystem.Text(row.Title, 13, t.Ink);
            title.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(title);

            ui.Badge = DesignSystem.Text("", 11.5, t.Muted);   // 11.5 is the scale floor
            ui.Badge.VerticalAlignment = VerticalAlignment.Center;
            ui.Badge.Margin = new Thickness(8, 0, 10, 0);
            Grid.SetColumn(ui.Badge, 1);
            grid.Children.Add(ui.Badge);

            ui.Recorder = new ChordRecorder(AllowBareKeysOf(parts.Model)) { ChordText = row.Current };
            Grid.SetColumn(ui.Recorder, 2);
            grid.Children.Add(ui.Recorder);

            ui.Reset = ResetButton(t, () =>
            {
                parts.Model.ResetToDefault(row.BundleKey);
                ui.Recorder.ChordText = row.Current;
                RefreshChrome(parts);
            });
            Grid.SetColumn(ui.Reset, 3);
            grid.Children.Add(ui.Reset);

            // Error copy reads at the start of the row, at the 12px caption size:
            // 11px right-aligned put the explanation furthest from the name it
            // belongs to.
            ui.ConflictCaption = DesignSystem.Text("", 12, t.Error);
            ui.ConflictCaption.Margin = new Thickness(0, 1, 0, 2);
            Grid.SetRow(ui.ConflictCaption, 1);
            Grid.SetColumnSpan(ui.ConflictCaption, 4);
            grid.Children.Add(ui.ConflictCaption);

            ui.Recorder.ChordChanged += chord =>
            {
                if (chord == null) parts.Model.Disable(row.BundleKey);
                else
                {
                    var error = parts.Model.TrySet(row.BundleKey, chord);
                    if (error != null) ui.Recorder.ChordText = row.Current; // parser refused: revert
                }
                RefreshChrome(parts);
            };
            return grid;
        }

        /// <summary>Drawn circular-arrow reset glyph; visible only on customized rows.</summary>
        private static Button ResetButton(Tokens t, Action onClick)
        {
            var arrow = new Path
            {
                Data = Geometry.Parse("M 9,1 A 5,5 0 1 0 11,5"),
                Stroke = DesignSystem.Brush(t.Muted),
                StrokeThickness = 1.4,
                Width = 12,
                Height = 12,
                Stretch = Stretch.None,
            };
            var button = new Button
            {
                Content = arrow,
                Width = 24,
                Height = 24,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = DesignSystem.ButtonChrome(new CornerRadius(3)),
                FocusVisualStyle = DesignSystem.FocusRing(DesignSystem.Accent),
                ToolTip = "Reset to default",
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += (s, e) => onClick();
            return button;
        }

        private static FrameworkElement Footer(Window window, Parts parts)
        {
            var t = parts.T;
            var bar = new Grid { Margin = new Thickness(16, 12, 16, 16) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var restore = DesignSystem.Quiet(t, "Restore All Defaults", () =>
            {
                parts.Model.ResetAll();
                foreach (var ui in parts.RowUis)
                    ui.Recorder.ChordText = ui.Row.Current;
                RefreshChrome(parts);
            });
            restore.HorizontalAlignment = HorizontalAlignment.Left;
            bar.Children.Add(restore);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = DesignSystem.Secondary(t, "Cancel", () => window.Close());
            cancel.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(cancel);

            parts.Save = DesignSystem.Primary(t, "Save", () =>
            {
                parts.Saved = true;
                window.Close();
            });
            actions.Children.Add(parts.Save);
            Grid.SetColumn(actions, 1);
            bar.Children.Add(actions);

            var frame = new Border
            {
                BorderBrush = DesignSystem.Brush(t.Line),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = bar,
            };
            return frame;
        }

        // ---- state -> chrome ---------------------------------------------------

        private static void RefreshChrome(Parts parts)
        {
            foreach (var ui in parts.RowUis)
            {
                var row = ui.Row;
                ui.Badge.Text = !row.IsCustom ? "" : row.Current == null ? "disabled" : "custom";
                ui.Reset.Visibility = row.IsCustom && row.DefaultChord != null
                    ? Visibility.Visible : Visibility.Hidden;

                var conflict = row.ConflictWith != null;
                ui.ConflictCaption.Text = conflict ? "conflicts with " + row.ConflictWith : "";
                ui.ConflictCaption.Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;
            }
            parts.Save.IsEnabled = parts.Model.IsDirty && !parts.Model.HasConflicts;
        }

        private static bool AllowBareKeysOf(ShortcutsModel model) => model.AllowBareKeys;
    }
}
