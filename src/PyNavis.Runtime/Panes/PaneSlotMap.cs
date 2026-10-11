using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Panes
{
    /// <summary>Which dockpane bundle owns which loader slot.</summary>
    public class PaneAssignment
    {
        public string BundleKey { get; set; }
        /// <summary>
        /// 1-based slot, or 0 when the bundle has no slot this session: every slot was
        /// already taken, or its claim is held (see <see cref="HeldSlot"/>).
        /// </summary>
        public int Slot { get; set; }

        /// <summary>
        /// The bundle's existing claim on a slot that config still provides but that did
        /// not register this session (the satellite failed to load), with
        /// <see cref="Slot"/> 0 beside it; otherwise 0. The claim is kept as is, never
        /// rehomed, so the next boot that loads the satellite gives the bundle its slot
        /// (and the dock layout Navisworks keyed by that slot's plugin id) back.
        /// </summary>
        public int HeldSlot { get; set; }
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
            return Resolve(bundleKeys, existing, slotCount, slotCount);
        }

        /// <param name="slotCount">Slots that actually registered this session.</param>
        /// <param name="configuredSlotCount">
        /// Slots config provides. Above <paramref name="slotCount"/> only when the
        /// satellite failed to load; a claim between the two is held, not reassigned.
        /// </param>
        public static List<PaneAssignment> Resolve(
            IReadOnlyList<string> bundleKeys,
            IReadOnlyDictionary<string, int> existing,
            int slotCount, int configuredSlotCount)
        {
            var result = new List<PaneAssignment>();
            var taken = new HashSet<int>();
            var newcomers = new List<PaneAssignment>();

            foreach (var key in bundleKeys ?? new List<string>())
            {
                var assignment = new PaneAssignment { BundleKey = key, Slot = 0 };
                result.Add(assignment);

                int claimed;
                if (existing == null || !existing.TryGetValue(key, out claimed)) claimed = 0;

                if (claimed >= 1 && claimed <= slotCount && taken.Add(claimed))
                    assignment.Slot = claimed;
                // Configured but not registered: the slot is coming back once the
                // satellite loads, so moving the bundle now would cost it the claim.
                else if (claimed > slotCount && claimed <= configuredSlotCount && taken.Add(claimed))
                    assignment.HeldSlot = claimed;
                // A claim past every configured slot (slots removed since it was
                // written) is stale: treat the bundle as new rather than pointing it at
                // a slot type that no longer exists.
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
