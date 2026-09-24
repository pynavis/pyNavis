using System;
using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Input
{
    /// <summary>A parsed keyboard chord: modifier flags plus one virtual key.</summary>
    public struct Chord : IEquatable<Chord>
    {
        public bool Ctrl, Alt, Shift;
        public int VirtualKey;

        /// <summary>The modifier policy: bare and Shift-only chords are rejected unless opted in.</summary>
        public bool HasRequiredModifier => Ctrl || Alt;

        public bool Equals(Chord other) =>
            Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift
            && VirtualKey == other.VirtualKey;

        public override bool Equals(object obj) => obj is Chord other && Equals(other);

        public override int GetHashCode() =>
            VirtualKey ^ (Ctrl ? 0x10000 : 0) ^ (Alt ? 0x20000 : 0) ^ (Shift ? 0x40000 : 0);

        /// <summary>Canonical display form, e.g. "Ctrl+Shift+M"; used in tooltips.</summary>
        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(ChordParser.NameOf(VirtualKey));
            return string.Join("+", parts);
        }
    }

    /// <summary>Parses "Ctrl+Shift+M" style binding strings. Pure, fully unit-tested.</summary>
    public static class ChordParser
    {
        private static readonly Dictionary<string, int> Keys = BuildKeys();
        private static readonly Dictionary<int, string> Names = Keys
            .GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key);

        private static Dictionary<string, int> BuildKeys()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;            // 0x41..0x5A
            for (var d = '0'; d <= '9'; d++) map[d.ToString()] = d;            // 0x30..0x39
            for (var f = 1; f <= 24; f++) map["F" + f] = 0x6F + f;             // VK_F1 = 0x70
            map["Left"] = 0x25; map["Up"] = 0x26; map["Right"] = 0x27; map["Down"] = 0x28;
            map["Home"] = 0x24; map["End"] = 0x23; map["PgUp"] = 0x21; map["PgDn"] = 0x22;
            return map;
        }

        /// <summary>Display name for a supported virtual key (inverse of parsing).</summary>
        public static string NameOf(int virtualKey) =>
            Names.TryGetValue(virtualKey, out var name) ? name : "0x" + virtualKey.ToString("X2");

        public static bool TryParse(string text, bool allowBareKeys, out Chord chord, out string error)
        {
            chord = default;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "empty binding";
                return false;
            }

            var result = new Chord();
            var key = -1;
            foreach (var raw in text.Split('+'))
            {
                var part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) result.Ctrl = true;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) result.Alt = true;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) result.Shift = true;
                else if (Keys.TryGetValue(part, out var vk))
                {
                    if (key != -1)
                    {
                        error = $"'{text}': more than one key";
                        return false;
                    }
                    key = vk;
                }
                else
                {
                    error = $"'{text}': unknown token '{part}'";
                    return false;
                }
            }

            if (key == -1)
            {
                error = $"'{text}': no key";
                return false;
            }

            result.VirtualKey = key;
            if (!allowBareKeys && !result.HasRequiredModifier)
            {
                error = $"'{text}': bindings need Ctrl or Alt (set shortcuts.allowBareKeys to permit bare keys)";
                return false;
            }

            chord = result;
            return true;
        }
    }
}
