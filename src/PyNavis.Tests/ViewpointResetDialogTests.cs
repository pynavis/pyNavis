using System;
using System.Collections.Generic;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Reset picker seams: nothing is resettable until something is ticked, the
    /// shortcuts ADD rather than replace, the primary button counts what it is
    /// about to write, and the values being written are spelled out so a reset is
    /// never applied blind.
    /// </summary>
    public class ViewpointResetDialogTests
    {
        private static List<ViewpointRow> Rows() => new List<ViewpointRow>
        {
            new ViewpointRow { Guid = "f", Key = "0", ParentKey = "", Folder = "", Name = "Site", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "a", Key = "0/0", ParentKey = "0", Folder = "Site", Name = "North", Kind = "viewpoint", Depth = 1, Comments = 2 },
            new ViewpointRow { Guid = "b", Key = "0/1", ParentKey = "0", Folder = "Site", Name = "South", Kind = "viewpoint", Depth = 1 },
            new ViewpointRow { Guid = "c", Key = "1", ParentKey = "", Folder = "", Name = "Walk", Kind = "animation" },
        };

        private const string Summary = "30 m/s, 45 deg/sec, 84 deg FOV";

        [Fact]
        public void Builds_WithResetDisabled_UntilSomethingIsTicked()
        {
            OnSta(() =>
            {
                var window = ViewpointResetDialog.Build(Rows(), Summary);
                Assert.False(ViewpointResetDialog.ResetEnabledOf(window));

                ViewpointResetDialog.SelectAnimationsForTest(window);

                Assert.True(ViewpointResetDialog.ResetEnabledOf(window));
                Assert.Equal(1, ViewpointResetDialog.SelectedCountOf(window));
            });
        }

        [Fact]
        public void ThePrimaryButton_CountsWhatItIsAboutToWrite()
        {
            OnSta(() =>
            {
                var window = ViewpointResetDialog.Build(Rows(), Summary);
                Assert.Equal("Reset", ViewpointResetDialog.PrimaryLabelOf(window));

                ViewpointResetDialog.SelectEverythingForTest(window);

                // Three leaves: two viewpoints and one animation. The folder is not
                // a leaf, so ticking everything must not claim four.
                Assert.Equal("Reset 3", ViewpointResetDialog.PrimaryLabelOf(window));
            });
        }

        [Fact]
        public void Everything_TicksEveryLeaf_AndAnimationsAddToIt_RatherThanReplacingIt()
        {
            OnSta(() =>
            {
                var window = ViewpointResetDialog.Build(Rows(), Summary);

                ViewpointResetDialog.SelectAnimationsForTest(window);
                Assert.Equal(1, ViewpointResetDialog.SelectedCountOf(window));

                ViewpointResetDialog.SelectEverythingForTest(window);
                Assert.Equal(3, ViewpointResetDialog.SelectedCountOf(window));

                // Running the animation shortcut again must not drop the others.
                ViewpointResetDialog.SelectAnimationsForTest(window);
                Assert.Equal(3, ViewpointResetDialog.SelectedCountOf(window));
            });
        }

        [Fact]
        public void ASummaryOfNothing_StillBuilds_SoTheDialogNeverThrowsOnEmptySettings()
        {
            // The script refuses to open the dialog when all three toggles are off,
            // but the dialog must not be the thing that enforces that.
            OnSta(() =>
            {
                var window = ViewpointResetDialog.Build(Rows(), "");
                Assert.False(ViewpointResetDialog.ResetEnabledOf(window));
            });
        }

        [Fact]
        public void AnEmptyDocument_BuildsAnEmptyPicker_RatherThanThrowing()
        {
            OnSta(() =>
            {
                var window = ViewpointResetDialog.Build(new List<ViewpointRow>(), Summary);
                Assert.False(ViewpointResetDialog.ResetEnabledOf(window));
                Assert.Equal(0, ViewpointResetDialog.SelectedCountOf(window));
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
