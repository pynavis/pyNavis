using System;

namespace PyNavis.Runtime.Events
{
    /// <summary>
    /// Fans one event out to its consumers, in the order they subscribed, then to the
    /// consumers of Handled; one failing consumer never starves the rest. NavisEvents
    /// holds the session's one hub; tests make their own, so they never raise
    /// app-closing at the whole runtime.
    /// </summary>
    internal sealed class EventHub
    {
        public event Action<NavisEventArgs> Raised;

        /// <summary>
        /// Raised once every consumer of Raised has handled the event, whenever each of
        /// them subscribed: for a consumer that must come after them all. Telemetry stops
        /// here on app-closing, because the hook runner runs that event's hooks inside
        /// Raised and each one queues a record; subscribing to Raised, telemetry stopped
        /// first (Boot starts it before the hook runner subscribes) and lost them.
        /// </summary>
        public event Action<NavisEventArgs> Handled;

        public void Raise(NavisEventArgs args)
        {
            Dispatch(Raised, args);
            Dispatch(Handled, args);
        }

        private static void Dispatch(Action<NavisEventArgs> handlers, NavisEventArgs args)
        {
            if (handlers == null) return;
            foreach (Action<NavisEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(args); }
                catch (Exception ex) { Log.Error($"Event consumer failed on '{args.Name}'", ex); }
            }
        }
    }
}
