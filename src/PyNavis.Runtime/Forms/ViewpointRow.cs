using System;
using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// One saved-viewpoint row, built from a pynavis.viewpoints snapshot.
    /// Identity is the GUID: index paths describe tree shape only and go stale
    /// the moment anything moves, so edits address rows by Guid.
    /// </summary>
    public sealed class ViewpointRow
    {
        public string Guid { get; set; }
        public string Key { get; set; }         // index path, tree shape only
        public string ParentKey { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }      // full folder path, "" at root
        public string Kind { get; set; }        // folder|viewpoint|animation
        public int Depth { get; set; }
        public bool IsFolder { get; set; }
        public int Comments { get; set; }
        public bool Duplicate { get; set; }     // set by MarkDuplicates
    }

    /// <summary>
    /// What the browser shows: duplicate marking, the scope/chip/query filter,
    /// empty-folder detection, and the contents readout. Pure so the rules are
    /// testable without a window.
    /// </summary>
    public static class ViewpointFilter
    {
        /// <summary>Flags every non-folder sharing a trimmed, case-insensitive
        /// name with another. Folders are exempt: two folders of the same name
        /// in different parents are normal.</summary>
        public static void MarkDuplicates(IList<ViewpointRow> rows)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.IsFolder || string.IsNullOrWhiteSpace(row.Name)) continue;
                var key = row.Name.Trim();
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            foreach (var row in rows)
                row.Duplicate = !row.IsFolder
                    && !string.IsNullOrWhiteSpace(row.Name)
                    && counts[row.Name.Trim()] > 1;
        }

        /// <summary>Indexes of the rows to list, in tree order. Folders live in
        /// the rail, so the list itself only ever holds leaves.</summary>
        public static List<int> Apply(
            IList<ViewpointRow> rows, string query, string scope, string chip)
        {
            var text = (query ?? "").Trim();
            var visible = new List<int>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.IsFolder) continue;
                if (!InScope(row, scope)) continue;
                if (chip == "animations" && row.Kind != "animation") continue;
                if (chip == "duplicates" && !row.Duplicate) continue;
                if (text.Length > 0
                    && (row.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0
                    && (row.Folder ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                visible.Add(i);
            }
            return visible;
        }

        /// <summary>A folder scope covers the folder and everything below it.
        /// The separator matters: "Level 1" must not swallow "Level 10".</summary>
        private static bool InScope(ViewpointRow row, string scope) =>
            string.IsNullOrEmpty(scope)
            || row.Folder == scope
            || (row.Folder ?? "").StartsWith(scope + "/", StringComparison.Ordinal);

        /// <summary>Folders holding no viewpoint or animation anywhere below,
        /// so a folder of empty folders counts as empty too.</summary>
        public static List<string> EmptyFolderKeys(IList<ViewpointRow> rows) =>
            rows.Where(f => f.IsFolder && !rows.Any(other =>
                    !other.IsFolder
                    && other.Key.StartsWith(f.Key + "/", StringComparison.Ordinal)))
                .Select(f => f.Key)
                .ToList();

        public static string StatsText(IList<ViewpointRow> rows)
        {
            var folders = rows.Count(r => r.IsFolder);
            var views = rows.Count(r => r.Kind == "viewpoint");
            var anims = rows.Count(r => r.Kind == "animation");
            return string.Format("{0} {1}, {2} {3}, {4} {5}",
                folders, folders == 1 ? "folder" : "folders",
                views, views == 1 ? "viewpoint" : "viewpoints",
                anims, anims == 1 ? "animation" : "animations");
        }
    }
}
