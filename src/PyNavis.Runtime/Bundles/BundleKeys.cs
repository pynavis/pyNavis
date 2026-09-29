using System.Linq;
using System.Text.RegularExpressions;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// A bundle key is the bundle folder's path relative to its extension, forward
    /// slashes, with any NN_ ordering prefix dropped from every segment. It is the
    /// identity config.json stores shortcut overrides and pane slots under, so it must
    /// survive an author renumbering or renaming a prefix.
    /// </summary>
    public static class BundleKeys
    {
        // Two or more digits, so a tool genuinely called "1_Thing" keeps its name.
        private static readonly Regex OrderPrefix = new Regex(@"^\d{2,}_");

        /// <summary>Drops the ordering prefix from every segment of a bundle key.
        /// A key without prefixes comes back unchanged; null and empty pass through.</summary>
        public static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf('_') < 0) return key;
            return string.Join("/", key.Split('/').Select(s => OrderPrefix.Replace(s, "")));
        }

        /// <summary>The title-side of the same rule: strip the prefix from one folder
        /// name (no path), for the parser and the layout matcher.</summary>
        public static string StripPrefix(string folderName) =>
            string.IsNullOrEmpty(folderName) ? folderName : OrderPrefix.Replace(folderName, "");
    }
}
