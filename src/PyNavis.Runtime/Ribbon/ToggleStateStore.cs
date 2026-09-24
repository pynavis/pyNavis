using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// Session-scoped on/off state per toggle bundle, keyed by bundle key. Never
    /// cleared on Reload so a toggled state survives a ribbon rebuild; persistence
    /// across restarts is the script's own business (pynavis.settings).
    /// </summary>
    public static class ToggleStateStore
    {
        private static readonly Dictionary<string, bool> States =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised with the bundle key after every Set; the ribbon refreshes icons off it.</summary>
        public static event Action<string> Changed;

        public static bool Get(string bundleKey) =>
            bundleKey != null && States.TryGetValue(bundleKey, out var on) && on;

        public static void Set(string bundleKey, bool on)
        {
            if (bundleKey == null) return;
            States[bundleKey] = on;
            Changed?.Invoke(bundleKey);
        }
    }
}
