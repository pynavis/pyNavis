using System;
using System.Collections.Generic;
using System.IO;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// The authoring guide the model reads, and the text the Copy button hands to
    /// people who would rather use another assistant. Generated at dev time by
    /// tools/build_authoring_pack.py into Ai/authoring-pack.md and shipped beside the
    /// runtime assembly. Never hand-edit the file: change the sources and re-run.
    /// </summary>
    public static class AuthoringPack
    {
        public const string FileName = "authoring-pack.md";

        private static string _cached;

        /// <summary>Beside the runtime assembly first; the app base directory second,
        /// for hosts that shadow-copy assemblies (the test runner does).</summary>
        public static IEnumerable<string> CandidatePaths()
        {
            var location = typeof(AuthoringPack).Assembly.Location;
            if (!string.IsNullOrEmpty(location))
                yield return Path.Combine(Path.GetDirectoryName(location) ?? "", FileName);
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", FileName);
        }

        /// <summary>The shipped pack; empty (and logged) when it is missing.</summary>
        public static string Load()
        {
            if (!string.IsNullOrEmpty(_cached)) return _cached;
            foreach (var path in CandidatePaths())
            {
                var text = LoadFrom(path);
                if (text.Length > 0)
                {
                    _cached = text;
                    return text;
                }
            }
            Log.Error("authoring-pack.md is missing beside the runtime; the assistant will know little about pyNavis.");
            return "";
        }

        public static string LoadFrom(string path)
        {
            try
            {
                return File.Exists(path) ? TextFiles.Read(path) : "";
            }
            catch (Exception ex)
            {
                Log.Error($"Could not read '{path}'", ex);
                return "";
            }
        }
    }
}
