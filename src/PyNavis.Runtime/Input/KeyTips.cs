using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Input
{
    /// <summary>
    /// Deterministic Alt-keytip assignment for one ribbon tab: explicit overrides claim
    /// first, then titles get their first letter, first two letters, or letter+digit.
    /// Pure so the dedupe rules are unit-tested.
    /// </summary>
    public static class KeyTips
    {
        public static Dictionary<string, string> Assign(
            IReadOnlyList<(string id, string title, string overrideTip)> items)
        {
            var result = new Dictionary<string, string>();
            var taken = new HashSet<string>();

            foreach (var (id, _, tip) in items.Where(i => !string.IsNullOrWhiteSpace(i.overrideTip)))
            {
                var upper = tip.Trim().ToUpperInvariant();
                if (taken.Add(upper)) result[id] = upper;
            }

            foreach (var (id, title, tip) in items)
            {
                if (!string.IsNullOrWhiteSpace(tip) || result.ContainsKey(id)) continue;
                var letters = new string((title ?? "").Where(char.IsLetter).ToArray()).ToUpperInvariant();
                if (letters.Length == 0) continue;

                var candidates = new List<string> { letters.Substring(0, 1) };
                if (letters.Length > 1) candidates.Add(letters.Substring(0, 2));
                for (var d = 2; d <= 9; d++) candidates.Add(letters.Substring(0, 1) + d);

                var chosen = candidates.FirstOrDefault(c => !taken.Contains(c));
                if (chosen == null) continue;
                taken.Add(chosen);
                result[id] = chosen;
            }
            return result;
        }
    }
}
