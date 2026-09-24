using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Pressed feedback and button sizing. A control that does not move under
    /// the pointer reads as dead, and the system documents pressed as a real
    /// state alongside rest, focus, and disabled.
    /// </summary>
    public class DesignSystemPressedTests
    {
        private static Color BgOf(System.Windows.Controls.Button b) =>
            ((SolidColorBrush)b.Background).Color;

        private static void Press(System.Windows.Controls.Button button, bool down)
        {
            button.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = down
                    ? UIElement.PreviewMouseLeftButtonDownEvent
                    : UIElement.PreviewMouseLeftButtonUpEvent,
            });
        }

        [Fact]
        public void Primary_DarkensWhilePressed_AndRecovers()
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                var button = DesignSystem.Primary(t, "Apply", null);
                var rest = BgOf(button);

                Press(button, true);
                var pressed = BgOf(button);
                Press(button, false);

                Assert.NotEqual(rest, pressed);
                Assert.True(Luma(pressed) < Luma(rest), "pressed must darken the fill");
                Assert.Equal(rest, BgOf(button));
            });
        }

        [Fact]
        public void Secondary_ShowsPressedToo()
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                var button = DesignSystem.Secondary(t, "Cancel", null);
                var rest = BgOf(button);

                Press(button, true);

                Assert.NotEqual(rest, BgOf(button));
            });
        }

        [Fact]
        public void DisabledButton_DoesNotRespondToPress()
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                var button = DesignSystem.Primary(t, "Apply", null);
                button.IsEnabled = false;
                var disabled = BgOf(button);

                Press(button, true);

                Assert.Equal(disabled, BgOf(button));
            });
        }

        [Fact]
        public void Primary_DoesNotForceAWideButton()
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                // "OK" and "Save" were being stretched to 140px by a hardcoded
                // MinWidth; width is the caller's business.
                Assert.Equal(84, DesignSystem.Primary(t, "OK", null).MinWidth);
                Assert.Equal(84, DesignSystem.Secondary(t, "Cancel", null).MinWidth);
            });
        }

        private static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

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
