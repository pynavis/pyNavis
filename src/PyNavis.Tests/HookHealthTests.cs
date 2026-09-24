using PyNavis.Runtime.Events;
using Xunit;

namespace PyNavis.Tests
{
    public class HookHealthTests
    {
        [Fact]
        public void Three_Consecutive_Failures_Disable_Once()
        {
            var health = new HookHealth();
            Assert.False(health.RecordFailure("a"));
            Assert.False(health.RecordFailure("a"));
            Assert.True(health.RecordFailure("a"));   // the crossing failure reports true
            Assert.True(health.IsDisabled("a"));
            Assert.False(health.RecordFailure("a"));  // already disabled: no re-toast
        }

        [Fact]
        public void Success_Resets_The_Streak()
        {
            var health = new HookHealth();
            health.RecordFailure("a");
            health.RecordFailure("a");
            health.RecordSuccess("a");
            Assert.False(health.RecordFailure("a"));
            Assert.False(health.IsDisabled("a"));
        }

        [Fact]
        public void Hooks_Are_Tracked_Independently()
        {
            var health = new HookHealth();
            health.RecordFailure("a");
            Assert.False(health.IsDisabled("b"));
        }

        [Fact]
        public void Reset_Reenables_Everything()
        {
            var health = new HookHealth();
            health.RecordFailure("a"); health.RecordFailure("a"); health.RecordFailure("a");
            health.Reset();
            Assert.False(health.IsDisabled("a"));
        }
    }
}
