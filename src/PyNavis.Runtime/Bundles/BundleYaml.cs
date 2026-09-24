using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// Minimal flat-YAML reader for bundle.yaml / extension.yaml (v1 spec: "key: value"
    /// lines, # comments, optional quotes). Deliberately not a YAML library - the v1
    /// spec is flat; swap for YamlDotNet if the spec ever grows nesting.
    /// </summary>
    public static class BundleYaml
    {
        /// <summary>Parses flat key/value pairs. Keys are lowercased; values keep their case.</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return result;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[value.Length - 1] == '"') ||
                     (value[0] == '\'' && value[value.Length - 1] == '\'')))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                result[key] = value;
            }
            return result;
        }
    }
}
