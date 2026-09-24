using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PyNavis
{
    /// <summary>
    /// Resolves PyNavis.Runtime and its dependencies (IronPython, DLR, ...) from the
    /// pyNavis runtime directory, which lives OUTSIDE the Navisworks Plugins folder.
    /// Runtime dir resolution order:
    ///   1. env var PYNAVIS_RUNTIME
    ///   2. %APPDATA%\pyNavis\config.json  ("runtime2026": "..." - keyed by Navisworks
    ///      release so multiple installed versions never share one runtime build)
    ///   3. %APPDATA%\pyNavis\{NavisVersion}\runtime      (the per-user installer's layout)
    ///   4. %PROGRAMDATA%\pyNavis\{NavisVersion}\runtime  (a machine-wide deployment)
    /// </summary>
    internal static class AssemblyResolver
    {
        private static bool _installed;
        internal static string RuntimeDir { get; private set; }

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            RuntimeDir = ResolveRuntimeDir();
            LoaderLog.Info($"AssemblyResolver installed. RuntimeDir = '{RuntimeDir ?? "<null>"}'");
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;

            // This event is process-wide: every other add-in's failed bind arrives here
            // too. Answering those by simple name would hand them our copy, or whatever
            // same-named assembly happens to be loaded, at a version they did not ask for.
            // A requester the CLR cannot name (Assembly.Load by string, which is how the
            // Python engines and clr.AddReference load) stays answered, since that is us
            // far more often than not.
            var requester = args.RequestingAssembly;
            if (requester != null && !IsOurs(requester, RuntimeDir)) return null;

            if (RuntimeDir != null)
            {
                var candidate = Path.Combine(RuntimeDir, name + ".dll");
                if (File.Exists(candidate))
                {
                    try
                    {
                        var asm = Assembly.LoadFrom(candidate);
                        LoaderLog.Info($"Resolved '{name}' -> {candidate}");
                        return asm;
                    }
                    catch (Exception ex)
                    {
                        LoaderLog.Error($"Failed loading '{candidate}': {ex}");
                        return null;
                    }
                }
            }

            // Multi-version fallback: a runtime compiled against another Navisworks
            // release's AdWindows requests a strong-name version that will never be on
            // disk here - but the host process already has ITS AdWindows loaded, and
            // that one is the correct assembly to bind to. Same-simple-name match only.
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    LoaderLog.Info($"Resolved '{args.Name}' -> already-loaded {loaded.FullName}");
                    return loaded;
                }
            }
            return null;
        }

        /// <summary>
        /// Whether a requesting assembly belongs to pyNavis: anything named PyNavis* (the
        /// loader, the runtime, the generated pane satellite in its own Plugins folder) or
        /// anything loaded from the runtime directory (IronPython, the DLR, pythonnet...).
        /// An assembly with no location (dynamic, e.g. compiled Python) cannot be placed,
        /// so it counts as ours rather than being refused.
        /// </summary>
        internal static bool IsOurs(Assembly assembly, string runtimeDir)
        {
            string name, location;
            try
            {
                name = assembly.GetName().Name;
                location = assembly.IsDynamic ? null : assembly.Location;
            }
            catch
            {
                return true;
            }

            if (name.StartsWith("PyNavis", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrEmpty(location)) return true;
            if (string.IsNullOrEmpty(runtimeDir)) return false;

            var root = Path.GetFullPath(runtimeDir).TrimEnd('\\', '/') + "\\";
            return Path.GetFullPath(location).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Test seam: runs the resolve logic without raising a real AppDomain event.</summary>
        internal static Assembly ResolveForTest(ResolveEventArgs args) => OnAssemblyResolve(null, args);

        private static string ResolveRuntimeDir()
        {
            var env = Environment.GetEnvironmentVariable("PYNAVIS_RUNTIME");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            // Loader has no JSON parser on purpose (zero dependencies) - a targeted
            // regex over config.json is enough to bootstrap; the runtime does full parsing.
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var key = "runtime" + LoaderInfo.NavisVersion;
                    var m = Regex.Match(File.ReadAllText(configPath), "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success)
                    {
                        var dir = JsonText.UnescapeString(m.Groups[1].Value).Replace('/', '\\');
                        if (Directory.Exists(dir)) return dir;
                        LoaderLog.Error($"config.json runtime dir does not exist: '{dir}'");
                    }
                }
                catch (Exception ex)
                {
                    LoaderLog.Error($"Failed reading '{configPath}': {ex.Message}");
                }
            }

            return FirstExistingRuntimeDir(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                LoaderInfo.NavisVersion, Directory.Exists);
        }

        /// <summary>Pure: the per-user install wins over a machine-wide one, and a
        /// missing folder is skipped rather than reported, so an installer that only
        /// wrote one of the two never leaves a dead path in the log.</summary>
        internal static string FirstExistingRuntimeDir(
            string appData, string programData, string navisVersion, Func<string, bool> exists)
        {
            foreach (var root in new[] { appData, programData })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var candidate = Path.Combine(root, "pyNavis", navisVersion, "runtime");
                if (exists(candidate)) return candidate;
            }
            return null;
        }
    }

    /// <summary>Build-time facts about this loader.</summary>
    internal static class LoaderInfo
    {
        /// <summary>Navisworks release year this loader was compiled for (e.g. "2026"), baked in by Directory.Build.props.</summary>
        internal static string NavisVersion { get; } = ReadNavisVersion();

        private static string ReadNavisVersion()
        {
            foreach (var attr in typeof(LoaderInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                if (attr.Key == "PyNavis.NavisVersion")
                    return attr.Value;
            return "unknown";
        }
    }
}
