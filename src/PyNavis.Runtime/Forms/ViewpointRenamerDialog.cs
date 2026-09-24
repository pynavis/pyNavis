using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PyNavis.Runtime.Output;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Batch renamer: the shared browser on the left, the operation and a live
    /// old-to-new diff on the right. A dumb view over pynavis.viewpoints, which
    /// owns every naming rule; the preview delegate runs that engine.
    /// </summary>
    public static class ViewpointRenamerDialog
    {
        // ---- interop POCOs (constructed and read from IronPython) --------------

        public sealed class RenameOp
        {
            public string Type { get; set; } = "replace";   // replace|affix|number|case
            public string Find { get; set; } = "";
            public string Replace { get; set; } = "";
            public bool Regex { get; set; }
            public string Prefix { get; set; } = "";
            public string Suffix { get; set; } = "";
            public string Pattern { get; set; } = "View {n}";
            public int Start { get; set; } = 1;
            public int Pad { get; set; } = 2;
            public string CaseMode { get; set; } = "title";
        }

        public sealed class RenameItem
        {
            public string Guid { get; set; }
            public string OldName { get; set; }
            public string NewName { get; set; }
            public bool Collision { get; set; }
        }

        public sealed class RenamePreview
        {
            public List<RenameItem> Items { get; } = new List<RenameItem>();
            public List<string> Problems { get; } = new List<string>();
        }

        public sealed class RenameRequest
        {
            public List<string> CheckedGuids { get; } = new List<string>();
            public RenameOp Op { get; set; } = new RenameOp();
        }

        private sealed class Parts
        {
            public ViewpointDialogShell Shell;
            public Func<RenameRequest, RenamePreview> Preview;
            public string OpType = "replace";
            public Dictionary<string, Button> Segments = new Dictionary<string, Button>();
            public Dictionary<string, FrameworkElement> Panels = new Dictionary<string, FrameworkElement>();
            public TextBox Find, With, Prefix, Suffix, Pattern, Start, Pad;
            public CheckBox Regex;
            public ComboBox CaseMode;
            public StackPanel DiffRows;
            public TextBlock Message;
            public bool Confirmed;
            public bool Quiet;
        }

        private static readonly Dictionary<Window, Parts> Live = new Dictionary<Window, Parts>();

        private static Parts PartsOf(Window window) => Live[window];

        // ---- script-facing API -------------------------------------------------

        public static RenameRequest Show(
            IList<ViewpointRow> rows, Func<RenameRequest, RenamePreview> preview)
        {
            var window = Build(rows, preview);
            window.ShowDialog();
            var parts = PartsOf(window);
            Live.Remove(window);
            return parts.Confirmed ? RequestOf(parts) : null;
        }

        public static Window Build(
            IList<ViewpointRow> rows, Func<RenameRequest, RenamePreview> preview)
        {
            var t = Tokens.For(PyNavisTheme.IsDark);
            var shell = new ViewpointDialogShell("Viewpoint Renamer",
                "Search and select what to rename, then set up the operation.", rows, t);
            var parts = new Parts { Shell = shell, Preview = preview };
            Live[shell.Window] = parts;

            BuildPanel(shell, parts);
            shell.Primary.Content = "Rename";
            shell.Primary.Click += (s, e) =>
            {
                parts.Confirmed = true;
                shell.Window.Close();
            };
            shell.Changed += () => Refresh(parts);
            Refresh(parts);
            return shell.Window;
        }

        // ---- test seams --------------------------------------------------------

        public static int PreviewRowCountOf(Window window) =>
            PartsOf(window).DiffRows.Children.Count;

        public static bool ApplyEnabledOf(Window window) =>
            PartsOf(window).Shell.Primary.IsEnabled;

        public static string MessageOf(Window window) => PartsOf(window).Message.Text;

        public static RenameRequest RequestOf(Window window) => RequestOf(PartsOf(window));

        public static void SetOpTypeForTest(Window window, string type)
        {
            var parts = PartsOf(window);
            parts.OpType = type;
            PaintSegments(parts);
            Refresh(parts);
        }

        public static void SetFindForTest(Window window, string find, string with)
        {
            var parts = PartsOf(window);
            parts.Quiet = true;
            parts.Find.Text = find;
            parts.Quiet = false;
            parts.With.Text = with;
        }

        public static void SelectAllForTest(Window window)
        {
            var browser = PartsOf(window).Shell.Browser;
            browser.Selection.SetAll(browser.Visible, true);
        }

        // ---- panel -------------------------------------------------------------

        private static void BuildPanel(ViewpointDialogShell shell, Parts parts)
        {
            var t = shell.T;
            var panel = shell.Panel;

            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            var labels = new[]
            {
                ("replace", "Replace"), ("affix", "Add"), ("number", "Number"), ("case", "Case"),
            };
            for (var i = 0; i < labels.Length; i++)
            {
                var (type, label) = labels[i];
                var radius = i == 0 ? new CornerRadius(3, 0, 0, 3)
                    : i == labels.Length - 1 ? new CornerRadius(0, 3, 3, 0)
                    : new CornerRadius(0);
                var segment = new Button
                {
                    Content = Centered(DesignSystem.Text(label, 12.5, t.Ink)),
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Template = DesignSystem.ButtonChrome(radius),
                    FocusVisualStyle = DesignSystem.FocusRing(DesignSystem.Accent),
                };
                segment.Click += (s, e) =>
                {
                    parts.OpType = type;
                    PaintSegments(parts);
                    Refresh(parts);
                };
                parts.Segments[type] = segment;
                segRow.Children.Add(segment);
            }
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = segRow,
            });

            parts.Find = Input(parts);
            parts.With = Input(parts);
            parts.Regex = new CheckBox
            {
                Content = "Regular expression",
                Foreground = DesignSystem.Brush(t.Ink),
                Margin = new Thickness(0, 8, 0, 0),
            };
            parts.Regex.Checked += (s, e) => Refresh(parts);
            parts.Regex.Unchecked += (s, e) => Refresh(parts);
            var replacePanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            replacePanel.Children.Add(Field(t, "Find", parts.Find));
            replacePanel.Children.Add(Field(t, "Replace with", parts.With));
            replacePanel.Children.Add(parts.Regex);
            parts.Panels["replace"] = replacePanel;

            parts.Prefix = Input(parts);
            parts.Suffix = Input(parts);
            var affixPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            affixPanel.Children.Add(Field(t, "Prefix", parts.Prefix));
            affixPanel.Children.Add(Field(t, "Suffix", parts.Suffix));
            parts.Panels["affix"] = affixPanel;

            parts.Pattern = Input(parts);
            parts.Pattern.Text = "View {n}";
            parts.Start = Input(parts);
            parts.Start.Text = "1";
            parts.Pad = Input(parts);
            parts.Pad.Text = "2";
            var numberPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            numberPanel.Children.Add(Field(t, "Pattern", parts.Pattern));
            var pair = new Grid();
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            var startField = Field(t, "Start", parts.Start);
            var padField = Field(t, "Digits", parts.Pad);
            padField.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(padField, 1);
            pair.Children.Add(startField);
            pair.Children.Add(padField);
            numberPanel.Children.Add(pair);
            var hint = DesignSystem.Text("{n} becomes the number, in list order.", 11, t.Muted);
            hint.Margin = new Thickness(0, 4, 0, 0);
            numberPanel.Children.Add(hint);
            parts.Panels["number"] = numberPanel;

            parts.CaseMode = new ComboBox
            {
                ItemsSource = new[] { "Title Case", "UPPERCASE", "lowercase" },
                SelectedIndex = 0,
                Margin = new Thickness(0, 10, 0, 0),
            };
            parts.CaseMode.SelectionChanged += (s, e) => Refresh(parts);
            var casePanel = new StackPanel();
            casePanel.Children.Add(parts.CaseMode);
            parts.Panels["case"] = casePanel;

            foreach (var entry in parts.Panels.Values) panel.Children.Add(entry);

            parts.Message = DesignSystem.Text("", 12, t.Muted);
            parts.Message.Margin = new Thickness(0, 12, 0, 6);
            parts.Message.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(parts.Message);

            parts.DiffRows = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = parts.DiffRows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 260,
            };
            DesignSystem.SlimScroll(scroll);
            panel.Children.Add(DesignSystem.GroupFrame(t, "Preview", scroll));

            PaintSegments(parts);
        }

        private static FrameworkElement Centered(TextBlock text)
        {
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            return text;
        }

        private static TextBox Input(Parts parts)
        {
            var box = new TextBox { FontSize = 13 };
            box.TextChanged += (s, e) => { if (!parts.Quiet) Refresh(parts); };
            return box;
        }

        private static FrameworkElement Field(Tokens t, string label, TextBox box)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caption = DesignSystem.Text(label, 11.5, t.Muted);
            caption.Margin = new Thickness(0, 0, 0, 3);
            stack.Children.Add(caption);
            stack.Children.Add(DesignSystem.InputField(t, box));
            return stack;
        }

        private static void PaintSegments(Parts parts)
        {
            foreach (var pair in parts.Segments)
            {
                var on = pair.Key == parts.OpType;
                pair.Value.Background = on
                    ? DesignSystem.Brush(DesignSystem.Accent) : Brushes.Transparent;
                ((TextBlock)pair.Value.Content).Foreground =
                    on ? Brushes.White : DesignSystem.Brush(parts.Shell.T.Ink);
            }
            foreach (var pair in parts.Panels)
                pair.Value.Visibility = pair.Key == parts.OpType
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private static RenameRequest RequestOf(Parts parts)
        {
            var request = new RenameRequest
            {
                Op = new RenameOp
                {
                    Type = parts.OpType,
                    Find = parts.Find?.Text ?? "",
                    Replace = parts.With?.Text ?? "",
                    Regex = parts.Regex?.IsChecked == true,
                    Prefix = parts.Prefix?.Text ?? "",
                    Suffix = parts.Suffix?.Text ?? "",
                    Pattern = parts.Pattern?.Text ?? "{n}",
                    Start = ParseInt(parts.Start?.Text, 1),
                    Pad = ParseInt(parts.Pad?.Text, 0),
                    CaseMode = parts.CaseMode == null ? "title"
                        : parts.CaseMode.SelectedIndex == 1 ? "upper"
                        : parts.CaseMode.SelectedIndex == 2 ? "lower" : "title",
                },
            };
            foreach (var row in parts.Shell.Browser.Selection.SelectedLeaves())
                request.CheckedGuids.Add(row.Guid);
            return request;
        }

        private static int ParseInt(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : fallback;

        private static void Refresh(Parts parts)
        {
            if (parts.DiffRows == null) return;
            var t = parts.Shell.T;
            parts.DiffRows.Children.Clear();

            RenamePreview preview;
            try
            {
                preview = parts.Preview(RequestOf(parts)) ?? new RenamePreview();
            }
            catch (Exception ex)
            {
                Log.Error("Rename preview failed", ex);
                parts.Message.Text = "Preview did not load. Check the log for details.";
                parts.Message.Foreground = DesignSystem.Brush(t.Error);
                parts.Shell.Primary.IsEnabled = false;
                return;
            }

            var collisions = preview.Items.Count(i => i.Collision);
            foreach (var item in preview.Items.Take(200))
                parts.DiffRows.Children.Add(DiffRow(t, item));
            if (preview.Items.Count > 200)
            {
                var more = DesignSystem.Text(
                    string.Format("{0:n0} more not shown here.", preview.Items.Count - 200), 11.5, t.Muted);
                more.Margin = new Thickness(0, 4, 0, 0);
                parts.DiffRows.Children.Add(more);
            }

            if (preview.Problems.Count > 0)
            {
                parts.Message.Text = preview.Problems[0];
                parts.Message.Foreground = DesignSystem.Brush(t.Error);
            }
            else if (collisions > 0)
            {
                parts.Message.Text = string.Format(
                    "{0:n0} name(s) would collide. Adjust the operation before applying.", collisions);
                parts.Message.Foreground = DesignSystem.Brush(t.Error);
            }
            else if (preview.Items.Count == 0)
            {
                parts.Message.Text = "Nothing changes yet. Select viewpoints and set up an operation.";
                parts.Message.Foreground = DesignSystem.Brush(t.Muted);
            }
            else
            {
                parts.Message.Text = string.Format("{0:n0} item(s) will be renamed.", preview.Items.Count);
                parts.Message.Foreground = DesignSystem.Brush(t.Muted);
            }

            parts.Shell.Note.Text = preview.Items.Count > 0 && collisions == 0
                ? string.Format("{0:n0} to rename", preview.Items.Count) : "";
            parts.Shell.Primary.IsEnabled =
                preview.Items.Count > 0 && collisions == 0 && preview.Problems.Count == 0;
        }

        private static FrameworkElement DiffRow(Tokens t, RenameItem item)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var old = DesignSystem.Text(item.OldName, 11.5, t.Muted);
            old.TextTrimming = TextTrimming.CharacterEllipsis;
            old.TextDecorations = TextDecorations.Strikethrough;
            stack.Children.Add(old);
            var next = DesignSystem.Text(item.NewName, 12,
                item.Collision ? t.Error : t.Ink);
            next.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(next);
            if (item.Collision)
                stack.Children.Add(DesignSystem.Text("collides with a sibling", 11, t.Error));
            return stack;
        }
    }
}
