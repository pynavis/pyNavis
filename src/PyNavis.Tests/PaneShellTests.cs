using System;
using System.Drawing;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Forms;
using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    public class PaneShellTests
    {
        /// <summary>What Navisworks calls a pane slot, from the slot plugin's
        /// DisplayName. Production reads it back off the plugin record.</summary>
        private const string Caption = "pyNavis Panel 1";

        [Fact]
        public void Shell_Fills_Its_Parent_Client_Area_When_Parented()
        {
            OnSta(() =>
            {
                using (var parent = new Form { ClientSize = new Size(310, 785) })
                using (var shell = new PaneShell(1))
                {
                    // Navisworks parents the pane control without running a WinForms layout
                    // pass, so Dock alone leaves it at the UserControl default 150x150.
                    parent.Controls.Add(shell);
                    Assert.Equal(new Size(310, 785), shell.Size);
                }
            });
        }

        [Fact]
        public void Shell_Follows_A_Parent_Resize()
        {
            OnSta(() =>
            {
                using (var parent = new Form { ClientSize = new Size(200, 200) })
                using (var shell = new PaneShell(2))
                {
                    parent.Controls.Add(shell);

                    // Suspend the parent's own layout engine before resizing, so a
                    // WinForms Dock-driven relayout cannot be the thing that resizes
                    // the shell. Only PaneShell's own Resize handler, which sets
                    // Bounds directly from ClientRectangle rather than going through
                    // layout, can make this assertion pass. Verified discriminating
                    //: with FillParent's body emptied this test fails
                    // (shell stays 200x200); restored, it passes. See the fix report
                    // for the exact RED/GREEN output.
                    parent.SuspendLayout();
                    parent.ClientSize = new Size(420, 640);
                    Assert.Equal(new Size(420, 640), shell.Size);
                }
            });
        }

        [Fact]
        public void SetContent_Replaces_The_Previous_Content()
        {
            OnSta(() =>
            {
                using (var shell = new PaneShell(3))
                {
                    var first = new TextBlock { Text = "one" };
                    var second = new TextBlock { Text = "two" };
                    shell.SetContent(first);
                    shell.SetContent(second);
                    Assert.Same(second, shell.CurrentContent);
                }
            });
        }

        [Fact]
        public void Retitle_Renames_The_Ancestor_Carrying_The_Slot_Caption()
        {
            OnSta(() =>
            {
                // Mirrors the measured chain: the caption sits on an ancestor several
                // levels up (PaneShell > Panel > ... > DockHost), not on the parent.
                using (var host = new Form { Text = Caption })
                using (var middle = new System.Windows.Forms.Panel())
                using (var shell = new PaneShell(1))
                {
                    host.Controls.Add(middle);
                    middle.Controls.Add(shell);

                    Assert.True(shell.Retitle(Caption, "Pane Demo"));
                    Assert.Equal("Pane Demo", host.Text);
                }
            });
        }

        [Fact]
        public void Retitle_Is_A_No_Op_Once_The_Caption_Has_Been_Replaced()
        {
            OnSta(() =>
            {
                // Navisworks calls OnVisibleChanged on every dock, undock and tab switch,
                // so the second call must not overwrite a title with a later bundle's.
                using (var host = new Form { Text = Caption })
                using (var shell = new PaneShell(2))
                {
                    host.Controls.Add(shell);
                    Assert.True(shell.Retitle(Caption, "First"));
                    Assert.False(shell.Retitle(Caption, "Second"));
                    Assert.Equal("First", host.Text);
                }
            });
        }

        [Fact]
        public void Retitle_Reports_False_While_The_Shell_Is_Still_Parked()
        {
            OnSta(() =>
            {
                // Measured: at content-build time the chain stops short of the docking
                // frame, so there is nothing to write and Fill must not be the caller.
                using (var parked = new Form())
                using (var shell = new PaneShell(3))
                {
                    parked.Controls.Add(shell);
                    Assert.False(shell.Retitle(Caption, "Pane Demo"));
                }
            });
        }

        [Fact]
        public void Retitle_Leaves_The_Caption_Alone_When_The_Bundle_Has_No_Title()
        {
            OnSta(() =>
            {
                using (var host = new Form { Text = Caption })
                using (var shell = new PaneShell(4))
                {
                    host.Controls.Add(shell);
                    Assert.False(shell.Retitle(Caption, null));
                    Assert.False(shell.Retitle(Caption, ""));
                    Assert.Equal(Caption, host.Text);
                }
            });
        }

        [Fact]
        public void RestoreCaption_Gives_The_Host_Caption_Back_When_A_Slot_Loses_Its_Bundle()
        {
            OnSta(() =>
            {
                // A Reload that removes the bundle must not leave the empty slot still
                // reading as the bundle that went away.
                using (var host = new Form { Text = Caption })
                using (var shell = new PaneShell(1))
                {
                    host.Controls.Add(shell);
                    shell.Retitle(Caption, "Pane Demo");

                    Assert.True(shell.RestoreCaption());
                    Assert.Equal(Caption, host.Text);
                }
            });
        }

        [Fact]
        public void RestoreCaption_Does_Nothing_When_The_Pane_Was_Never_Retitled()
        {
            OnSta(() =>
            {
                // Fill calls this for every unclaimed slot, including ones that were never
                // claimed at all, so it must not touch a caption it did not write.
                using (var host = new Form { Text = Caption })
                using (var shell = new PaneShell(2))
                {
                    host.Controls.Add(shell);

                    Assert.False(shell.RestoreCaption());
                    Assert.Equal(Caption, host.Text);
                }
            });
        }

        [Fact]
        public void RestoreCaption_Only_Reverts_Once()
        {
            OnSta(() =>
            {
                // Fill runs on every Reload, so a second pass must not re-revert a caption
                // a newly-claimed bundle has since written.
                using (var host = new Form { Text = Caption })
                using (var shell = new PaneShell(3))
                {
                    host.Controls.Add(shell);
                    shell.Retitle(Caption, "Pane Demo");
                    Assert.True(shell.RestoreCaption());

                    host.Text = "Someone Else";
                    Assert.False(shell.RestoreCaption());
                    Assert.Equal("Someone Else", host.Text);
                }
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
