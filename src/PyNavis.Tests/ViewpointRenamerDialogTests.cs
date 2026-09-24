using System;
using System.Collections.Generic;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Renamer seams: preview plumbing through the python-standing delegate and
    /// the Apply rules (empty plan, collisions, and a failing preview all block).
    /// </summary>
    public class ViewpointRenamerDialogTests
    {
        private static List<ViewpointRow> Rows() => new List<ViewpointRow>
        {
            new ViewpointRow { Guid = "f", Key = "0", ParentKey = "", Folder = "", Name = "Site", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "a", Key = "0/0", ParentKey = "0", Folder = "Site", Name = "North", Kind = "viewpoint", Depth = 1 },
            new ViewpointRow { Guid = "b", Key = "0/1", ParentKey = "0", Folder = "Site", Name = "South", Kind = "viewpoint", Depth = 1 },
        };

        private static ViewpointRenamerDialog.RenamePreview Preview(
            params (string old, string next, bool collision)[] items)
        {
            var preview = new ViewpointRenamerDialog.RenamePreview();
            foreach (var (old, next, collision) in items)
                preview.Items.Add(new ViewpointRenamerDialog.RenameItem
                { Guid = old, OldName = old, NewName = next, Collision = collision });
            return preview;
        }

        [Fact]
        public void Builds_WithPreviewRows_AndApplyEnabled()
        {
            OnSta(() =>
            {
                var window = ViewpointRenamerDialog.Build(Rows(),
                    _ => Preview(("North", "N1", false), ("South", "S1", false)));

                Assert.Equal(2, ViewpointRenamerDialog.PreviewRowCountOf(window));
                Assert.True(ViewpointRenamerDialog.ApplyEnabledOf(window));
            });
        }

        [Fact]
        public void EmptyPlan_DisablesApply_WithGuidance()
        {
            OnSta(() =>
            {
                var window = ViewpointRenamerDialog.Build(Rows(), _ => Preview());

                Assert.False(ViewpointRenamerDialog.ApplyEnabledOf(window));
                Assert.Contains("Nothing changes", ViewpointRenamerDialog.MessageOf(window));
            });
        }

        [Fact]
        public void Collision_DisablesApply()
        {
            OnSta(() =>
            {
                var window = ViewpointRenamerDialog.Build(Rows(),
                    _ => Preview(("North", "Same", false), ("South", "Same", true)));

                Assert.False(ViewpointRenamerDialog.ApplyEnabledOf(window));
                Assert.Contains("collide", ViewpointRenamerDialog.MessageOf(window));
            });
        }

        [Fact]
        public void ProblemFromTheEngine_IsShown_AndBlocks()
        {
            OnSta(() =>
            {
                var window = ViewpointRenamerDialog.Build(Rows(), _ =>
                {
                    var preview = new ViewpointRenamerDialog.RenamePreview();
                    preview.Problems.Add("invalid pattern: unbalanced parenthesis");
                    return preview;
                });

                Assert.False(ViewpointRenamerDialog.ApplyEnabledOf(window));
                Assert.Contains("invalid pattern", ViewpointRenamerDialog.MessageOf(window));
            });
        }

        [Fact]
        public void ThrowingPreview_DisablesApply_InsteadOfCrashing()
        {
            OnSta(() =>
            {
                var window = ViewpointRenamerDialog.Build(
                    Rows(), _ => throw new InvalidOperationException("engine says no"));

                Assert.False(ViewpointRenamerDialog.ApplyEnabledOf(window));
                Assert.Contains("Preview did not load", ViewpointRenamerDialog.MessageOf(window));
            });
        }

        [Fact]
        public void OpAndInputs_AndSelection_FlowIntoTheRequest()
        {
            OnSta(() =>
            {
                ViewpointRenamerDialog.RenameRequest seen = null;
                var window = ViewpointRenamerDialog.Build(Rows(), request =>
                {
                    seen = request;
                    return Preview(("North", "N1", false));
                });

                ViewpointRenamerDialog.SelectAllForTest(window);
                ViewpointRenamerDialog.SetFindForTest(window, "View", "Cam");

                Assert.Equal("replace", seen.Op.Type);
                Assert.Equal("View", seen.Op.Find);
                Assert.Equal("Cam", seen.Op.Replace);
                // folders are never renamed as part of a leaf selection
                Assert.Equal(new[] { "a", "b" }, seen.CheckedGuids);

                ViewpointRenamerDialog.SetOpTypeForTest(window, "number");
                Assert.Equal("number", ViewpointRenamerDialog.RequestOf(window).Op.Type);
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
