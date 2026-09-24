using System;
using System.Collections.Generic;
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
