using System;
using PyNavis.Runtime.Execution;
using PyNavis.Runtime.Output;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pump throttle and the single-run gate.
    ///
    /// Scripts run on the Navisworks UI thread, so a long one has to pump the
    /// message loop or the app paints a white "Not Responding" frame - which is
    /// how a real 4.6-day clash-grouping bug came in as "Navisworks hangs".
    /// Pumping dispatches input, hence the gate.
    ///
    /// The throttle predicate is pinned here because it has a sharp edge: a zero
    /// sentinel silently disables pumping forever once uptime passes ~24.9 days.
    /// The same trap was already found and fixed once in the grouper's preview
    /// (ClashGrouperDialog.Preview.cs), so it is worth a test in its own right.
    /// </summary>
    public class UiPumpTests
    {
        [Fact]
        public void Due_IsFalseBeforeTheInterval_AndTrueAtOrAfterIt()
        {
            Assert.False(UiPump.Due(1000, 1000));
            Assert.False(UiPump.Due(1000 + UiPump.IntervalMs - 1, 1000));
            Assert.True(UiPump.Due(1000 + UiPump.IntervalMs, 1000));
            Assert.True(UiPump.Due(1000 + UiPump.IntervalMs + 1, 1000));
        }

        [Fact]
        public void Due_SurvivesTickCountWrapAround()
        {
            // Environment.TickCount wraps MaxValue -> MinValue (~49.7 days). C#
            // subtraction is unchecked, so the difference of two real samples
            // stays right across the boundary.
            var before = int.MaxValue - 10;
            var after = unchecked(before + UiPump.IntervalMs);
            Assert.True(after < 0, "test should straddle the wrap");
            Assert.True(UiPump.Due(after, before));
            Assert.False(UiPump.Due(unchecked(before + 1), before));
        }

        [Fact]
        public void Seed_LeavesTheFirstCallDue()
        {
            // So the first progress report pumps immediately, which is what puts
            // the window on screen instead of at the end of the run.
            Assert.True(UiPump.Due(Environment.TickCount, UiPump.Seed()));
        }

        [Fact]
        public void Seed_IsNeverZero_TheSentinelThatDisablesPumping()
        {
            // With uptime past ~24.9 days `now` is negative, so `now - 0` is
            // hugely negative and Due would answer false for the rest of the
            // session. Proven directly rather than trusted.
            Assert.False(UiPump.Due(int.MinValue + 5, 0));
            Assert.NotEqual(0, UiPump.Seed());
        }

        [Fact]
        public void Pump_WithNoDispatcher_DoesNothingAndDoesNotThrow()
        {
            // Output can never be allowed to fail the script that produced it.
            UiPump.Pump(null);
        }

        [Fact]
        public void Gate_AdmitsOne_RefusesTheSecond_AndReopensOnExit()
        {
            RunGate.Exit(); // tests share the static; start from a known state
            try
            {
                Assert.True(RunGate.TryEnter("First"));
                Assert.True(RunGate.IsHeld);
                Assert.False(RunGate.TryEnter("Second"));

                // The refusal message names what is already running, not what
                // was just refused.
                Assert.Equal("First", RunGate.CurrentTitle);

                RunGate.Exit();
                Assert.False(RunGate.IsHeld);
                Assert.Null(RunGate.CurrentTitle);
                Assert.True(RunGate.TryEnter("Third"));
            }
            finally
            {
                RunGate.Exit();
            }
        }

        [Fact]
        public void Gate_ExitWithoutEnter_IsHarmless()
        {
            RunGate.Exit();
            RunGate.Exit();
            Assert.False(RunGate.IsHeld);
        }
    }
}
