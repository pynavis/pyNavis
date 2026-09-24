using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>Toast geometry and construction. Positioning is pure, so it needs no window.</summary>
    public class ToastTests
    {
        private static readonly Rect Host = new Rect(100, 50, 1000, 800); // x, y, w, h

        [Fact]
        public void RectFor_FirstToast_SitsAtBottomRight_InsideTheMargin()
        {
            var rect = Toast.RectFor(Host, 0, new Size(300, 64), 16, 8);

            Assert.Equal(100 + 1000 - 16 - 300, rect.X);
            Assert.Equal(50 + 800 - 16 - 64, rect.Y);
            Assert.Equal(300, rect.Width);
        }

        [Fact]
        public void RectFor_LaterToasts_StackUpward_ByHeightPlusGap()
        {
            // Uniform heights, the easy case: 'below' is the summed height underneath.
            var first = Toast.RectFor(Host, 0, new Size(300, 64), 16, 8);
            var second = Toast.RectFor(Host, 1, new Size(300, 64), 16, 8, below: 64);
            var third = Toast.RectFor(Host, 2, new Size(300, 64), 16, 8, below: 128);

            Assert.Equal(first.X, second.X);
            Assert.Equal(first.Y - 72, second.Y);
            Assert.Equal(first.Y - 144, third.Y);
        }

        [Fact]
        public void RectFor_TallerToast_StillClearsTheBottomMargin()
        {
            var rect = Toast.RectFor(Host, 0, new Size(300, 96), 16, 8);
            Assert.Equal(50 + 800 - 16 - 96, rect.Y);
        }

        [Fact]
        public void A_Short_Toast_Above_A_Tall_One_Does_Not_Overlap_It()
        {
            // Toast heights are not uniform: a message with a detail line is taller than
            // one without. Stacking has to add up the heights actually below a toast,
            // not multiply its own height by its index. Field-measured: a
            // one-line toast above a two-line one sat on top of it.
            var tall = Toast.RectFor(Host, 0, new Size(300, 96), 16, 8);
            var short_ = Toast.RectFor(Host, 1, new Size(300, 48), 16, 8, below: 96);

            Assert.True(short_.Bottom <= tall.Top,
                $"short toast bottom {short_.Bottom} must sit above tall toast top {tall.Top}");
            Assert.Equal(8, tall.Top - short_.Bottom);
        }

        [Fact]
        public void A_Tall_Toast_Above_A_Short_One_Leaves_Exactly_One_Gap()
        {
            // The mirror case: getting this wrong the other way leaves a visible hole
            // rather than an overlap, which is why summing beats multiplying.
            var short_ = Toast.RectFor(Host, 0, new Size(300, 48), 16, 8);
            var tall = Toast.RectFor(Host, 1, new Size(300, 96), 16, 8, below: 48);

            Assert.Equal(8, short_.Top - tall.Bottom);
        }

        [Fact]
        public void Three_Mixed_Height_Toasts_Each_Clear_The_One_Below()
        {
            var first = Toast.RectFor(Host, 0, new Size(300, 48), 16, 8);
            var second = Toast.RectFor(Host, 1, new Size(300, 96), 16, 8, below: 48);
            var third = Toast.RectFor(Host, 2, new Size(300, 64), 16, 8, below: 48 + 96);

            Assert.Equal(8, first.Top - second.Bottom);
            Assert.Equal(8, second.Top - third.Bottom);
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
                var window = Toast.Build(ToastLevel.Success, "Memorized 128 items", "from Tower.nwf");

                Assert.Equal("Memorized 128 items", Toast.MessageTextOf(window));
                Assert.Equal("from Tower.nwf", Toast.DetailTextOf(window));
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
                var window = Toast.Build(ToastLevel.Error, "Memory is empty", null);
                Assert.Equal(Visibility.Collapsed, Toast.DetailBlockOf(window).Visibility);
            });
        }

        [Theory]
        [InlineData(ToastLevel.Success)]
        [InlineData(ToastLevel.Error)]
        [InlineData(ToastLevel.Info)]
        [InlineData(ToastLevel.Warning)]
        public void Build_PaintsBorderAndRail_InTheLevelColour(ToastLevel level)
        {
            OnSta(() =>
            {
                var window = Toast.Build(level, "m", null);
                var expected = Toast.ColorFor(DesignSystem.Tokens.Current, level);

                Assert.Equal(expected, ((SolidColorBrush)Toast.CardOf(window).BorderBrush).Color);
                Assert.Equal(expected, ((SolidColorBrush)Toast.RailOf(window).Background).Color);
            });
        }

        [Fact]
        public void Errors_LingerLongerThanSuccesses()
        {
            Assert.True(Toast.DurationFor(ToastLevel.Error) > Toast.DurationFor(ToastLevel.Success));
            Assert.True(Toast.DurationFor(ToastLevel.Warning) > Toast.DurationFor(ToastLevel.Info));
        }

        [Fact]
        public void CloseAll_WithNoToasts_IsSafe()
        {
            Toast.CloseAll(); // reload path must never throw, toasts or not
        }
    }
}
