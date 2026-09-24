using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Pure parts of CPython discovery (explicit paths, dll picking). The registry
    /// scan itself is a thin untested shell over these.
    /// </summary>
    public class CPythonLocatorTests : IDisposable
    {
        private readonly string _dir;

        public CPythonLocatorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Touch(string name)
        {
            var p = Path.Combine(_dir, name);
            File.WriteAllText(p, "");
            return p;
        }

        [Fact]
        public void ExplicitDllFile_IsReturnedAsIs()
        {
            var dll = Touch("python311.dll");
            Assert.Equal(dll, CPythonLocator.ResolveConfigured(dll));
        }

        [Fact]
        public void InstallDir_ResolvesToItsPythonDll()
        {
            Touch("python311.dll");
            Assert.Equal(Path.Combine(_dir, "python311.dll"), CPythonLocator.ResolveConfigured(_dir));
        }

        [Fact]
        public void DirWithMultipleVersions_PicksHighest()
        {
            Touch("python39.dll");
            Touch("python311.dll");
            Touch("python310.dll");
            Assert.Equal(Path.Combine(_dir, "python311.dll"), CPythonLocator.FindDllIn(_dir));
        }

        [Fact]
        public void Python3Dll_IsIgnored_OnlyVersionedDllsCount()
        {
            // python3.dll is the stable-ABI stub; pythonnet needs the real python3XX.dll.
            Touch("python3.dll");
            Touch("python312.dll");
            Assert.Equal(Path.Combine(_dir, "python312.dll"), CPythonLocator.FindDllIn(_dir));
        }

        // Registry PythonCore key names, newest usable first. They were sorted as strings,
        // so "3.9" beat "3.13" and an older interpreter silently won.
        [Fact]
        public void RegistryVersions_SortNumerically_NotAsStrings()
        {
            Assert.Equal(new[] { "3.13", "3.11", "3.9" },
                CPythonLocator.UsableVersionKeys(new[] { "3.9", "3.13", "3.11" }));
        }

        // A 32-bit python3XX.dll cannot load into 64-bit Navisworks; neither can ARM64.
        [Fact]
        public void RegistryVersions_Skip32BitAndArmInstalls()
        {
            Assert.Equal(new[] { "3.11" },
                CPythonLocator.UsableVersionKeys(new[] { "3.12-32", "3.11", "3.13-arm64" }));
        }

        // pythonnet hosts a fixed range: a Python newer than it knows breaks the engine,
        // so a fresh install of one must not silently take over from a working one.
        [Fact]
        public void RegistryVersions_SkipWhatPythonnetCannotHost()
        {
            Assert.Equal(new[] { "3.13", "3.7" },
                CPythonLocator.UsableVersionKeys(new[] { "3.14", "3.13", "3.7", "3.6", "2.7", "4.0" }));
        }

        [Fact]
        public void RegistryVersions_IgnoreKeysThatAreNotVersions()
        {
            Assert.Equal(new[] { "3.10" },
                CPythonLocator.UsableVersionKeys(new[] { "ContinuumAnalytics", "", "3", "3.x", "3.10" }));
        }

        [Fact]
        public void MissingPath_YieldsNull()
        {
            Assert.Null(CPythonLocator.ResolveConfigured(Path.Combine(_dir, "nope")));
            Assert.Null(CPythonLocator.ResolveConfigured(null));
            Assert.Null(CPythonLocator.FindDllIn(_dir));
        }
    }
}
