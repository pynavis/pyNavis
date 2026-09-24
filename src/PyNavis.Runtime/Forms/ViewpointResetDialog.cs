using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PyNavis.Runtime.Output;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Picks which saved viewpoints get the stored walk speed, turn speed and field
    /// of view written into them. The shared browser plus two shortcuts that ADD to
    /// the selection, the values that are about to be written spelled out so nothing
    /// is applied blind, and a plain primary action rather than the Deleter's
    /// destructive one: this rewrites cameras, it does not remove anything.
    ///
    /// The script applies; nothing here touches the document.
    /// </summary>
    public static class ViewpointResetDialog
    {
        private sealed class Parts
        {
            public ViewpointDialogShell Shell;
            public StackPanel Breakdown;
            public bool Confirmed;
        }

        private static readonly Dictionary<Window, Parts> Live = new Dictionary<Window, Parts>();

        private static Parts PartsOf(Window window) => Live[window];

        // ---- script-facing API -------------------------------------------------

        /// <summary>
        /// Shows the dialog; returns the guids to reset, or null on cancel.
        /// valuesSummary is what the settings will write ("30 m/s, 45 deg/sec,
        /// 84 deg FOV"), shown so the user can see it before committing.
        /// </summary>
        public static List<string> Show(IList<ViewpointRow> rows, string valuesSummary)
        {
            var window = Build(rows, valuesSummary);
            window.ShowDialog();
            var parts = PartsOf(window);
            Live.Remove(window);
            if (!parts.Confirmed) return null;
            return parts.Shell.Browser.Selection.SelectedLeaves().Select(r => r.Guid).ToList();
        }

        public static Window Build(IList<ViewpointRow> rows, string valuesSummary)
        {
            var t = Tokens.For(PyNavisTheme.IsDark);
            var shell = new ViewpointDialogShell("Reset viewpoints",
                "Pick the saved viewpoints to write the stored speeds into.", rows, t);
            var parts = new Parts { Shell = shell };
            Live[shell.Window] = parts;

            var criteria = new StackPanel();
            var label = DesignSystem.Text("Add to selection", 11.5, t.Muted);
            label.Margin = new Thickness(0, 0, 0, 6);
            criteria.Children.Add(label);

            var buttons = new WrapPanel();
            buttons.Children.Add(DesignSystem.Quiet(t, "Everything",
                () => shell.Browser.Selection.SetAll(shell.Browser.Visible, true)));
            buttons.Children.Add(DesignSystem.Quiet(t, "Animations",
                () => AddWhere(shell, r => r.Kind == "animation")));
            criteria.Children.Add(buttons);
            shell.Panel.Children.Add(criteria);

            // What is going to be written, in the same words the settings window uses.
            // Applying numbers the user cannot see would make this a guess.
            var values = DesignSystem.Text(
                string.IsNullOrEmpty(valuesSummary) ? "Nothing is switched on" : valuesSummary,
                12.5, t.Ink);
            values.TextWrapping = TextWrapping.Wrap;
            var valuesBox = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            valuesBox.Children.Add(values);
            var hint = DesignSystem.Text("Shift+Click the button to change these.", 11.5, t.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 6, 0, 0);
            valuesBox.Children.Add(hint);
            shell.Panel.Children.Add(DesignSystem.GroupFrame(t, "What gets written", valuesBox));

            parts.Breakdown = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            shell.Panel.Children.Add(DesignSystem.GroupFrame(t, "What changes", parts.Breakdown));

            shell.Primary.Click += (s, e) =>
            {
                parts.Confirmed = true;
                shell.Window.Close();
            };
            shell.Changed += () => Refresh(parts);
            Refresh(parts);
            return shell.Window;
        }

        private static void AddWhere(ViewpointDialogShell shell, Func<ViewpointRow, bool> match)
        {
            foreach (var index in shell.Browser.Visible)
            {
                var row = shell.Browser.Rows[index];
                if (match(row)) shell.Browser.Selection.Set(row.Guid, true);
            }
        }

        // ---- test seams --------------------------------------------------------

        public static bool ResetEnabledOf(Window window) =>
            PartsOf(window).Shell.Primary.IsEnabled;

        public static string PrimaryLabelOf(Window window) =>
            Convert.ToString(PartsOf(window).Shell.Primary.Content);

        public static void SelectEverythingForTest(Window window)
        {
            var shell = PartsOf(window).Shell;
            shell.Browser.Selection.SetAll(shell.Browser.Visible, true);
        }

        public static void SelectAnimationsForTest(Window window) =>
            AddWhere(PartsOf(window).Shell, r => r.Kind == "animation");

        public static int SelectedCountOf(Window window) =>
            PartsOf(window).Shell.Browser.Selection.Count;

        // ---- state -> chrome ---------------------------------------------------

        private static void Refresh(Parts parts)
        {
            var t = parts.Shell.T;
            var picked = parts.Shell.Browser.Selection.SelectedLeaves();
            var anims = picked.Count(r => r.Kind == "animation");

            parts.Breakdown.Children.Clear();
            parts.Breakdown.Children.Add(Line(t, "Viewpoints", picked.Count - anims));
            parts.Breakdown.Children.Add(Line(t, "Animations", anims, true));

            parts.Shell.Primary.Content = picked.Count > 0
                ? string.Format("Reset {0:n0}", picked.Count) : "Reset";
            parts.Shell.Primary.IsEnabled = picked.Count > 0;
        }

        private static FrameworkElement Line(Tokens t, string label, int value, bool muted = false)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var name = DesignSystem.Text(label, 12.5, muted ? t.Muted : t.Ink);
            row.Children.Add(name);
            var count = DesignSystem.TabularNumber(value.ToString("n0"), 12.5,
                muted ? t.Muted : t.Ink);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            return row;
        }
    }
}
