using System;
using System.Text.RegularExpressions;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>Lightweight, AST-free scan of a script for script metadata.</summary>
    public class ScriptMetadata
    {
        private static readonly Regex TitleRegex = new Regex(
            @"^__title__\s*=\s*(['""])(.*?)\1", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Value of a top-level <c>__title__ = '...'</c> assignment; null if absent.</summary>
        public string Title { get; set; }

        /// <summary>Module docstring (first statement triple-quoted string); null if absent.</summary>
        public string Docstring { get; set; }

        public static ScriptMetadata Scan(string scriptText)
        {
            var meta = new ScriptMetadata();
            if (string.IsNullOrEmpty(scriptText)) return meta;

            var title = TitleRegex.Match(scriptText);
            if (title.Success) meta.Title = title.Groups[2].Value;

            meta.Docstring = FindModuleDocstring(scriptText);
            return meta;
        }

        private static string FindModuleDocstring(string text)
        {
            // A docstring only counts if it is the first statement: skip comments and
            // blank lines, then require the very next content to open a triple quote.
            var pos = 0;
            while (pos < text.Length)
            {
                var eol = text.IndexOf('\n', pos);
                var lineEnd = eol < 0 ? text.Length : eol;
                var line = text.Substring(pos, lineEnd - pos).Trim();
                if (line.Length != 0 && !line.StartsWith("#")) break;
                pos = eol < 0 ? text.Length : eol + 1;
            }

            var rest = text.Substring(pos).TrimStart();
            foreach (var delim in new[] { "\"\"\"", "'''" })
            {
                if (!rest.StartsWith(delim, StringComparison.Ordinal)) continue;
                var end = rest.IndexOf(delim, delim.Length, StringComparison.Ordinal);
                return end < 0 ? null : rest.Substring(delim.Length, end - delim.Length).Trim();
            }
            return null;
        }
    }
}
