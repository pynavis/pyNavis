using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The browser control itself. The load-bearing test is the virtualization
    /// proof: 20,000 rows must realize only the handful of containers actually
    /// on screen, which is exactly what the old ViewpointTree could not do.
    /// </summary>
    public class ViewpointBrowserTests
    {
        private static List<ViewpointRow> BigSet(int leaves)
        {
            var rows = new List<ViewpointRow>
            {
                new ViewpointRow { Guid = "f0", Key = "0", ParentKey = "", Folder = "",
                                   Name = "Coordination", Kind = "folder", IsFolder = true },
            };
            for (var i = 0; i < leaves; i++)
                rows.Add(new ViewpointRow
                {
                    Guid = "g" + i,
                    Key = "0/" + i,
                    ParentKey = "0",
                    Folder = "Coordination",
                    Name = (i % 50 == 0 ? "North " : "View ") + i,
                    Kind = i % 500 == 0 ? "animation" : "viewpoint",
                    Depth = 1,
                });
            return rows;
        }

        /// <summary>Hosts the control in a real (never shown) window and pumps
        /// layout, which is what forces the ListBox to realize containers.</summary>
        private static void Realized(ViewpointBrowser browser, Action check)
        {
            var window = new Window
            {
                Width = 900,
                Height = 600,
                Content = browser.View,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            browser.PumpForTest();
            try { check(); }
            finally { window.Close(); }
        }

        [Fact]
        public void TwentyThousandRows_RealizeOnlyWhatIsOnScreen()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(BigSet(20000), DesignSystem.Tokens.For(false));

                Realized(browser, () =>
                {
                    Assert.Equal(20000, browser.VisibleCount);
                    Assert.True(browser.RealizedRowCount > 0, "nothing was realized at all");
                    Assert.True(browser.RealizedRowCount < 120,
                        "realized " + browser.RealizedRowCount + " containers: not virtualizing");
                });
            });
        }

        [Fact]
        public void Search_NarrowsTheVisibleSet()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(BigSet(1000), DesignSystem.Tokens.For(false));
                Assert.Equal(1000, browser.VisibleCount);

                browser.SearchForTest("north");

                Assert.Equal(20, browser.VisibleCount);   // every 50th of 1000
            });
        }

        [Fact]
        public void Chip_NarrowsToAnimations()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(BigSet(1000), DesignSystem.Tokens.For(false));

                browser.ChipForTest("animations");

                Assert.Equal(2, browser.VisibleCount);    // indexes 0 and 500
            });
        }

        [Fact]
        public void SelectAll_OverTheVisibleSet_CountsThatSetOnly()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(BigSet(1000), DesignSystem.Tokens.For(false));
                browser.SearchForTest("north");

                browser.Selection.SetAll(browser.Visible, true);

                Assert.Equal(20, browser.Selection.Count);
            });
        }

        [Fact]
        public void SetRows_ReplacesTheContent_AndClearsSelection()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(BigSet(500), DesignSystem.Tokens.For(false));
                browser.Selection.SetAll(browser.Visible, true);

                browser.SetRows(BigSet(10));

                Assert.Equal(10, browser.VisibleCount);
                Assert.Equal(0, browser.Selection.Count);
            });
        }

        [Fact]
        public void FolderScope_LimitsTheList()
        {
            OnSta(() =>
            {
                var rows = BigSet(10);
                rows.Add(new ViewpointRow
                {
                    Guid = "other", Key = "1", ParentKey = "", Folder = "",
                    Name = "Loose", Kind = "viewpoint",
                });
                var browser = new ViewpointBrowser(rows, DesignSystem.Tokens.For(false));
                Assert.Equal(11, browser.VisibleCount);

                browser.ScopeForTest("Coordination");

                Assert.Equal(10, browser.VisibleCount);
            });
        }

        // Level 1 holds a nested Deck folder; Level 10 is a prefix sibling that a
        // match without the separator would fold into Level 1.
        private static List<ViewpointRow> Nested() => new List<ViewpointRow>
        {
            new ViewpointRow { Guid = "f0", Key = "0", ParentKey = "", Folder = "", Name = "Level 1", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "a", Key = "0/0", ParentKey = "0", Folder = "Level 1", Name = "North", Kind = "viewpoint", Depth = 1 },
            new ViewpointRow { Guid = "b", Key = "0/1", ParentKey = "0", Folder = "Level 1", Name = "South", Kind = "viewpoint", Depth = 1 },
            new ViewpointRow { Guid = "f1", Key = "0/2", ParentKey = "0", Folder = "Level 1", Name = "Deck", Kind = "folder", IsFolder = true, Depth = 1 },
            new ViewpointRow { Guid = "c", Key = "0/2/0", ParentKey = "0/2", Folder = "Level 1/Deck", Name = "East", Kind = "viewpoint", Depth = 2 },
            new ViewpointRow { Guid = "f2", Key = "1", ParentKey = "", Folder = "", Name = "Level 10", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "d", Key = "1/0", ParentKey = "1", Folder = "Level 10", Name = "Roof", Kind = "animation", Depth = 1 },
            new ViewpointRow { Guid = "f3", Key = "2", ParentKey = "", Folder = "", Name = "Empty", Kind = "folder", IsFolder = true },
        };

        [Fact]
        public void SelectEverythingInside_TakesTheFolderAndItsSubfolders_InOneGesture()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(Nested(), DesignSystem.Tokens.For(false));
                var fired = 0;
                browser.SelectionChanged += () => fired++;

                browser.SelectEverythingInside("Level 1");

                Assert.Equal(1, fired);
                Assert.Equal(new[] { "a", "b", "c" },
                    browser.Selection.SelectedLeaves().ConvertAll(r => r.Guid));
            });
        }

        [Fact]
        public void SelectAllInFolder_TakesThatFolderOnly_InOneGesture()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(Nested(), DesignSystem.Tokens.For(false));
                var fired = 0;
                browser.SelectionChanged += () => fired++;

                browser.SelectAllInFolder("Level 1");

                Assert.Equal(1, fired);
                Assert.Equal(new[] { "a", "b" },
                    browser.Selection.SelectedLeaves().ConvertAll(r => r.Guid));
            });
        }

        [Fact]
        public void Rail_CountsLeavesBelowEachFolder_AndWhatIsSelectedThere()
        {
            OnSta(() =>
            {
                var browser = new ViewpointBrowser(Nested(), DesignSystem.Tokens.For(false));
                Assert.Equal(new[]
                {
                    ("All viewpoints", 4, 0, false),
                    ("Level 1", 3, 0, true),
                    ("Deck", 1, 0, true),
                    ("Level 10", 1, 0, true),
                    ("Empty", 0, 0, false),
                }, browser.RailForTest);

                // Ticking Deck carries East with it, and both sit under Level 1.
                browser.Selection.Set("f1", true);

                Assert.Equal(new[]
                {
                    ("All viewpoints", 4, 0, false),
                    ("Level 1", 3, 2, true),
                    ("Deck", 1, 1, true),
                    ("Level 10", 1, 0, true),
                    ("Empty", 0, 0, false),
                }, browser.RailForTest);

                // A collapsed folder hides what is under it; a re-snapshot recounts.
                browser.CollapseAll();
                Assert.Equal(new[] { "All viewpoints", "Level 1", "Level 10", "Empty" },
                    browser.RailForTest.Select(r => r.Label));

                var fewer = Nested();
                fewer.RemoveAll(r => r.Guid == "b");
                browser.SetRows(fewer);
                browser.ExpandAll();
                Assert.Equal(new[]
                {
                    ("All viewpoints", 3, 0, false),
                    ("Level 1", 2, 0, true),
                    ("Deck", 1, 0, true),
                    ("Level 10", 1, 0, true),
                    ("Empty", 0, 0, false),
                }, browser.RailForTest);
            });
        }

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA action failed: " + failure);
        }
    }
}
