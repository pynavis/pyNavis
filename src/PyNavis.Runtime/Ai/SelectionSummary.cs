using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PyNavis.Runtime.Ai
{
    /// <summary>One property of one selected item, by display name. Value is kept
    /// only so an old caller compiles; the summary never reads it.</summary>
    public sealed class PropertyRow
    {
        public string Category;
        public string Property;
        public string Value;
    }

    /// <summary>
    /// The attach-selection payload: the item count, then each category with the
    /// property names under it. Names only. A value is project data (a source file,
    /// a client, a project number in a custom tab) and this text leaves the machine,
    /// so no value, no file name, no path, no geometry ever goes into it.
    /// </summary>
    public static class SelectionSummary
    {
        public const int MaxChars = 4000;

        public static string Build(int itemCount, IEnumerable<PropertyRow> rows)
        {
            if (itemCount <= 0) return "Nothing is selected.";

            var byCategory = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var categoryOrder = new List<string>();
            foreach (var row in rows ?? Enumerable.Empty<PropertyRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Category) || string.IsNullOrWhiteSpace(row.Property)) continue;
                if (!byCategory.TryGetValue(row.Category, out var props))
                {
                    props = new List<string>();
                    byCategory[row.Category] = props;
                    categoryOrder.Add(row.Category);
                }
                if (!props.Contains(row.Property, StringComparer.Ordinal)) props.Add(row.Property);
            }

            var sb = new StringBuilder();
            sb.Append(itemCount).Append(itemCount == 1 ? " item selected." : " items selected.");
            if (categoryOrder.Count > 0)
                sb.Append(" Categories: ").Append(string.Join(", ", categoryOrder)).Append('.');
            sb.Append(" Property names only, no values.\n");

            foreach (var category in categoryOrder)
            {
                var block = new StringBuilder();
                block.Append('\n').Append(category).Append('\n');
                foreach (var name in byCategory[category]) block.Append("  ").Append(name).Append('\n');
                if (sb.Length + block.Length > MaxChars)
                {
                    sb.Append("\n(truncated)");
                    return sb.ToString();
                }
                sb.Append(block);
            }
            return sb.ToString().TrimEnd() + "\n";
        }
    }
}
