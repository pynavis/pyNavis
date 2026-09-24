using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The shared viewpoint dialog at sizes it has to survive. A Grid whose
    /// columns do not fit is not laid out smaller: it overflows its slot and WPF
    /// clips the overflow away. That is how the list lost its scrollbar in the
    /// field - the rail had been dragged wide, so rail + seam + the list's
    /// minimum came to more than the pane had, and the 17px that fell outside
    /// were exactly the scrollbar.
    /// </summary>
    public class ViewpointDialogLayoutTests
    {
        private static List<ViewpointRow> Rows(int leaves)
        {
            var rows = new List<ViewpointRow>
            {
                new ViewpointRow { Guid = "f0", Key = "0", ParentKey = "", Folder = "",
                                   Name = "Micron Project C1403889", Kind = "folder", IsFolder = true },
            };
            for (var i = 0; i < leaves; i++)
                rows.Add(new ViewpointRow
                {
                    Guid = "g" + i,
                    Key = "0/" + i,
                    ParentKey = "0",
                    Folder = "Micron Project C1403889",
                    Name = "North Tower | Sector B/C " + i,
                    Kind = "viewpoint",
                    Depth = 1,
                });
            return rows;
        }

        /// <summary>A shell with the pane widths the field report had: the folder
        /// rail dragged wide and the operation pane at its minimum.</summary>
        private static ViewpointDialogShell WideRail(double rail = 381)
        {
            var shell = new ViewpointDialogShell("Reset viewpoints",
                "Pick the saved viewpoints to write the stored speeds into.",
                Rows(211), DesignSystem.Tokens.For(false));
            shell.LayoutKey = null;             // never read or write the user's config
            shell.Browser.RailWidth = rail;
            shell.PanelWidth = 240;
            return shell;
        }

        /// <summary>Shows the window off-screen so layout runs against a real
        /// client area - the width the frame leaves is part of what is under
        /// test - then closes it whatever the check does.</summary>
        private static void Shown(Window window, double width, Action check)
        {
            window.Width = width;
            window.Height = 560;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.Show();
            window.UpdateLayout();
            try { check(); }
            finally { window.Close(); }
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) yield return hit;
                foreach (var deep in Descendants<T>(child)) yield return deep;
            }
        }

        private static double RightEdge(FrameworkElement element, Visual root) =>
            element.TransformToAncestor(root).Transform(new Point(0, 0)).X + element.ActualWidth;

        [Theory]
        [InlineData(1000, 381)]     // the field report: the size it opens at, rail dragged wide
        [InlineData(1000, 700)]     // the seam pulled as far right as it will go
        [InlineData(0, 381)]        // width 0 means "as narrow as the window can go"
        [InlineData(0, 700)]
        public void TheListKeepsItsScrollbar_HoweverWideTheRailIs(double width, double rail)
        {
            OnSta(() =>
            {
                var shell = WideRail(rail);
                Shown(shell.Window, width > 0 ? width : shell.Window.MinWidth, () =>
                {
                    var root = (FrameworkElement)shell.Window.Content;
                    var list = Descendants<ListBox>(root).Single();
                    var bar = Descendants<ScrollBar>(list)
                        .Single(s => s.Orientation == Orientation.Vertical);

                    Assert.Equal(Visibility.Visible, bar.Visibility);

                    // 211 rows in a 400px list: it is the scrollbar or nothing.
                    var pane = shell.Browser.View;
                    Assert.True(RightEdge(bar, root) <= RightEdge(pane, root) + 0.5,
                        string.Format(
                            "the list's scrollbar ends at {0:n0} but the browser pane ends at {1:n0}, "
                            + "so the part of it past the pane is clipped away",
                            RightEdge(bar, root), RightEdge(pane, root)));
                });
            });
        }

        [Fact]
        public void NeitherPaneGrid_OverflowsTheSpaceItWasGiven()
        {
            OnSta(() =>
            {
                var shell = WideRail(700);
                Shown(shell.Window, shell.Window.MinWidth, () =>
                {
                    var root = (FrameworkElement)shell.Window.Content;
                    foreach (var grid in Descendants<Grid>(root)
                        .Where(g => g.ColumnDefinitions.Count == 3))
                    {
                        var slot = LayoutInformation.GetLayoutSlot(grid);
                        Assert.True(grid.RenderSize.Width <= slot.Width + 0.5, string.Format(
                            "a {0}-wide grid was given {1:n0}: the columns [{2}] do not fit, "
                            + "so everything past {1:n0} is clipped",
                            grid.RenderSize.Width, slot.Width,
                            string.Join(",", grid.ColumnDefinitions.Select(c => c.ActualWidth.ToString("n0")))));
                    }
                });
            });
        }

        [Fact]
        public void TheShowingCount_StaysOnOneLine_AtTheNarrowestWindow()
        {
            OnSta(() =>
            {
                var shell = WideRail();
                Shown(shell.Window, shell.Window.MinWidth, () =>
                {
                    var root = (FrameworkElement)shell.Window.Content;
                    var showing = Descendants<TextBlock>(root)
                        .Single(t => (t.Text ?? "").StartsWith("Showing"));

                    // Squeezed to a few pixels this wraps one character per line
                    // and grows into a tower that eats the dialog's height.
                    Assert.True(showing.ActualHeight < 40,
                        string.Format("'{0}' is {1:n0} tall, so it has wrapped into a column",
                            showing.Text, showing.ActualHeight));
                });
            });
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
