using System.Collections.Generic;

namespace PyNavis.Runtime.Events
{
    /// <summary>
    /// Events held back while a command holds the run gate, replayed once it frees.
    /// Same event twice in one run replays once. Pure so it is unit-testable.
    /// </summary>
    public class PendingEvents
    {
        private readonly List<NavisEvent> _queue = new List<NavisEvent>();

        public void Add(NavisEvent evt)
        {
            if (!_queue.Contains(evt)) _queue.Add(evt);
        }

        /// <summary>The queued events in the order they arrived; leaves the queue empty.</summary>
        public IList<NavisEvent> Take()
        {
            var taken = new List<NavisEvent>(_queue);
            _queue.Clear();
            return taken;
        }
    }
}
