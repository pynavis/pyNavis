using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Panes
{
    /// <summary>Which dockpane bundle owns which loader slot.</summary>
    public class PaneAssignment
    {
        public string BundleKey { get; set; }
        /// <summary>1-based slot, or 0 when every slot was already taken.</summary>
        public int Slot { get; set; }
    }

    /// <summary>
    /// Pure slot bookkeeping. Navisworks remembers a dock pane's position, size and
    /// open state per plugin id, so a bundle must keep the same slot for life: claims
    /// already in config always win, and only genuinely new bundles take free slots.
    /// </summary>
    /// <remarks>
    /// The caller guarantees <c>bundleKeys</c> has no duplicates. A colliding bundle key
    /// means two dockpane bundles were resolved to the same identity, which this method
    /// cannot detect on its own - it would silently treat the second occurrence as the
    /// same claim as the first, crossing slot ownership between two unrelated panes.
    /// </remarks>
    public static class PaneSlotMap
    {
        public static List<PaneAssignment> Resolve(
            IReadOnlyList<string> bundleKeys,
            IReadOnlyDictionary<string, int> existing,
            int slotCount)
        {
            var result = new List<PaneAssignment>();
            var taken = new HashSet<int>();
            var newcomers = new List<PaneAssignment>();

            foreach (var key in bundleKeys ?? new List<string>())
            {
                var assignment = new PaneAssignment { BundleKey = key, Slot = 0 };
                result.Add(assignment);

                int slot;
                // A claim past the end (slots removed since it was written) is stale:
                // treat the bundle as new rather than pointing it at a slot type that
                // no longer exists.
                if (existing != null && existing.TryGetValue(key, out slot)
                    && slot >= 1 && slot <= slotCount && taken.Add(slot))
                    assignment.Slot = slot;
                else
                    newcomers.Add(assignment);
            }

            var next = 1;
            foreach (var newcomer in newcomers)
            {
                while (next <= slotCount && taken.Contains(next)) next++;
                if (next > slotCount) break;      // the rest overflow, Slot stays 0
                newcomer.Slot = next;
                taken.Add(next);
            }
            return result;
        }
    }
}
