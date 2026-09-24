using System.IO;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Engine
{
    /// <summary>
    /// Resolves the engine's global module search paths: python stdlib next to the
    /// runtime dll, and pynavislib from config ("pynavislib" key - dev points it at the
    /// repo checkout) with the installed layout (runtime dir) as fallback.
    /// </summary>
    public static class EngineConfigBuilder
    {
        public static EngineConfig Build(string runtimeDir, PyNavisConfig userConfig,
            System.Collections.Generic.IList<string> libraryDirs = null)
        {
            var config = new EngineConfig();

            var stdlib = Path.Combine(runtimeDir, "lib");
            if (Directory.Exists(stdlib))
                config.IronPythonPaths.Add(stdlib);
            else
                Log.Error($"Python stdlib not found at '{stdlib}' - 'import os' etc. will fail on IronPython.");

            var pynavislib = ResolvePyNavisLib(runtimeDir, userConfig);
            if (pynavislib != null)
                config.SearchPaths.Add(pynavislib);
            else
                Log.Error("pynavislib not found (config 'pynavislib' key or <runtime>\\pynavislib) - 'import pynavis' will fail.");

            if (libraryDirs != null)
                foreach (var lib in libraryDirs)
                    config.SearchPaths.Add(lib);

            config.CPythonDll = userConfig.CPythonPath;

            return config;
        }

        /// <summary>Directory containing the pynavis package, or null when nowhere on disk.</summary>
        public static string ResolvePyNavisLib(string runtimeDir, PyNavisConfig userConfig)
        {
            var configured = userConfig.PyNavisLibPath;
            if (!string.IsNullOrEmpty(configured))
            {
                if (Directory.Exists(configured)) return configured;
                Log.Error($"Configured pynavislib '{configured}' does not exist - trying installed location.");
            }

            var installed = Path.Combine(runtimeDir, "pynavislib");
            return Directory.Exists(installed) ? installed : null;
        }
    }
}
