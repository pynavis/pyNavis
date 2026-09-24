using System;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The progress window shown during a clash-group write: STA construction
    /// (never shown), and the bar geometry.
    ///
    /// Begin() is not exercised here because it calls EnableWindow on the
    /// Navisworks host, which does not exist in a test run. Build() is the seam,
    /// matching ClashGrouperDialogTests.
    /// </summary>
    public class ProgressScopeTests
    {
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
        public void BarWidthFor_SpansTheTrack_AndClampsBothEnds()
        {
            Assert.Equal(0.0, ProgressScope.BarWidthFor(0.0));
            Assert.Equal(ProgressScope.TrackWidth / 2, ProgressScope.BarWidthFor(0.5));
            Assert.Equal(ProgressScope.TrackWidth, ProgressScope.BarWidthFor(1.0));

            // apply_plan divides by a test count and reports (n + fraction)/total,
            // so out-of-range values are cheap to produce; they must not paint a
            // bar wider than its track or a negative width (which throws in WPF).
            Assert.Equal(0.0, ProgressScope.BarWidthFor(-0.25));
            Assert.Equal(ProgressScope.TrackWidth, ProgressScope.BarWidthFor(1.5));
        }

        [Fact]
        public void BarWidthFor_TreatsNaNAsEmpty_NotAsAWpfException()
        {
            // 0/0 is reachable if a caller ever reports against an empty
            // selection, and NaN assigned to Width throws.
            Assert.Equal(0.0, ProgressScope.BarWidthFor(double.NaN));
        }

        [Fact]
        public void Builds_WithTitleAndLabel_AndAnEmptyBar()
        {
            OnSta(() =>
            {
                var window = ProgressScope.Build("Grouping clashes", "Starting");
                Assert.Equal("Starting", ProgressScope.LabelTextOf(window));
                Assert.Equal(0.0, ProgressScope.BarWidthOf(window));
                Assert.Equal(380.0, window.Width);
                // No titlebar: closing it mid-write would leave the user with a
                // frozen Navisworks and no explanation.
                Assert.Equal(System.Windows.WindowStyle.None, window.WindowStyle);
                Assert.Equal(System.Windows.ResizeMode.NoResize, window.ResizeMode);
            });
        }

        [Fact]
        public void Builds_WithNullText_WithoutThrowing()
        {
            OnSta(() =>
            {
                var window = ProgressScope.Build(null, null);
                Assert.Equal("", ProgressScope.LabelTextOf(window));
            });
        }

        [Fact]
        public void CloseAll_WithNoLiveScopes_IsHarmless()
        {
            // ScriptExecutor calls this in a finally after every run.
            ProgressScope.CloseAll();
            ProgressScope.CloseAll();
        }

        [Fact]
        public void Cancel_Button_Flips_IsCancelled()
        {
            OnSta(() =>
            {
                var window = ProgressScope.Build("T", "working", canCancel: true);
                Assert.False(ProgressScope.CancelledOf(window));
                ProgressScope.CancelForTest(window);
                Assert.True(ProgressScope.CancelledOf(window));
            });
        }

        [Fact]
        public void Cancel_Relabels_ToCancelling()
        {
            OnSta(() =>
            {
                var window = ProgressScope.Build("T", "working", canCancel: true);
                ProgressScope.CancelForTest(window);
                Assert.Equal("Cancelling...", ProgressScope.LabelTextOf(window));
            });
        }

        [Fact]
        public void Build_WithoutCanCancel_HasNoCancelSeamEffect()
        {
            // Default 2-arg Build behaves exactly as before: not cancellable.
            OnSta(() =>
            {
                var window = ProgressScope.Build("T", "working");
                Assert.False(ProgressScope.CancelledOf(window));
            });
        }
    }
}
