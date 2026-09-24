using PyNavis.Runtime.Events;
using PyNavis.Runtime.Execution;
using Xunit;

namespace PyNavis.Tests
{
    public class PendingEventsTests
    {
        [Fact]
        public void Take_Returns_What_Was_Queued_And_Empties()
        {
            var pending = new PendingEvents();
            pending.Add(NavisEvent.AppInit);
            pending.Add(NavisEvent.DocOpened);

            Assert.Equal(new[] { NavisEvent.AppInit, NavisEvent.DocOpened }, pending.Take());
            Assert.Empty(pending.Take());
        }

        [Fact]
        public void The_Same_Event_Queued_Twice_Comes_Back_Once()
        {
            var pending = new PendingEvents();
            pending.Add(NavisEvent.AppInit);
            pending.Add(NavisEvent.AppInit);

            Assert.Equal(new[] { NavisEvent.AppInit }, pending.Take());
        }
    }

    /// <summary>
    /// Reload raises app-init from INSIDE the Reload command, so a plain Raise is
    /// dropped by HookRunner's while-a-command-runs skip and app-init hooks never
    /// fire on Reload. RaiseWhenIdle holds it until the gate frees.
    /// </summary>
    public class RaiseWhenIdleTests
    {
        [Fact]
        public void With_Nothing_Running_It_Raises_Immediately()
        {
            RunGate.Exit();                       // tests share the statics
            var seen = 0;
            void Handler(NavisEventArgs a) { if (a.Event == NavisEvent.AppInit) seen++; }

            NavisEvents.Raised += Handler;
            try
            {
                NavisEvents.RaiseWhenIdle(NavisEvent.AppInit);
                Assert.Equal(1, seen);
            }
            finally { NavisEvents.Raised -= Handler; }
        }

        [Fact]
        public void While_A_Command_Runs_It_Waits_For_The_Flush()
        {
            RunGate.Exit();
            var seen = 0;
            void Handler(NavisEventArgs a) { if (a.Event == NavisEvent.AppInit) seen++; }

            NavisEvents.Raised += Handler;
            try
            {
                Assert.True(RunGate.TryEnter("Reload"));
                NavisEvents.RaiseWhenIdle(NavisEvent.AppInit);
                Assert.Equal(0, seen);            // held, not dropped

                RunGate.Exit();
                NavisEvents.FlushPending();
                Assert.Equal(1, seen);

                NavisEvents.FlushPending();       // nothing left to replay
                Assert.Equal(1, seen);
            }
            finally
            {
                NavisEvents.Raised -= Handler;
                RunGate.Exit();
            }
        }
    }
}
