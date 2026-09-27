using System;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// The two quiet hints a pyNavis button can carry, both appended to its caption:
    /// a marker when the bundle has a config.py (the Shift+Click secondary action) and
    /// another when a keyboard chord resolves to it. They live in the caption rather
    /// than on the icon because a badge drawn over the art covered the glyph it was
    /// meant to annotate (field report 2026-09-26).
    ///
    /// Both are read from config.json once per ribbon build, so Reload picks up an
    /// edit without a restart. Everything here is pure and unit tested.
    /// </summary>
    public static class RibbonMarkers
    {
        /// <summary>Appended to a button caption when the bundle has a config.py.
        /// "ribbon.configMarker". Empty turns it off. The default is the Shift key's own
        /// symbol, so the caption says which modifier opens the action.</summary>
        public static string ConfigMarker { get; private set; } = DefaultConfigMarker;

        public const string DefaultConfigMarker = "\u21E7";

        /// <summary>Appended to a button caption when a chord resolves to it.
        /// "ribbon.shortcutMarker". Empty turns it off.</summary>
        public static string ShortcutMarker { get; private set; } = "●";

        /// <summary>Re-reads both settings. Called once per ribbon build.</summary>
        public static void Configure(PyNavisConfig config)
        {
            ConfigMarker = config == null ? DefaultConfigMarker : (config.RibbonConfigMarker ?? "");
            ShortcutMarker = config == null ? "●" : (config.RibbonShortcutMarker ?? "");
        }

        /// <summary>Test seam: puts both settings back where a fresh install has them.</summary>
        internal static void ResetForTests()
        {
            ConfigMarker = DefaultConfigMarker;
            ShortcutMarker = "●";
        }

        /// <summary>The caption a button with a config.py should show; see WithShortcutMarker
        /// for how a marker joins a caption. When both hints apply, this one goes first
        /// so the caption reads "Name ⇧ ●".</summary>
        public static string WithConfigMarker(string caption) => Append(caption, ConfigMarker);

        /// <summary>
        /// The caption a button with a chord should show.
        ///
        /// The marker joins the LAST line, because a large button's RibbonTitle carries
        /// its own newline to wrap a long name onto two lines and appending past that
        /// would strand the marker beside the first word. A marker that starts with a
        /// newline is taken at its word and gets its own line, which is how a caller
        /// asks for the marker under the name rather than beside it.
        /// </summary>
        public static string WithShortcutMarker(string caption) => Append(caption, ShortcutMarker);

        private static string Append(string caption, string marker)
        {
            if (string.IsNullOrEmpty(marker)) return caption;
            if (string.IsNullOrEmpty(caption)) return caption;
            if (marker.StartsWith("\n", StringComparison.Ordinal)
                || marker.StartsWith("\r", StringComparison.Ordinal))
                return caption + marker;
            return caption + " " + marker;
        }

    }
}
