using System;
using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Input;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The shortcuts editor's state, kept free of WPF so every rule is unit-testable:
    /// one row per pushbutton in ribbon order, author defaults merged with config
    /// overrides, edits validated through ChordParser, conflicts flagged with the
    /// same first-wins order ShortcutMap uses, and a minimal override map out.
    /// All chord text is canonicalized (parse + Chord.ToString) so spelling
    /// variants of the same chord compare equal.
    /// </summary>
    public sealed class ShortcutsModel
    {
        public sealed class Row
        {
            internal Row() { }
            public string BundleKey { get; internal set; }
            public string PanelTitle { get; internal set; }
            public string Title { get; internal set; }
            /// <summary>Canonical author default, or null when the bundle ships none.</summary>
            public string DefaultChord { get; internal set; }
            /// <summary>Canonical current chord, or null when unbound/disabled.</summary>
            public string Current { get; internal set; }
            /// <summary>True when Current differs from the author default.</summary>
            public bool IsCustom => !string.Equals(Current, DefaultChord, StringComparison.Ordinal);
            /// <summary>Title of the earlier row that owns this chord, or null.</summary>
            public string ConflictWith { get; internal set; }
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<string, Row> _byKey =
            new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _loaded =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly bool _allowBareKeys;

        private ShortcutsModel(bool allowBareKeys) => _allowBareKeys = allowBareKeys;

        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>The config's shortcuts.allowBareKeys flag; recorders validate with it.</summary>
        public bool AllowBareKeys => _allowBareKeys;

        public bool HasConflicts => _rows.Any(r => r.ConflictWith != null);

        /// <summary>True when any row differs from the state loaded from config.</summary>
        public bool IsDirty
        {
            get
            {
                foreach (var row in _rows)
                {
                    var had = _loaded.TryGetValue(row.BundleKey, out var was);
                    if (had != row.IsCustom) return true;
                    if (had && !string.Equals(was, row.Current, StringComparison.Ordinal)) return true;
                }
                return false;
            }
        }

        public static ShortcutsModel Build(
            IReadOnlyList<ExtensionModel> extensions, PyNavisConfig config)
        {
            var model = new ShortcutsModel(config.ShortcutsAllowBareKeys);
            foreach (var panel in extensions.SelectMany(e => e.Tabs).SelectMany(t => t.Panels))
            {
                foreach (var button in panel.Buttons)
                {
                    if (button.BundleKey == null || model._byKey.ContainsKey(button.BundleKey))
                        continue;

                    var row = new Row
                    {
                        BundleKey = button.BundleKey,
                        PanelTitle = panel.Title,
                        Title = button.Title,
                        DefaultChord = model.Canonical(button.Shortcut),
                    };
                    // A user entry (even null) replaces the default entirely - same
                    // rule as ShortcutMap. Unparsable overrides fall back to default.
                    if (config.ShortcutBindings.TryGetValue(button.BundleKey, out var userChord))
                        row.Current = userChord == null ? null : model.Canonical(userChord) ?? row.DefaultChord;
                    else
                        row.Current = row.DefaultChord;

                    if (row.IsCustom)
                        model._loaded[row.BundleKey] = row.Current;
                    model._rows.Add(row);
                    model._byKey[row.BundleKey] = row;
                }
            }
            model.Reflag();
            return model;
        }

        /// <summary>Rebinds a row; returns null on success or the parse error text.</summary>
        public string TrySet(string bundleKey, string chordText)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return "unknown tool";
            if (!ChordParser.TryParse(chordText, _allowBareKeys, out var chord, out var error))
                return error;

            row.Current = chord.ToString();
            Reflag();
            return null;
        }

        public void Disable(string bundleKey)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return;
            row.Current = null;
            Reflag();
        }

        public void ResetToDefault(string bundleKey)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return;
            row.Current = row.DefaultChord;
            Reflag();
        }

        public void ResetAll()
        {
            foreach (var row in _rows)
                row.Current = row.DefaultChord;
            Reflag();
        }

        /// <summary>The minimal override map: only rows away from their default;
        /// null means "disable the author default". Stale keys never survive.</summary>
        public Dictionary<string, string> ToBindings()
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in _rows)
                if (row.IsCustom && (row.Current != null || row.DefaultChord != null))
                    bindings[row.BundleKey] = row.Current;
            return bindings;
        }

        private string Canonical(string chordText) =>
            chordText != null && ChordParser.TryParse(chordText, _allowBareKeys, out var chord, out _)
                ? chord.ToString() : null;

        /// <summary>First-wins conflict pass in ribbon order, matching ShortcutMap.</summary>
        private void Reflag()
        {
            var owners = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in _rows)
            {
                row.ConflictWith = null;
                if (row.Current == null) continue;
                if (owners.TryGetValue(row.Current, out var winner))
                    row.ConflictWith = winner.Title;
                else
                    owners[row.Current] = row;
            }
        }
    }
}
