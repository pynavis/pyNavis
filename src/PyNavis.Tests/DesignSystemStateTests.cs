using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The states every button must have, and the accent-contrast rule. These
    /// were missing across the whole toolkit: a disabled button still painted
    /// as a live accent button with a hand cursor, and Primary always wrote
    /// white text even on a light Windows accent.
    /// </summary>
    public class DesignSystemStateTests
    {
        [Theory]
        // dark accents keep white text
        [InlineData(0x00, 0x5F, 0xB8, 0xFF, 0xFF, 0xFF)]
        [InlineData(0x1A, 0x1A, 0x1A, 0xFF, 0xFF, 0xFF)]
        // light accents must flip to ink, or the label is unreadable
        [InlineData(0xFF, 0xE0, 0x00, 0x1A, 0x1A, 0x1A)]
        [InlineData(0x9C, 0xF5, 0x4A, 0x1A, 0x1A, 0x1A)]
        public void OnAccent_PicksTheReadableForeground(
            byte r, byte g, byte b, byte er, byte eg, byte eb)
        {
            var chosen = DesignSystem.OnAccent(Color.FromRgb(r, g, b));

            Assert.Equal(Color.FromRgb(er, eg, eb), chosen);
        }

        [Fact]
        public void DisabledPrimary_PaintsTheDisabledState_AndDropsTheHandCursor()
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                var button = DesignSystem.Primary(t, "Rename", null);
                var live = ((SolidColorBrush)button.Background).Color;

                button.IsEnabled = false;

                Assert.NotEqual(live, ((SolidColorBrush)button.Background).Color);
                Assert.Equal(t.Surface, ((SolidColorBrush)button.Background).Color);
                Assert.Equal(t.Muted, ((SolidColorBrush)button.Foreground).Color);
                Assert.NotEqual(System.Windows.Input.Cursors.Hand, button.Cursor);

                button.IsEnabled = true;

                Assert.Equal(live, ((SolidColorBrush)button.Background).Color);
                Assert.Equal(System.Windows.Input.Cursors.Hand, button.Cursor);
            });
        }

        [Theory]
        [InlineData("secondary")]
        [InlineData("quiet")]
        [InlineData("danger")]
        public void EveryWeight_HasADisabledState(string weight)
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                var button = weight == "secondary" ? DesignSystem.Secondary(t, "Cancel", null)
                    : weight == "quiet" ? DesignSystem.Quiet(t, "Sort", null)
                    : DesignSystem.Danger(t, "Delete", null);

                button.IsEnabled = false;

                Assert.Equal(t.Muted, ((SolidColorBrush)button.Foreground).Color);
            });
        }

        [Fact]
        public void HoverOnADisabledButton_DoesNothing()
        {
            OnSta(() =>
            {
                var t = DesignSystem.Tokens.For(false);
                var button = DesignSystem.Secondary(t, "Cancel", null);
                button.IsEnabled = false;

                button.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = FrameworkElement.MouseEnterEvent });

                Assert.Equal(t.Surface, ((SolidColorBrush)button.Background).Color);
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
