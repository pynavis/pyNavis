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
    /// Bulk deleter: the shared browser plus criteria shortcuts that ADD to the
    /// selection, a breakdown of what is about to go (including how many carry
    /// review comments), and one destructive action. The script confirms and
    /// applies; nothing here touches the document.
    /// </summary>
    public static class ViewpointDeleterDialog
    {
        private sealed class Parts
        {
            public ViewpointDialogShell Shell;
            public StackPanel Breakdown;
            public TextBlock Warning;
            public bool Confirmed;
        }

        private static readonly Dictionary<Window, Parts> Live = new Dictionary<Window, Parts>();

        private static Parts PartsOf(Window window) => Live[window];

        // ---- script-facing API -------------------------------------------------

        /// <summary>Shows the dialog; returns the guids to delete, or null on cancel.</summary>
        public static List<string> Show(IList<ViewpointRow> rows)
        {
            var window = Build(rows);
            window.ShowDialog();
            var parts = PartsOf(window);
            Live.Remove(window);
            if (!parts.Confirmed) return null;
            return parts.Shell.Browser.Selection.SelectedLeaves().Select(r => r.Guid).ToList();
        }

        public static Window Build(IList<ViewpointRow> rows)
        {
            var t = Tokens.For(PyNavisTheme.IsDark);
            var shell = new ViewpointDialogShell("Viewpoint Deleter",
                "Select what goes. Deleting cannot be undone from pyNavis.", rows, t);
            var parts = new Parts { Shell = shell };
            Live[shell.Window] = parts;

            var criteria = new StackPanel();
            var label = DesignSystem.Text("Add to selection", 11.5, t.Muted);
            label.Margin = new Thickness(0, 0, 0, 6);
            criteria.Children.Add(label);

            var buttons = new WrapPanel();
            buttons.Children.Add(DesignSystem.Quiet(t, "Animations",
                () => AddWhere(shell, r => r.Kind == "animation")));
            buttons.Children.Add(DesignSystem.Quiet(t, "Duplicates",
                () => AddWhere(shell, r => r.Duplicate)));
            buttons.Children.Add(DesignSystem.Quiet(t, "With comments",
                () => AddWhere(shell, r => r.Comments > 0)));
            criteria.Children.Add(buttons);
            shell.Panel.Children.Add(criteria);

            parts.Breakdown = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            shell.Panel.Children.Add(DesignSystem.GroupFrame(t, "What goes", parts.Breakdown));

            parts.Warning = DesignSystem.Text("", 11.5, t.Muted);
            parts.Warning.TextWrapping = TextWrapping.Wrap;
            parts.Warning.Margin = new Thickness(0, 10, 0, 0);
            shell.Panel.Children.Add(parts.Warning);

            shell.MakePrimaryDangerous("Delete", () =>
            {
                parts.Confirmed = true;
                shell.Window.Close();
            });
            shell.Browser.DeleteRequested += () =>
            {
                if (shell.Primary.IsEnabled)
                {
                    parts.Confirmed = true;
                    shell.Window.Close();
                }
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

        public static bool DeleteEnabledOf(Window window) =>
            PartsOf(window).Shell.Primary.IsEnabled;

        public static string SummaryOf(Window window) =>
            PartsOf(window).Shell.BreakdownTextForTest;

        public static void SelectAnimationsForTest(Window window) =>
            AddWhere(PartsOf(window).Shell, r => r.Kind == "animation");

        public static void SelectWhereCommentedForTest(Window window) =>
            AddWhere(PartsOf(window).Shell, r => r.Comments > 0);

        public static int SelectedCountOf(Window window) =>
            PartsOf(window).Shell.Browser.Selection.Count;

        // ---- state -> chrome ---------------------------------------------------

        private static void Refresh(Parts parts)
        {
            var t = parts.Shell.T;
            var picked = parts.Shell.Browser.Selection.SelectedLeaves();
            var anims = picked.Count(r => r.Kind == "animation");
            var commented = picked.Count(r => r.Comments > 0);

            parts.Breakdown.Children.Clear();
            parts.Breakdown.Children.Add(Line(t, "Viewpoints", picked.Count - anims));
            parts.Breakdown.Children.Add(Line(t, "Animations", anims));
            parts.Breakdown.Children.Add(Line(t, "Carrying comments", commented, true));

            parts.Warning.Text = commented > 0
                ? string.Format(
                    "{0:n0} of these carry review comments, which go with them. A bulk delete this size may exceed the Navisworks undo stack.",
                    commented)
                : "A bulk delete this size may exceed the Navisworks undo stack.";

            parts.Shell.Primary.Content = picked.Count > 0
                ? string.Format("Delete {0:n0}", picked.Count) : "Delete";
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
