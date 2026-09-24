using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PyNavis.Runtime.Install
{
    /// <summary>Where the per-user installer and a machine-wide deployment put things,
    /// as pure path logic the host consults at boot.</summary>
    public static class InstallPaths
    {
        /// <summary>Where installers put the shipped extension: the per-user installer
        /// under %APPDATA%, a machine-wide deployment under %PROGRAMDATA%.</summary>
        public static IEnumerable<string> DefaultExtensionRoots()
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "extensions");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "pyNavis", "extensions");
        }

        /// <summary>Pure: config.json roots first and in order, then each default root
        /// that exists on disk and is not already listed.</summary>
        public static List<string> ExtensionRoots(
            IEnumerable<string> configured, IEnumerable<string> defaults, Func<string, bool> exists)
        {
            var roots = new List<string>(configured);
            foreach (var installed in defaults)
                if (exists(installed) && !roots.Contains(installed, StringComparer.OrdinalIgnoreCase))
                    roots.Add(installed);
            return roots;
        }
    }
}
