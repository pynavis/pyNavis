using System;
using System.Collections.Generic;
using PyNavis.Runtime.Bundles;

namespace PyNavis.Runtime.Input
{
    /// <summary>
    /// The resolved chord table: author defaults from bundle.yaml merged with user
    /// overrides from config.json (user wins, null disables). Pure - built from data,
    /// no hook, no ribbon - so the merge and conflict rules are fully unit-tested.
    /// </summary>
    public sealed class ShortcutMap
    {
        private readonly Dictionary<Chord, PushButtonModel> _byChord =
            new Dictionary<Chord, PushButtonModel>();
        private readonly Dictionary<string, Chord> _byKey =
            new Dictionary<string, Chord>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _problems = new List<string>();

        /// <summary>Human-readable parse and conflict reports; the caller logs them.</summary>
        public IReadOnlyList<string> Problems => _problems;

        public int Count => _byChord.Count;

        public bool TryGetButton(Chord chord, out PushButtonModel button) =>
            _byChord.TryGetValue(chord, out button);

        /// <summary>The resolved binding for a button, or null; drives tooltip suffixes.</summary>
        public Chord? BindingFor(PushButtonModel button) =>
            button?.BundleKey != null && _byKey.TryGetValue(button.BundleKey, out var chord)
                ? chord : (Chord?)null;

        public static ShortcutMap Build(
            IReadOnlyList<PushButtonModel> buttons,
            IReadOnlyDictionary<string, string> overrides,
            bool allowBareKeys)
        {
            var map = new ShortcutMap();
            var byKey = new Dictionary<string, PushButtonModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var button in buttons)
                if (button.BundleKey != null && !byKey.ContainsKey(button.BundleKey))
                    byKey[button.BundleKey] = button;

            foreach (var pair in overrides)
                if (!byKey.ContainsKey(pair.Key))
                    map._problems.Add($"shortcut override for unknown tool '{pair.Key}' ignored");

            foreach (var button in buttons)
            {
                // A user entry (even null) replaces the author default entirely.
                var overridden = overrides.TryGetValue(button.BundleKey ?? "", out var userChord);
                var text = overridden ? userChord : button.Shortcut;
                if (text == null) continue;   // unbound, or explicitly disabled

                if (!ChordParser.TryParse(text, allowBareKeys, out var chord, out var error))
                {
                    map._problems.Add($"'{button.BundleKey}': {error}");
                    continue;
                }
                if (map._byChord.TryGetValue(chord, out var winner))
                {
                    map._problems.Add(
                        $"'{button.BundleKey}' wants {chord} but '{winner.BundleKey}' already has it - first wins");
                    continue;
                }
                map._byChord[chord] = button;
                map._byKey[button.BundleKey] = chord;
            }
            return map;
        }
    }
}
