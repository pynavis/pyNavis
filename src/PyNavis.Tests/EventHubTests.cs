using System;
using System.Collections.Generic;
using PyNavis.Runtime.Events;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The one fan-out every Navisworks event goes through. Consumers of Handled come
    /// after every consumer of Raised, whenever each subscribed: telemetry's final flush
    /// on app-closing must follow the app-closing hooks that record into it.
    /// </summary>
    public class EventHubTests
    {
        [Fact]
        public void Handled_RunsAfterEveryRaisedConsumer_EvenOneThatSubscribedLater()
        {
            var hub = new EventHub();
            var order = new List<string>();
            hub.Raised += e => order.Add("first");
            hub.Handled += e => order.Add("handled");
            hub.Raised += e => order.Add("later");

            hub.Raise(new NavisEventArgs { Event = NavisEvent.AppClosing });

            Assert.Equal(new[] { "first", "later", "handled" }, order);
        }

        [Fact]
        public void AFailingConsumer_NeverStarvesTheRest()
        {
            var hub = new EventHub();
            var order = new List<string>();
            hub.Raised += e => throw new InvalidOperationException("boom");
            hub.Raised += e => order.Add("raised");
            hub.Handled += e => throw new InvalidOperationException("boom");
            hub.Handled += e => order.Add("handled");

            hub.Raise(new NavisEventArgs { Event = NavisEvent.DocSaved });

            Assert.Equal(new[] { "raised", "handled" }, order);
        }
    }
}
