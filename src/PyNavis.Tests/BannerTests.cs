using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>Banner geometry and construction. Positioning is pure, so it needs no window.</summary>
    public class BannerTests
    {
        private static readonly Rect Host = new Rect(100, 50, 1000, 800); // x, y, w, h

        [Fact]
        public void RectFor_SpansTheHostWidth_AndSitsOnItsBottomEdge()
        {
            var rect = Banner.RectFor(Host, 72);

            Assert.Equal(100, rect.X);
            Assert.Equal(1000, rect.Width);
            Assert.Equal(50 + 800 - 72, rect.Y);
            Assert.Equal(72, rect.Height);
            Assert.Equal(Host.Bottom, rect.Bottom);
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

        [Fact]
        public void Build_CarriesMessageAndDetail_AndNeverActivates()
        {
            OnSta(() =>
            {
                var window = Banner.Build(ToastLevel.Success, "True distance: 4ft 0in", "square to both faces");

                Assert.Equal("True distance: 4ft 0in", Banner.MessageTextOf(window));
                Assert.Equal("square to both faces", Banner.DetailTextOf(window));
                Assert.False(window.ShowActivated);
                Assert.True(window.Topmost);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
            });
        }

        [Fact]
        public void Build_WithoutDetail_HidesTheDetailLine()
        {
            OnSta(() =>
            {
                var window = Banner.Build(ToastLevel.Error, "No face found", null);
                Assert.Equal(Visibility.Collapsed, Banner.DetailBlockOf(window).Visibility);
            });
        }

        [Theory]
        [InlineData(ToastLevel.Success)]
        [InlineData(ToastLevel.Error)]
        [InlineData(ToastLevel.Info)]
        [InlineData(ToastLevel.Warning)]
        public void Build_FillsTheWholeBar_InTheLevelColour_WithReadableText(ToastLevel level)
        {
            OnSta(() =>
            {
                var window = Banner.Build(level, "m", "d");
                var fill = Toast.ColorFor(DesignSystem.Tokens.Current, level);

                Assert.Equal(fill, ((SolidColorBrush)Banner.BarOf(window).Background).Color);
                // The text sits on the colour itself, not on paper, so it follows the fill's luminance.
                var ink = DesignSystem.OnAccent(fill);
                Assert.Equal(ink, ((SolidColorBrush)Banner.MessageBlockOf(window).Foreground).Color);
                Assert.Equal(ink, ((SolidColorBrush)Banner.DetailBlockOf(window).Foreground).Color);
            });
        }

        [Fact]
        public void Build_UsesLargerTypeThanAToast_SoItReadsFromAcrossTheRoom()
        {
            OnSta(() =>
            {
                var banner = Banner.Build(ToastLevel.Success, "m", null);
                var toast = Toast.Build(ToastLevel.Success, "m", null);
                Assert.True(Banner.MessageBlockOf(banner).FontSize > Toast.MessageBlockOf(toast).FontSize);
            });
        }

        [Fact]
        public void A_Banner_Lingers_Longer_Than_A_Toast_Of_The_Same_Level()
        {
            foreach (ToastLevel level in Enum.GetValues(typeof(ToastLevel)))
                Assert.True(Banner.DurationFor(level) > Toast.DurationFor(level), level.ToString());
        }

        [Fact]
        public void StayFor_ZeroMeansUntilDismissed_NegativeOrNaNMeansTheLevelsOwn()
        {
            Assert.Equal(TimeSpan.Zero, Banner.StayFor(ToastLevel.Info, 0));
            Assert.Equal(TimeSpan.FromSeconds(3), Banner.StayFor(ToastLevel.Info, 3));
            Assert.Equal(Banner.DurationFor(ToastLevel.Error), Banner.StayFor(ToastLevel.Error, double.NaN));
            Assert.Equal(Banner.DurationFor(ToastLevel.Warning), Banner.StayFor(ToastLevel.Warning, -1));
        }

        [Fact]
        public void CloseAll_WithNoBanner_IsSafe()
        {
            Banner.CloseAll(); // reload path must never throw, banner or not
        }
    }
}
