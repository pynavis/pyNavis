using PyNavis.Runtime.Events;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>Flips gated buttons' IsEnabled on every hub event, from one snapshot.</summary>
    public static class ContextGate
    {
        private static bool _subscribed;

        public static void Wire()
        {
            if (_subscribed) return;
            _subscribed = true;
            NavisEvents.Raised += args => Refresh();
        }

        public static void Refresh()
        {
            var snapshot = ContextConditions.Snapshot();
            foreach (var entry in ButtonRegistry.All)
            {
                if (entry.model.ContextRule == null) continue;
                var enabled = entry.model.ContextRule.Evaluate(snapshot.Contains);
                if (entry.item.IsEnabled != enabled) entry.item.IsEnabled = enabled;
            }
        }
    }
}
