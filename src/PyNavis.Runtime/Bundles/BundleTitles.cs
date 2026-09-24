using System.Text.RegularExpressions;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// The two forms of a bundle caption. A long caption stretches its ribbon
    /// panel wide, so bundle.yaml may break one with a literal <c>\n</c>:
    ///
    ///     title: Export\nViewpoints
    ///
    /// The ribbon draws the break; everything else (logs, toasts, the output
    /// window title, keytip letters, <c>__title__</c>) wants one line. Authors
    /// write the break with or without surrounding spaces, so both collapse to
    /// exactly one line break, and neither form keeps a stray double space.
    /// </summary>
    public static class BundleTitles
    {
        private static readonly Regex Break = new Regex(@"[ \t]*\\n[ \t]*", RegexOptions.Compiled);
        private static readonly Regex Runs = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);

        /// <summary>The caption as the ribbon draws it: breaks become newlines.</summary>
        public static string ForRibbon(string title) =>
            title == null ? null : Runs.Replace(Break.Replace(title, "\n"), " ").Trim();

        /// <summary>The caption on one line: breaks become single spaces.</summary>
        public static string Flatten(string title) =>
            title == null ? null : Runs.Replace(Break.Replace(title, " "), " ").Trim();
    }
}
