using System;
using System.IO;
using PyNavis.Cli;
using PyNavis.Runtime.Config;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pynavis CLI's core operations, exercised against fake filesystem layouts.
    /// The exe's Main is a thin arg-parser over this class.
    /// </summary>
    public class CliCoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _binRoot;
        private readonly string _navisDir;
        private readonly string _bundleRoot;
        private readonly string _configPath;
        private readonly StringWriter _out = new StringWriter();
        private readonly CliCore _cli;

        public CliCoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            _binRoot = Path.Combine(_dir, "bin");
            _navisDir = Path.Combine(_dir, "NW2026");
            _bundleRoot = Path.Combine(_dir, "ApplicationPlugins", "pyNavis.bundle");
            _configPath = Path.Combine(_dir, "config.json");

            Directory.CreateDirectory(Path.Combine(_binRoot, "2026"));
            File.WriteAllText(Path.Combine(_binRoot, "2026", "PyNavis.dll"), "fake loader");
            Directory.CreateDirectory(_navisDir);
            File.WriteAllText(Path.Combine(_navisDir, "Roamer.exe"), "");

            _cli = new CliCore(_binRoot, _configPath, _bundleRoot, year => year == "2026" ? _navisDir : null, _out);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Attach_CopiesLoader_IntoTheBundle_AndRegistersItsYear()
        {
            var exit = _cli.Attach("2026");

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(_bundleRoot, "Contents", "2026", "PyNavis.dll")));
            Assert.False(Directory.Exists(Path.Combine(_navisDir, "Plugins", "PyNavis")));
            var manifest = File.ReadAllText(Path.Combine(_bundleRoot, "PackageContents.xml"));
            Assert.Contains("SeriesMin=\"Nw23\"", manifest);
            Assert.Contains("ModuleName=\"./Contents/2026/PyNavis.dll\"", manifest);
            Assert.Contains("AppType=\"ManagedPlugin\"", manifest);
        }

        [Fact]
        public void Attach_Twice_KeepsOneEntry_AndOtherYearsIntact()
        {
            Directory.CreateDirectory(Path.Combine(_bundleRoot, "Contents", "2025"));
            Directory.CreateDirectory(_bundleRoot);
            File.WriteAllText(Path.Combine(_bundleRoot, "PackageContents.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><ApplicationPackage SchemaVersion=\"1.0\" Name=\"pyNavis\" AppVersion=\"0.4.0\">" +
                "<Components Description=\"Navisworks 2025\"><RuntimeRequirements OS=\"Win64\" Platform=\"NAVMAN|NAVSIM\" SeriesMin=\"Nw22\" SeriesMax=\"Nw22\" />" +
                "<ComponentEntry AppName=\"pyNavis\" AppType=\"ManagedPlugin\" ModuleName=\"./Contents/2025/PyNavis.dll\" /></Components></ApplicationPackage>");

            _cli.Attach("2026");
            _cli.Attach("2026");

            var manifest = File.ReadAllText(Path.Combine(_bundleRoot, "PackageContents.xml"));
            Assert.Equal(1, CountOf(manifest, "./Contents/2026/PyNavis.dll"));
            Assert.Equal(1, CountOf(manifest, "./Contents/2025/PyNavis.dll"));
        }

        [Fact]
        public void Attach_WarnsAboutALegacyPluginsCopy()
        {
            Directory.CreateDirectory(Path.Combine(_navisDir, "Plugins", "PyNavis"));

            _cli.Attach("2026");

            Assert.Contains("legacy", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static int CountOf(string text, string needle)
        {
            int count = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
            return count;
        }

        [Fact]
        public void Attach_UnknownVersion_Fails()
        {
            Assert.Equal(1, _cli.Attach("2024"));
            Assert.Contains("not found", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Attach_WithoutBuild_Fails()
        {
            File.Delete(Path.Combine(_binRoot, "2026", "PyNavis.dll"));
            Assert.Equal(1, _cli.Attach("2026"));
            Assert.Contains("build", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Detach_RemovesTheYear_AndTheBundleWhenItWasTheLastOne()
        {
            _cli.Attach("2026");

            var exit = _cli.Detach("2026");

            Assert.Equal(0, exit);
            Assert.False(Directory.Exists(Path.Combine(_bundleRoot, "Contents", "2026")));
            Assert.False(Directory.Exists(_bundleRoot));
        }

        [Fact]
        public void Detach_WhenNotAttached_SaysSo_AndSucceeds()
        {
            Assert.Equal(0, _cli.Detach("2026"));
            Assert.Contains("not attached", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExtensionsAdd_CreatesConfig_AndListShowsIt()
        {
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.Equal(0, _cli.ExtensionsAdd(extDir));
            Assert.Equal(0, _cli.ExtensionsList());

            Assert.Contains(extDir, _out.ToString());
            Assert.Contains(extDir, PyNavisConfig.Load(_configPath).ExtensionPaths);
        }

        [Fact]
        public void ExtensionsAdd_PreservesOtherConfigKeys_AndDeduplicates()
        {
            File.WriteAllText(_configPath, "{ \"runtime2026\": \"D:\\\\r\", \"extensions\": [\"D:\\\\existing\"] }");
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.Equal(0, _cli.ExtensionsAdd(extDir));
            Assert.Equal(0, _cli.ExtensionsAdd(extDir)); // second add must not duplicate

            var config = PyNavisConfig.Load(_configPath);
            Assert.Equal(new[] { @"D:\existing", extDir }, config.ExtensionPaths);
            Assert.Contains("runtime2026", File.ReadAllText(_configPath));
        }

        [Fact]
        public void Env_ReportsVersionAndAttachmentState()
        {
            _cli.Attach("2026");

            Assert.Equal(0, _cli.Env());

            var text = _out.ToString();
            Assert.Contains("2026", text);
            Assert.Contains("attached", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
