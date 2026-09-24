using System;
using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Output
{
    /// <summary>WPF-free color math so theming logic stays unit-testable.</summary>
    public static class ThemeMath
    {
        /// <summary>WCAG relative luminance of an sRGB color (0 = black, 1 = white).</summary>
        public static double Luminance(int r, int g, int b)
        {
            double Channel(int v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
        }

        public static bool IsDarkColor(int r, int g, int b) => Luminance(r, g, b) < 0.5;

        /// <summary>WCAG contrast ratio between two colors (1..21).</summary>
        public static double ContrastRatio(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            var l1 = Luminance(r1, g1, b1);
            var l2 = Luminance(r2, g2, b2);
            var lighter = Math.Max(l1, l2);
            var darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }
    }

    /// <summary>
    /// The two output-window palettes as plain sRGB ints (converted to WPF brushes at
    /// the window edge). Both are tested for WCAG AA contrast - tracebacks included.
    /// </summary>
    public sealed class OutputPalette
    {
        public static readonly OutputPalette Light = new OutputPalette
        {
            BackgroundR = 255, BackgroundG = 255, BackgroundB = 255,
            ForegroundR = 30, ForegroundG = 30, ForegroundB = 30,
            ErrorR = 179, ErrorG = 38, ErrorB = 30,
            ChromeR = 243, ChromeG = 243, ChromeB = 243,
            AccentR = 214, AccentG = 214, AccentB = 214,
        };

        public static readonly OutputPalette Dark = new OutputPalette
        {
            BackgroundR = 30, BackgroundG = 30, BackgroundB = 30,
            ForegroundR = 220, ForegroundG = 220, ForegroundB = 220,
            ErrorR = 255, ErrorG = 123, ErrorB = 114,
            ChromeR = 45, ChromeG = 45, ChromeB = 45,
            AccentR = 70, AccentG = 70, AccentB = 70,
        };

        public static OutputPalette For(bool dark) => dark ? Dark : Light;

        public int BackgroundR, BackgroundG, BackgroundB;
        public int ForegroundR, ForegroundG, ForegroundB;
        public int ErrorR, ErrorG, ErrorB;
        /// <summary>Toolbar / secondary surfaces.</summary>
        public int ChromeR, ChromeG, ChromeB;
        /// <summary>Borders and control edges on the chrome.</summary>
        public int AccentR, AccentG, AccentB;
    }

    /// <summary>Find-in-output logic; the window only jumps between the offsets.</summary>
    public static class TextSearch
    {
        public static int[] FindAll(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return new int[0];

            var hits = new List<int>();
            var at = 0;
            while ((at = text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                hits.Add(at);
                at += 1;
            }
            return hits.ToArray();
        }

        /// <summary>First hit strictly after <paramref name="position"/>, wrapping to the first.</summary>
        /// <summary>
        /// What the find field reports: nothing while the box is empty, the
        /// position within the matches once there are any, and a plain "no
        /// matches" when there are none. Without this the window rewound to the
        /// top in silence and looked broken.
        /// </summary>
        public static string StatusFor(int[] hits, int currentOffset, string query)
        {
            if (string.IsNullOrEmpty(query)) return "";
            if (hits == null || hits.Length == 0) return "no matches";
            var index = System.Array.IndexOf(hits, currentOffset);
            return index < 0
                ? string.Format("{0} matches", hits.Length)
                : string.Format("{0} of {1}", index + 1, hits.Length);
        }

        public static int NextAfter(int[] hits, int position)
        {
            if (hits.Length == 0) return -1;
            foreach (var hit in hits)
                if (hit > position) return hit;
            return hits[0];
        }
    }
}
