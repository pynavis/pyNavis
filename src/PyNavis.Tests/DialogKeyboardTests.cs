using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The dialog keyboard contract: Esc always closes, Enter commits the safe
    /// choice, and no dialog is left ownerless (an ownerless ShowDialog is not
    /// modal to Navisworks and sinks behind it).
    /// </summary>
    public class DialogKeyboardTests
    {
        private static Button[] ButtonsOf(Window window) =>
            Descendants(window.Content as DependencyObject).OfType<Button>().ToArray();

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(
            DependencyObject root)
        {
            if (root == null) yield break;
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            if (count == 0 && root is ContentControl holder && holder.Content is DependencyObject inner)
            {
                yield return inner;
                foreach (var deep in Descendants(inner)) yield return deep;
                yield break;
            }
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var deep in Descendants(child)) yield return deep;
            }
        }

        [Fact]
        public void Alert_CanBeDismissedWithEscape()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert("Something happened", "pyNavis");
                var buttons = Dialogs.ButtonsForTest(window);

                // one button, and Esc must reach it
                Assert.Single(buttons);
                Assert.True(buttons[0].IsCancel, "Alert's only button must accept Escape");
                Assert.True(buttons[0].IsDefault, "and Enter, since it is the only way out");
            });
        }

        [Fact]
        public void Alert_WithCopyText_AddsACopyButton_ThatIsNeitherAnswerNorWayOut()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert("Moved Pipe by 152.4 mm", "Clear Clash", "152.4 mm");
                var copy = Dialogs.CopyButtonOf(window);

                Assert.NotNull(copy);
                Assert.Equal("152.4 mm", Dialogs.CopyTextOf(window));
                Assert.Equal("Copy", copy.Content);
                // Copy is an aside: OK keeps Enter and Escape, and stays the only answer.
                Assert.False(copy.IsDefault);
                Assert.False(copy.IsCancel);
                var buttons = Dialogs.ButtonsForTest(window);
                Assert.Single(buttons);
                Assert.True(buttons[0].IsDefault && buttons[0].IsCancel);
            });
        }

        [Fact]
        public void Alert_WithoutCopyText_HasNoCopyButton()
        {
            OnSta(() =>
            {
                Assert.Null(Dialogs.CopyButtonOf(Dialogs.BuildAlert("m", "t")));
                Assert.Null(Dialogs.CopyButtonOf(Dialogs.BuildAlert("m", "t", "")));
            });
        }

        [Fact]
        public void Confirm_DefaultsToTheSafeChoice_SoEnterCannotDestroy()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildConfirm("Delete 4,200 viewpoints?", "pyNavis");
                var buttons = Dialogs.ButtonsForTest(window);

                var yes = buttons.First();
                var no = buttons.Last();
                Assert.False(yes.IsDefault, "Enter must not confirm a destructive action");
                Assert.True(no.IsDefault, "the safe choice is the default");
                Assert.True(no.IsCancel, "and Escape picks it too");
            });
        }

        [Fact]
        public void AskString_CommitsOnEnter_BecauseTypingIsTheAction()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAskString("Name?", "Level 01", "pyNavis");
                var buttons = Dialogs.ButtonsForTest(window);

                Assert.True(buttons.First().IsDefault);
                Assert.True(buttons.Last().IsCancel);
            });
        }

        [Fact]
        public void OwnByHost_IsSafeWhenThereIsNoHostWindow()
        {
            OnSta(() =>
            {
                var window = new Window();
                // The test host has no Navisworks main window; this must be a
                // no-op rather than throwing during dialog construction.
                FluentChrome.OwnByHost(window);
                Assert.Null(window.Owner);
            });
        }

        [Fact]
        public void ChordRecorder_LetsTabThrough_SoItIsNotAKeyboardTrap()
        {
            // HandlesKey takes the RESOLVED key, so Alt+Tab (System -> Tab) also
            // passes through while Alt+M (System -> M) stays recordable.
            Assert.False(ChordRecorder.HandlesKey(Key.Tab));
            Assert.True(ChordRecorder.HandlesKey(Key.M));
            Assert.True(ChordRecorder.HandlesKey(Key.Escape));
            Assert.True(ChordRecorder.HandlesKey(Key.Back));
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
