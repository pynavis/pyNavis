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
    /// Manager: sort, move into a folder, create folders, purge empty ones.
    /// Actions apply live through a delegate that mutates the document and
    /// hands back a fresh snapshot, because every structural change reshuffles
    /// the tree. Rows are keyed by GUID, so the browser survives that refresh.
    /// </summary>
    public static class ViewpointManagerDialog
    {
        public sealed class ManagerAction
        {
            public string Kind { get; set; }             // sort|move|create|purge
            public string TargetKey { get; set; } = "";  // folder path, "" = top level
            public string Name { get; set; } = "";
            public List<string> Guids { get; } = new List<string>();
        }

        public sealed class ManagerResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            /// <summary>Fresh snapshot after a mutation; null keeps what is shown.</summary>
            public List<ViewpointRow> Rows { get; set; }
        }

        private sealed class Parts
        {
            public ViewpointDialogShell Shell;
            public Func<ManagerAction, ManagerResult> Execute;
            public ComboBox Folders;
            public List<string> FolderPaths = new List<string>();
            public TextBox NewName;
            public TextBlock Status;
            public TextBlock Stats;
        }

        private static readonly Dictionary<Window, Parts> Live = new Dictionary<Window, Parts>();

        private static Parts PartsOf(Window window) => Live[window];

        // ---- script-facing API -------------------------------------------------

        public static void Show(IList<ViewpointRow> rows, Func<ManagerAction, ManagerResult> execute)
        {
            var window = Build(rows, execute);
            window.ShowDialog();
            Live.Remove(window);
        }

        public static Window Build(IList<ViewpointRow> rows, Func<ManagerAction, ManagerResult> execute)
        {
            var t = Tokens.For(PyNavisTheme.IsDark);
            var shell = new ViewpointDialogShell("Viewpoint Manager",
                "Actions apply immediately. Select rows to scope a move.", rows, t);
            var parts = new Parts { Shell = shell, Execute = execute };
            Live[shell.Window] = parts;

            parts.Folders = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            shell.Panel.Children.Add(Caption(t, "Move into"));
            shell.Panel.Children.Add(parts.Folders);

            shell.Panel.Children.Add(Caption(t, "Applies straight away"));
            var jobs = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            jobs.Children.Add(DesignSystem.Quiet(t, "Sort A-Z", () => Run(parts, "sort")));
            var makeRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            parts.NewName = new TextBox { FontSize = 13, Width = 130 };
            var field = DesignSystem.InputField(t, parts.NewName);
            DockPanel.SetDock(field, Dock.Right);
            makeRow.Children.Add(field);
            makeRow.Children.Add(DesignSystem.Quiet(t, "New folder", () => Run(parts, "create")));
            jobs.Children.Add(makeRow);
            jobs.Children.Add(DesignSystem.Quiet(t, "Purge empty folders", () => Run(parts, "purge")));
            shell.Panel.Children.Add(jobs);

            parts.Status = DesignSystem.Text("", 12, t.Muted);
            parts.Status.TextWrapping = TextWrapping.Wrap;
            parts.Status.Margin = new Thickness(0, 4, 0, 10);
            shell.Panel.Children.Add(parts.Status);

            parts.Stats = DesignSystem.Text("", 12, t.Ink);
            parts.Stats.TextWrapping = TextWrapping.Wrap;
            shell.Panel.Children.Add(DesignSystem.GroupFrame(t, "Contents", parts.Stats));

            shell.Primary.Content = "Move";
            shell.Primary.Click += (s, e) => Run(parts, "move");
            shell.Close.Content = "Close";
            shell.Changed += () => RefreshStrip(parts);

            InstallRows(parts, rows);
            return shell.Window;
        }

        private static FrameworkElement Caption(Tokens t, string text)
        {
            var block = DesignSystem.Text(text, 11.5, t.Muted);
            block.Margin = new Thickness(0, 6, 0, 4);
            return block;
        }

        // ---- test seams --------------------------------------------------------

        public static string StatusTextOf(Window window) => PartsOf(window).Status.Text;

        public static string StatsTextOf(Window window) => PartsOf(window).Stats.Text;

        public static int RowCountOf(Window window) => PartsOf(window).Shell.Browser.Rows.Count;

        public static void RunActionForTest(Window window, string kind) =>
            Run(PartsOf(window), kind);

        // ---- actions -----------------------------------------------------------

        private static void Run(Parts parts, string kind)
        {
            var action = new ManagerAction
            {
                Kind = kind,
                TargetKey = TargetOf(parts),
                Name = parts.NewName?.Text ?? "",
            };
            foreach (var row in parts.Shell.Browser.Selection.SelectedLeaves())
                action.Guids.Add(row.Guid);

            ManagerResult result;
            try
            {
                result = parts.Execute(action) ?? Failure("Action failed. Check the log for details.");
            }
            catch (Exception ex)
            {
                Log.Error("Manager action '" + kind + "' failed", ex);
                result = Failure("Action failed. Check the log for details.");
            }

            parts.Status.Text = result.Message ?? "";
            parts.Status.Foreground = DesignSystem.Brush(
                result.Success ? parts.Shell.T.Muted : parts.Shell.T.Error);

            if (result.Success && result.Rows != null) InstallRows(parts, result.Rows);
        }

        private static ManagerResult Failure(string message) =>
            new ManagerResult { Success = false, Message = message };

        private static string TargetOf(Parts parts)
        {
            var index = parts.Folders.SelectedIndex;
            return index <= 0 || index > parts.FolderPaths.Count ? "" : parts.FolderPaths[index - 1];
        }

        private static void InstallRows(Parts parts, IList<ViewpointRow> rows)
        {
            parts.Shell.Browser.SetRows(rows);

            parts.FolderPaths = rows.Where(r => r.IsFolder)
                .Select(r => string.IsNullOrEmpty(r.Folder) ? r.Name : r.Folder + "/" + r.Name)
                .ToList();
            var items = new List<string> { "(top level)" };
            items.AddRange(rows.Where(r => r.IsFolder)
                .Select(r => new string(' ', r.Depth * 2) + r.Name));
            parts.Folders.ItemsSource = items;
            parts.Folders.SelectedIndex = 0;

            parts.Stats.Text = ViewpointFilter.StatsText(rows);
            RefreshStrip(parts);
        }

        private static void RefreshStrip(Parts parts)
        {
            var picked = parts.Shell.Browser.Selection.Count;
            parts.Shell.Primary.IsEnabled = picked > 0;
            parts.Shell.Note.Text = picked > 0 ? string.Format("{0:n0} to move", picked) : "";
        }
    }
}
