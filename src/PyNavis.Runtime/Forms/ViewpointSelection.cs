using System;
using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The browser's selection, held as a set of GUIDs so it survives renames,
    /// moves, and re-snapshots. Ticking a folder cascades to its descendants;
    /// ranges and invert operate over the currently visible order only.
    /// Changed fires once per gesture, not once per affected row, so a
    /// select-all over 200k rows repaints once.
    /// </summary>
    public sealed class ViewpointSelection
    {
        private IList<ViewpointRow> _rows;
        private Dictionary<string, ViewpointRow> _byGuid;
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);

        public ViewpointSelection(IList<ViewpointRow> rows) => Rebind(rows);

        /// <summary>Points the selection at a fresh snapshot and empties it.
        /// Callers that re-snapshot MUST rebind, or cascades and the summary
        /// would still be reasoning about rows that no longer exist.</summary>
        public void Rebind(IList<ViewpointRow> rows)
        {
            _rows = rows;
            _byGuid = new Dictionary<string, ViewpointRow>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row.Guid != null) _byGuid[row.Guid] = row;
            _selected.Clear();
        }

        public event Action Changed;

        public IReadOnlyCollection<string> Guids => _selected;

        public int Count => _selected.Count;

        public bool IsSelected(string guid) => _selected.Contains(guid);

        /// <summary>Ticks or clears a row; a folder carries its descendants.</summary>
        public void Set(string guid, bool on)
        {
            Apply(guid, on);
            Raise();
        }

        public void SetRange(IList<int> visible, int from, int to, bool on)
        {
            var lo = Math.Max(0, Math.Min(from, to));
            var hi = Math.Min(visible.Count - 1, Math.Max(from, to));
            for (var i = lo; i <= hi; i++)
                Apply(_rows[visible[i]].Guid, on);
            Raise();
        }

        public void SetAll(IList<int> visible, bool on)
        {
            foreach (var index in visible) Apply(_rows[index].Guid, on);
            Raise();
        }

        public void Invert(IList<int> visible)
        {
            foreach (var index in visible)
            {
                var guid = _rows[index].Guid;
                Apply(guid, !_selected.Contains(guid));
            }
            Raise();
        }

        public void Clear()
        {
            _selected.Clear();
            Raise();
        }

        /// <summary>How many selected rows sit under a folder key, for the rail.</summary>
        public int SelectedUnder(string folderKey)
        {
            var prefix = folderKey + "/";
            var n = 0;
            foreach (var guid in _selected)
            {
                if (!_byGuid.TryGetValue(guid, out var row)) continue;
                if (row.Key != folderKey
                    && row.Key.StartsWith(prefix, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>"412 viewpoints, 3 animations, 2 folders", empty at zero.
        /// The breakdown matters because a folder stands for everything in it.</summary>
        public string Summary()
        {
            if (_selected.Count == 0) return "";
            int views = 0, anims = 0, folders = 0;
            foreach (var guid in _selected)
            {
                if (!_byGuid.TryGetValue(guid, out var row)) continue;
                if (row.IsFolder) folders++;
                else if (row.Kind == "animation") anims++;
                else views++;
            }
            var parts = new List<string>();
            if (views > 0) parts.Add(views + (views == 1 ? " viewpoint" : " viewpoints"));
            if (anims > 0) parts.Add(anims + (anims == 1 ? " animation" : " animations"));
            if (folders > 0) parts.Add(folders + (folders == 1 ? " folder" : " folders"));
            return string.Join(", ", parts);
        }

        /// <summary>Selected leaves only, in tree order: what an edit acts on.</summary>
        public List<ViewpointRow> SelectedLeaves() =>
            _rows.Where(r => !r.IsFolder && _selected.Contains(r.Guid)).ToList();

        private void Apply(string guid, bool on)
        {
            if (guid == null) return;
            Toggle(guid, on);
            if (!_byGuid.TryGetValue(guid, out var row) || !row.IsFolder) return;

            var prefix = row.Key + "/";
            foreach (var other in _rows)
                if (other.Key != null
                    && other.Key.StartsWith(prefix, StringComparison.Ordinal))
                    Toggle(other.Guid, on);
        }

        private void Toggle(string guid, bool on)
        {
            if (on) _selected.Add(guid);
            else _selected.Remove(guid);
        }

        private void Raise() => Changed?.Invoke();
    }
}
