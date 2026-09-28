using System;
using System.Collections.Generic;
using System.IO;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// The most recent traceback per bundle folder, kept for the session. The chat pane
    /// reads it to offer "Send last error" for the tool it is editing; the executor
    /// writes it for every failing script, generated or not, because that is cheaper
    /// than deciding which ones matter.
    /// </summary>
    public static class FailureLog
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> Last =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Record(string bundleDir, string traceback)
        {
            if (string.IsNullOrWhiteSpace(bundleDir) || string.IsNullOrWhiteSpace(traceback)) return;
            lock (Gate) Last[Key(bundleDir)] = traceback.Trim();
        }

        public static string LastFor(string bundleDir)
        {
            if (string.IsNullOrWhiteSpace(bundleDir)) return null;
            lock (Gate) return Last.TryGetValue(Key(bundleDir), out var text) ? text : null;
        }

        public static void Clear(string bundleDir)
        {
            if (string.IsNullOrWhiteSpace(bundleDir)) return;
            lock (Gate) Last.Remove(Key(bundleDir));
        }

        private static string Key(string dir)
        {
            try { return Path.GetFullPath(dir).TrimEnd('\\', '/'); }
            catch { return dir.TrimEnd('\\', '/'); }
        }
    }
}
