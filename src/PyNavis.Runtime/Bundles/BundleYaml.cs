using System;
using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// Minimal flat-YAML reader for bundle.yaml / extension.yaml (v1 spec: "key: value"
    /// lines, # comments, optional quotes) plus one shape of nesting: a key with no
    /// value followed by "- item" lines is a block list, stored as the items joined by
    /// newlines so the dictionary stays string-to-string. Deliberately not a YAML
    /// library; swap for YamlDotNet if the spec ever grows real nesting.
    /// </summary>
    public static class BundleYaml
    {
        /// <summary>Parses flat key/value pairs. Keys are lowercased; values keep their case.</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return result;

            string listKey = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (listKey != null && line.StartsWith("- "))
                {
                    var item = Unquote(line.Substring(2).Trim());
                    result[listKey] = result[listKey].Length == 0 ? item : result[listKey] + "\n" + item;
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = Unquote(line.Substring(colon + 1).Trim());
                result[key] = value;
                // An empty value opens a block list; the next "- item" lines belong to it.
                listKey = value.Length == 0 ? key : null;
            }
            return result;
        }

        /// <summary>The items of a block-list value, or an empty list when the key is
        /// absent or has no items.</summary>
        public static IReadOnlyList<string> ListOf(Dictionary<string, string> yaml, string key)
        {
            string value;
            if (yaml == null || !yaml.TryGetValue(key, out value) || string.IsNullOrEmpty(value))
                return new string[0];
            return value.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }
            return value;
        }
    }
}
