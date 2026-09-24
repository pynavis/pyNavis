using System;
using System.Collections.Generic;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Deleter seams: nothing is deletable until something is selected, the
    /// criteria shortcuts add to the selection, and the breakdown reports by
    /// type so "412 selected" cannot hide what it means.
    /// </summary>
    public class ViewpointDeleterDialogTests
    {
        private static List<ViewpointRow> Rows() => new List<ViewpointRow>
        {
            new ViewpointRow { Guid = "f", Key = "0", ParentKey = "", Folder = "", Name = "Site", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "a", Key = "0/0", ParentKey = "0", Folder = "Site", Name = "North", Kind = "viewpoint", Depth = 1, Comments = 2 },
            new ViewpointRow { Guid = "b", Key = "0/1", ParentKey = "0", Folder = "Site", Name = "South", Kind = "viewpoint", Depth = 1 },
            new ViewpointRow { Guid = "c", Key = "1", ParentKey = "", Folder = "", Name = "Walk", Kind = "animation" },
        };

        [Fact]
        public void Builds_WithDeleteDisabled_UntilSomethingIsSelected()
        {
            OnSta(() =>
            {
                var window = ViewpointDeleterDialog.Build(Rows());
                Assert.False(ViewpointDeleterDialog.DeleteEnabledOf(window));

                ViewpointDeleterDialog.SelectAnimationsForTest(window);

                Assert.True(ViewpointDeleterDialog.DeleteEnabledOf(window));
                Assert.Equal(1, ViewpointDeleterDialog.SelectedCountOf(window));
            });
        }

        [Fact]
        public void Criteria_AddToTheSelection_RatherThanReplacingIt()
        {
            OnSta(() =>
            {
                var window = ViewpointDeleterDialog.Build(Rows());

                ViewpointDeleterDialog.SelectAnimationsForTest(window);
                ViewpointDeleterDialog.SelectWhereCommentedForTest(window);

                Assert.Equal(2, ViewpointDeleterDialog.SelectedCountOf(window));
            });
        }

        [Fact]
        public void Summary_BreaksTheSelectionDownByType()
        {
            OnSta(() =>
            {
                var window = ViewpointDeleterDialog.Build(Rows());

                ViewpointDeleterDialog.SelectAnimationsForTest(window);
                ViewpointDeleterDialog.SelectWhereCommentedForTest(window);

                Assert.Equal("1 viewpoint, 1 animation", ViewpointDeleterDialog.SummaryOf(window));
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
