using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PyNavis.Runtime.Engine
{
    /// <summary>
    /// Finds the CPython dll pythonnet should host. Resolution order:
    ///   1. config "cpython" key (dll file or install dir)
    ///   2. PYNAVIS_CPYTHON environment variable (same forms)
    ///   3. registry PythonCore installs (HKCU then HKLM), highest version wins
    /// </summary>
    public static class CPythonLocator
    {
        // python3.dll is the stable-ABI stub; pythonnet needs the real versioned dll.
        private static readonly Regex VersionedDll = new Regex(
            @"^python3(\d+)\.dll$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Resolve(string configured)
        {
            var fromConfig = ResolveConfigured(configured);
            if (fromConfig != null) return fromConfig;

            var fromEnv = ResolveConfigured(Environment.GetEnvironmentVariable("PYNAVIS_CPYTHON"));
            if (fromEnv != null) return fromEnv;

            return FromRegistry();
        }

        /// <summary>Explicit dll path or install dir → versioned python dll path; null when invalid.</summary>
        public static string ResolveConfigured(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (File.Exists(path)) return path;
            if (Directory.Exists(path)) return FindDllIn(path);
            return null;
        }

        /// <summary>Highest-versioned python3XX.dll directly in <paramref name="dir"/>; null if none.</summary>
        public static string FindDllIn(string dir)
        {
            return Directory.GetFiles(dir, "python3*.dll")
                .Select(p => new { Path = p, Match = VersionedDll.Match(Path.GetFileName(p)) })
                .Where(x => x.Match.Success)
                .OrderByDescending(x => int.Parse(x.Match.Groups[1].Value))
                .Select(x => x.Path)
                .FirstOrDefault();
        }

        // The range the shipped pythonnet (3.0.5) can host. Raise MaxMinor with pythonnet.
        private const int MinMinor = 7;
        private const int MaxMinor = 13;

        private static readonly Regex VersionKey = new Regex(@"^3\.(\d{1,3})$", RegexOptions.Compiled);

        /// <summary>
        /// PythonCore registry key names this process can actually host, newest first.
        /// Compared as numbers ("3.13" beats "3.9"); 32-bit ("3.12-32") and ARM64 installs
        /// are dropped because their dll cannot load into 64-bit Navisworks; so is anything
        /// outside pythonnet's range, so installing a brand-new Python never silently
        /// replaces a working interpreter with one the engine cannot start. The config
        /// "cpython" key still overrides all of this.
        /// </summary>
        public static string[] UsableVersionKeys(System.Collections.Generic.IEnumerable<string> keyNames)
        {
            return keyNames
                .Select(k => new { Key = k, Match = VersionKey.Match(k ?? "") })
                .Where(x => x.Match.Success)
                .Select(x => new { x.Key, Minor = int.Parse(x.Match.Groups[1].Value) })
                .Where(x => x.Minor >= MinMinor && x.Minor <= MaxMinor)
                .OrderByDescending(x => x.Minor)
                .Select(x => x.Key)
                .ToArray();
        }

        private static string FromRegistry()
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var core = hive.OpenSubKey(@"SOFTWARE\Python\PythonCore"))
                    {
                        if (core == null) continue;
                        var best = UsableVersionKeys(core.GetSubKeyNames())
                            .Select(v => new { Version = v, Dll = DllFromCoreKey(core, v) })
                            .FirstOrDefault(x => x.Dll != null);
                        if (best != null)
                        {
                            Log.Info($"CPython {best.Version} found in the registry: {best.Dll}");
                            return best.Dll;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("CPython registry scan failed for " + hive.Name, ex);
                }
            }
            return null;
        }

        private static string DllFromCoreKey(RegistryKey core, string version)
        {
            using (var key = core.OpenSubKey(version + @"\InstallPath"))
            {
                var dir = key?.GetValue(null) as string;
                return dir != null && Directory.Exists(dir) ? FindDllIn(dir) : null;
            }
        }
    }
}
