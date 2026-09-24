using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PyNavis.Runtime.Forms;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// Script output window, built on the DesignSystem v2 toolkit:
    /// a solid surface, the real Windows titlebar, a quiet toolbar (copy / clear /
    /// save / find) and the monospace text inside a bordered frame with a thin
    /// scrollbar. Created lazily by OutputWindowWriter on the first byte a script
    /// prints, so silent scripts never pop a window. Theme-aware.
    /// </summary>
    public class OutputWindow : Window, IOutputWindow
    {
        private readonly DesignSystem.Tokens _t;
        private readonly Brush _errorBrush;
        private readonly Paragraph _paragraph;
        private readonly RichTextBox _box;
        private readonly TextBox _findBox;
        private int _lastFindOffset = -1;
        private TextBlock _findStatus;

        public OutputWindow(string title)
        {
            Title = title;
            Width = 760;
            Height = 480;
            MinWidth = 460;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _t = DesignSystem.Tokens.Current;
            _errorBrush = DesignSystem.Brush(_t.Error);
            Background = DesignSystem.Brush(_t.Surface);
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FluentChrome.Apply(this);

            _paragraph = new Paragraph { Margin = new Thickness(0) };
            _box = new RichTextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                IsInactiveSelectionHighlightEnabled = true,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Document = new FlowDocument(_paragraph) { PageWidth = 2000 },
                Padding = new Thickness(12, 10, 12, 10),
                Background = DesignSystem.Brush(_t.Paper),
                Foreground = DesignSystem.Brush(_t.Ink),
            };
            _box.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),
                DesignSystem.SlimScrollStyle());

            // Quiet toolbar: borderless actions left, a bordered find field right.
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(DesignSystem.Quiet(_t, "Copy", CopyToClipboard));
            actions.Children.Add(DesignSystem.Quiet(_t, "Clear", () =>
            {
                // Clearing throws away everything the run produced and there is
                // no undo, so ask once when there is something to lose.
                if (_paragraph.Inlines.Count > 0
                    && !Forms.Dialogs.Confirm("Clear the output?", "pyNavis")) return;
                _paragraph.Inlines.Clear();
                _lastFindOffset = -1;
                RefreshFindStatus();
            }));
            actions.Children.Add(DesignSystem.Quiet(_t, "Save", SaveToFile));

            _findBox = new TextBox { Width = 180, Height = 28, ToolTip = "Find  (Enter, F3)" };
            _findBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) FindNext(); };
            _findBox.TextChanged += (s, e) => { _lastFindOffset = -1; RefreshFindStatus(); };
            var findLabel = DesignSystem.Text("Find", 13, _t.Muted);
            findLabel.VerticalAlignment = VerticalAlignment.Center;
            findLabel.Margin = new Thickness(0, 0, 8, 0);
            _findStatus = DesignSystem.Text("", 11.5, _t.Muted);
            _findStatus.VerticalAlignment = VerticalAlignment.Center;
            _findStatus.Margin = new Thickness(8, 0, 0, 0);
            _findStatus.MinWidth = 66;
            var find = new StackPanel { Orientation = Orientation.Horizontal };
            find.Children.Add(findLabel);
            find.Children.Add(DesignSystem.InputField(_t, _findBox));
            find.Children.Add(_findStatus);

            var header = new DockPanel { Margin = new Thickness(16, 10, 16, 8), LastChildFill = false };
            DockPanel.SetDock(actions, Dock.Left);
            DockPanel.SetDock(find, Dock.Right);
            header.Children.Add(actions);
            header.Children.Add(find);

            var frame = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = DesignSystem.Brush(_t.LineStrong),
                BorderThickness = new Thickness(1),
                Background = DesignSystem.Brush(_t.Paper),
                Margin = new Thickness(16, 0, 16, 16),
                Child = _box,
            };

            var layout = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);
            layout.Children.Add(frame);
            Content = layout;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _findBox.Focus();
                    _findBox.SelectAll();
                }
                else if (e.Key == Key.F3) FindNext();
            };
        }

        // ---- IOutputWindow: rich calls degrade to readable plain text ----------

        public void AppendText(string text, bool isError) => Append(text, isError);

        public void AppendHtml(string html) => Append(PlainTextFor(html), false);

        /// <summary>
        /// What an HTML fragment looks like in the plain-text fallback window.
        /// Markup normally shows as its own source, which is readable enough for
        /// a table or a heading - but an image is a megabyte or more of base64
        /// and a chart is a wall of SVG path data, and dumping either one buries
        /// everything the script actually printed. Those two collapse to a
        /// one-line placeholder instead. Pure, so it is unit-tested without a
        /// window.
        /// </summary>
        public static string PlainTextFor(string html)
        {
            if (string.IsNullOrEmpty(html)) return "\n";
            var trimmed = html.TrimStart();
            if (trimmed.StartsWith("<figure", System.StringComparison.OrdinalIgnoreCase))
                return "[image]\n";
            if (trimmed.StartsWith("<svg", System.StringComparison.OrdinalIgnoreCase))
                return "[chart]\n";
            return html + "\n";
        }

        public void ShowProgress(double fraction, string label) =>
            Append($"[{(int)(System.Math.Max(0, System.Math.Min(1, fraction)) * 100),3}%] {label}\n", false);

        public string RegisterElementLink(object item, string label) => label;

        public void SetTitle(string title)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetTitle(title));
                return;
            }
            Title = title;
        }

        public void Append(string text, bool isError)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Append(text, isError));
                return;
            }

            var run = new Run(text);
            if (isError) run.Foreground = _errorBrush;
            _paragraph.Inlines.Add(run);
            _box.ScrollToEnd();
        }

        /// <summary>All output text (concatenated runs - offsets align with FindNext's).</summary>
        private string FullText() =>
            string.Concat(_paragraph.Inlines.OfType<Run>().Select(r => r.Text));

        private void CopyToClipboard()
        {
            var selection = _box.Selection;
            var text = selection != null && !selection.IsEmpty ? selection.Text : FullText();
            if (text.Length > 0) Clipboard.SetText(text);
        }

        private void SaveToFile()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "pynavis-output.txt",
            };
            if (dialog.ShowDialog(this) == true)
                System.IO.File.WriteAllText(dialog.FileName, FullText());
        }

        private void FindNext()
        {
            var hits = TextSearch.FindAll(FullText(), _findBox.Text);
            var next = TextSearch.NextAfter(hits, _lastFindOffset);
            _lastFindOffset = next;                     // -1 wraps to the top next time
            if (next >= 0) SelectTextOffset(next, _findBox.Text.Length);
            RefreshFindStatus(hits);
        }

        /// <summary>Keeps the "3 of 12" / "no matches" readout truthful.</summary>
        private void RefreshFindStatus(int[] hits = null)
        {
            if (_findStatus == null) return;
            _findStatus.Text = TextSearch.StatusFor(
                hits ?? TextSearch.FindAll(FullText(), _findBox.Text),
                _lastFindOffset, _findBox.Text);
        }

        /// <summary>Maps a plain-text offset (run-concatenation space) to pointers and selects.</summary>
        private void SelectTextOffset(int offset, int length)
        {
            var start = PointerAt(offset);
            var end = PointerAt(offset + length);
            if (start == null || end == null) return;

            _box.Selection.Select(start, end);
            var rect = start.GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty)
                _box.ScrollToVerticalOffset(_box.VerticalOffset + rect.Top - 60);
        }

        private TextPointer PointerAt(int offset)
        {
            var at = 0;
            foreach (var run in _paragraph.Inlines.OfType<Run>())
            {
                if (offset <= at + run.Text.Length)
                    return run.ContentStart.GetPositionAtOffset(offset - at);
                at += run.Text.Length;
            }
            return null;
        }
    }
}
