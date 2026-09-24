using System;
using System.IO;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Search-path resolution for the engine: stdlib next to the runtime dll, pynavislib
    /// from config (dev: repo checkout) falling back to runtime dir (installed layout).
    /// </summary>
    public class EngineConfigBuilderTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _runtimeDir;

        public EngineConfigBuilderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            _runtimeDir = Path.Combine(_dir, "runtime");
            Directory.CreateDirectory(_runtimeDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static PyNavisConfig ConfigWith(string dir, string json)
        {
            var p = Path.Combine(dir, "config.json");
            File.WriteAllText(p, json);
            return PyNavisConfig.Load(p);
        }

        [Fact]
        public void StdlibNextToRuntime_IsAdded_ToIronPythonPathsOnly()
        {
            // The bundled stdlib is IronPython 3.4's. CPython brings its own and must
            // never see this one, so it goes in IronPythonPaths, not shared SearchPaths.
            var lib = Path.Combine(_runtimeDir, "lib");
            Directory.CreateDirectory(lib);

            var config = EngineConfigBuilder.Build(_runtimeDir, new PyNavisConfig());

            Assert.Contains(lib, config.IronPythonPaths);
            Assert.DoesNotContain(lib, config.SearchPaths);
        }

        [Fact]
        public void MissingStdlib_IsNotAdded()
        {
            var config = EngineConfigBuilder.Build(_runtimeDir, new PyNavisConfig());

            Assert.Empty(config.IronPythonPaths);
            Assert.Empty(config.SearchPaths);
        }

        [Fact]
        public void PyNavisLib_FromConfig_IsAdded()
        {
            var libDir = Path.Combine(_dir, "repo", "pynavislib");
            Directory.CreateDirectory(libDir);
            var userConfig = ConfigWith(_dir, "{ \"pynavislib\": " + Json(libDir) + " }");

            var config = EngineConfigBuilder.Build(_runtimeDir, userConfig);

            Assert.Contains(libDir, config.SearchPaths);
        }

        [Fact]
        public void PyNavisLib_ConfigPathMissingOnDisk_FallsBackToRuntimeDir()
        {
            var fallback = Path.Combine(_runtimeDir, "pynavislib");
            Directory.CreateDirectory(fallback);
            var userConfig = ConfigWith(_dir, "{ \"pynavislib\": \"D:\\\\does\\\\not\\\\exist\" }");

            var config = EngineConfigBuilder.Build(_runtimeDir, userConfig);

            Assert.Contains(fallback, config.SearchPaths);
        }

        [Fact]
        public void PyNavisLib_NoConfigKey_UsesRuntimeDirWhenPresent()
        {
            var fallback = Path.Combine(_runtimeDir, "pynavislib");
            Directory.CreateDirectory(fallback);

            var config = EngineConfigBuilder.Build(_runtimeDir, new PyNavisConfig());

            Assert.Contains(fallback, config.SearchPaths);
        }

        [Fact]
        public void CPythonKey_IsPassedThrough_ToEngineConfig()
        {
            var userConfig = ConfigWith(_dir, "{ \"cpython\": \"C:\\\\Py311\" }");

            var config = EngineConfigBuilder.Build(_runtimeDir, userConfig);

            Assert.Equal(@"C:\Py311", config.CPythonDll);
        }

        [Fact]
        public void PyNavisLib_NowhereOnDisk_IsOmitted()
        {
            var config = EngineConfigBuilder.Build(_runtimeDir, new PyNavisConfig());

            Assert.Empty(config.SearchPaths);
        }

        private static string Json(string path) => "\"" + path.Replace("\\", "\\\\") + "\"";
    }
}
