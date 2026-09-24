using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PyNavis.Runtime.Output;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The pyNavis message dialogs (alert / confirm / ask), built on the shared
    /// DesignSystem v2 toolkit: a solid paper surface, the real Windows titlebar,
    /// the accent only on the primary button, and a bordered v2 input field.
    /// pynavis.forms calls these, so every python script gets the look with no
    /// script changes. Build* methods construct without showing - the test seam.
    /// </summary>
    public static class Dialogs
    {
        // ---- script-facing API -------------------------------------------------

        public static void Alert(string message, string title)
        {
            var window = BuildAlert(message, title);
            window.ShowDialog();
        }

        public static bool Confirm(string message, string title)
        {
            var window = BuildConfirm(message, title);
            return window.ShowDialog() == true;
        }

        /// <summary>Entered string, or null on cancel.</summary>
        public static string AskString(string prompt, string defaultValue, string title)
        {
            var window = BuildAskString(prompt, defaultValue, title);
            return window.ShowDialog() == true ? PartsOf(window).Input.Text : null;
        }

        public static string SaveFile(string filter, string defaultName, string title)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter, FileName = defaultName, Title = title,
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static string OpenFile(string filter, string title)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = title };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        // ---- construction (test seam) -----------------------------------------

        public static Window BuildAlert(string message, string title) =>
            Build(title, message, null, new[] { ("OK", true) }, false);

        /// <summary>A yes/no question. Confirm is the dialog callers reach for
        /// before destroying something, so Enter picks No, not Yes.</summary>
        public static Window BuildConfirm(string message, string title) =>
            Build(title, message, null, new[] { ("Yes", true), ("No", false) }, true);

        public static Window BuildAskString(string prompt, string defaultValue, string title) =>
            Build(title, prompt, defaultValue ?? "", new[] { ("OK", true), ("Cancel", false) }, false);

        public static string MessageTextOf(Window window) => PartsOf(window).Message.Text;
        public static string InputTextOf(Window window) => PartsOf(window).Input.Text;
        public static int ButtonCountOf(Window window) => PartsOf(window).Buttons.Count;

        /// <summary>The footer buttons in order, for pinning the keyboard contract.</summary>
        public static IReadOnlyList<Button> ButtonsForTest(Window window) => PartsOf(window).Buttons;

        // ---- internals ---------------------------------------------------------

        private sealed class Parts
        {
            public TextBlock Message;
            public TextBox Input;
            public List<Button> Buttons = new List<Button>();
        }

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        private static Window Build(
            string title, string message, string inputDefault,
            (string label, bool result)[] buttons, bool defaultIsSafe)
        {
            var t = DesignSystem.Tokens.Current;
            var parts = new Parts();

            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 380,
                MaxWidth = 520,
                ResizeMode = ResizeMode.NoResize,          // real OS titlebar, close only
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = DesignSystem.Brush(t.Paper),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Tag = parts,
                ShowInTaskbar = false,
            };
            FluentChrome.Apply(window);                    // rounded corners + dark titlebar attr

            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            parts.Message = DesignSystem.Text(message, 13, t.Ink);   // 13 is body
            body.Children.Add(parts.Message);

            parts.Input = new TextBox { Text = inputDefault ?? "", Height = 32 };
            var inputField = DesignSystem.InputField(t, parts.Input);
            inputField.Margin = new Thickness(0, 16, 0, 0);
            inputField.Visibility = inputDefault != null ? Visibility.Visible : Visibility.Collapsed;
            body.Children.Add(inputField);

            var footer = new Border
            {
                Background = DesignSystem.Brush(t.Surface),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 12, 24, 12),
                Child = BuildButtonRow(window, parts, buttons, t, defaultIsSafe),
            };

            var layout = new StackPanel();
            layout.Children.Add(body);
            layout.Children.Add(footer);
            window.Content = layout;

            window.ContentRendered += (s, e) =>
            {
                if (parts.Input.Visibility == Visibility.Visible
                    || inputDefault != null)
                {
                    parts.Input.Focus();
                    parts.Input.SelectAll();
                }
            };
            return window;
        }

        private static StackPanel BuildButtonRow(
            Window window, Parts parts, (string label, bool result)[] buttons,
            DesignSystem.Tokens t, bool defaultIsSafe)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            // Keyboard contract: Escape always leads somewhere, and Enter never
            // triggers a destructive answer. The LAST button is the way out, so
            // it takes Escape. Enter goes to the first button (commit) unless
            // the caller says the safe answer should win, which Confirm does:
            // Enter on "Delete 4,200 viewpoints?" must not delete them.
            var single = buttons.Length == 1;
            var index = 0;
            foreach (var (label, result) in buttons)
            {
                var captured = result;
                var first = index == 0;
                var last = index == buttons.Length - 1;
                var button = first
                    ? DesignSystem.Primary(t, label, () => window.DialogResult = captured)
                    : DesignSystem.Secondary(t, label, () => window.DialogResult = captured);
                button.IsDefault = single || (defaultIsSafe ? last : first);
                button.IsCancel = last;
                button.Margin = new Thickness(8, 0, 0, 0);
                parts.Buttons.Add(button);
                row.Children.Add(button);
                index++;
            }
            return row;
        }
    }
}
