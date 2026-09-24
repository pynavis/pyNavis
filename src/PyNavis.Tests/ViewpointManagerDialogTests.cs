using System;
using System.Collections.Generic;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Manager seams: a successful action rebuilds the browser from the fresh
    /// snapshot the delegate returns, and a failing or throwing delegate leaves
    /// the dialog alive on the rows it already had.
    /// </summary>
    public class ViewpointManagerDialogTests
    {
        private static List<ViewpointRow> Rows(int leaves = 2)
        {
            var rows = new List<ViewpointRow>
            {
                new ViewpointRow { Guid = "f", Key = "0", ParentKey = "", Folder = "", Name = "Site", Kind = "folder", IsFolder = true },
            };
            for (var i = 0; i < leaves; i++)
                rows.Add(new ViewpointRow
                {
                    Guid = "g" + i, Key = "0/" + i, ParentKey = "0", Folder = "Site",
                    Name = "View " + i, Kind = "viewpoint", Depth = 1,
                });
            return rows;
        }

        [Fact]
        public void SuccessfulAction_RebuildsFromTheReturnedSnapshot()
        {
            OnSta(() =>
            {
                var window = ViewpointManagerDialog.Build(Rows(2), action =>
                    new ViewpointManagerDialog.ManagerResult
                    {
                        Success = true,
                        Message = "Sorted 4 item(s).",
                        Rows = Rows(5),
                    });

                Assert.Equal(3, ViewpointManagerDialog.RowCountOf(window));   // folder + 2

                ViewpointManagerDialog.RunActionForTest(window, "sort");

                Assert.Equal(6, ViewpointManagerDialog.RowCountOf(window));   // folder + 5
                Assert.Equal("Sorted 4 item(s).", ViewpointManagerDialog.StatusTextOf(window));
            });
        }

        [Fact]
        public void FailedAction_KeepsTheRows_AndExplains()
        {
            OnSta(() =>
            {
                var window = ViewpointManagerDialog.Build(Rows(2), _ =>
                    new ViewpointManagerDialog.ManagerResult
                    { Success = false, Message = "Cannot move a folder into itself." });

                ViewpointManagerDialog.RunActionForTest(window, "move");

                Assert.Equal(3, ViewpointManagerDialog.RowCountOf(window));
                Assert.Contains("into itself", ViewpointManagerDialog.StatusTextOf(window));
            });
        }

        [Fact]
        public void ThrowingDelegate_ShowsFailure_AndSurvives()
        {
            OnSta(() =>
            {
                var window = ViewpointManagerDialog.Build(
                    Rows(2), _ => throw new InvalidOperationException("engine says no"));

                ViewpointManagerDialog.RunActionForTest(window, "purge");

                Assert.Contains("Action failed", ViewpointManagerDialog.StatusTextOf(window));
                Assert.Equal(3, ViewpointManagerDialog.RowCountOf(window));
            });
        }

        [Fact]
        public void StatsLine_ReportsTheContents()
        {
            OnSta(() =>
            {
                var window = ViewpointManagerDialog.Build(Rows(2), _ =>
                    new ViewpointManagerDialog.ManagerResult { Success = true });

                Assert.Equal("1 folder, 2 viewpoints, 0 animations",
                    ViewpointManagerDialog.StatsTextOf(window));
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
