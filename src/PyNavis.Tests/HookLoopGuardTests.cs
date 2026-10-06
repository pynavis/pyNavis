using PyNavis.Runtime.Events;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// A hook that causes its own event must not run itself again: nested synchronously
    /// (viewpoints-changed renaming a viewpoint) it would recurse until the stack overflows
    /// and takes Navisworks down; through a debounce (camera-moved setting the view) it
    /// would loop four times a second for ever.
    /// </summary>
    public class HookLoopGuardTests
    {
        [Fact]
        public void AnEventRaisedWhileAHookRuns_DoesNotStartAnother()
        {
            var guard = new HookLoopGuard();

            Assert.True(guard.TryEnter());
            Assert.False(guard.TryEnter());
            guard.Exit();
            Assert.True(guard.TryEnter());
        }

        [Fact]
        public void ADebounceArmedInsideAHook_FiresAsHookCaused()
        {
            var guard = new HookLoopGuard();
            guard.TryEnter();
            guard.Armed(NavisEvent.CameraMoved);
            guard.Exit();

            Assert.True(guard.FiredFromHook(NavisEvent.CameraMoved));
        }

        [Fact]
        public void ADebounceArmedByTheUser_FiresNormally()
        {
            var guard = new HookLoopGuard();
            guard.Armed(NavisEvent.CameraMoved);

            Assert.False(guard.FiredFromHook(NavisEvent.CameraMoved));
        }

        [Fact]
        public void TheUserMovingAgainAfterTheHook_WinsOverTheHooksArming()
        {
            var guard = new HookLoopGuard();
            guard.TryEnter();
            guard.Armed(NavisEvent.SelectionChanged);
            guard.Exit();
            guard.Armed(NavisEvent.SelectionChanged);

            Assert.False(guard.FiredFromHook(NavisEvent.SelectionChanged));
        }

        [Fact]
        public void HookCaused_IsReportedOncePerFiring_AndPerEvent()
        {
            var guard = new HookLoopGuard();
            guard.TryEnter();
            guard.Armed(NavisEvent.CameraMoved);
            guard.Exit();

            Assert.False(guard.FiredFromHook(NavisEvent.SelectionChanged));
            Assert.True(guard.FiredFromHook(NavisEvent.CameraMoved));
            Assert.False(guard.FiredFromHook(NavisEvent.CameraMoved));
        }
    }
}
