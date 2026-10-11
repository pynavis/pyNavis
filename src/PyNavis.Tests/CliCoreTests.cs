using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
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

        // A config.json edited by hand and left with a stray comma must not be replaced
        // by one holding only the extension list: the runtime key the loader boots from,
        // the shortcut bindings and the for-life pane slots would all go with it.
        [Fact]
        public void ExtensionsAdd_RefusesToOverwriteAConfigThatIsNotValidJson()
        {
            const string broken = "{ \"runtime2026\": \"D:\\\\r\", \"extensions\": [\"D:\\\\existing\"], }";
            File.WriteAllText(_configPath, broken);
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.NotEqual(0, _cli.ExtensionsAdd(extDir));

            Assert.Equal(broken, File.ReadAllText(_configPath));
            Assert.Contains("nothing was changed", _out.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(_configPath + ".tmp"));
        }

        [Fact]
        public void ExtensionsList_ReportsAConfigThatIsNotValidJson_InsteadOfAnEmptyList()
        {
            File.WriteAllText(_configPath, "{ \"extensions\": [\"D:\\existing\"], }");

            Assert.NotEqual(0, _cli.ExtensionsList());

            Assert.DoesNotContain("No extension roots configured", _out.ToString());
            Assert.Contains(_configPath, _out.ToString());
        }

        [Fact]
        public void ExtensionsAdd_KeepsEveryOtherKey_NestedOnesIncluded()
        {
            File.WriteAllText(_configPath,
                "{ \"runtime2026\": \"D:\\\\r\"," +
                "  \"shortcuts\": { \"bindings\": { \"pyNavis.extension/Clash.tab/Grouper.pushbutton\": \"Ctrl+Shift+G\", \"off\": null } }," +
                "  \"panes\": { \"assignments\": { \"pyNavis.extension/Ask.dockpane\": 2 }, \"extraSlots\": 3 }," +
                "  \"layout\": { \"settings\": { \"width\": 640.5 } }," +
                "  \"telemetry\": { \"enabled\": true }," +
                "  \"extensions\": [] }");
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.Equal(0, _cli.ExtensionsAdd(extDir));

            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(_configPath));
            Assert.Equal(@"D:\r", data["runtime2026"]);
            var bindings = Map(Map(data["shortcuts"])["bindings"]);
            Assert.Equal("Ctrl+Shift+G", bindings["pyNavis.extension/Clash.tab/Grouper.pushbutton"]);
            Assert.True(bindings.ContainsKey("off"));
            Assert.Null(bindings["off"]);
            var panes = Map(data["panes"]);
            Assert.Equal(2, Convert.ToInt32(Map(panes["assignments"])["pyNavis.extension/Ask.dockpane"]));
            Assert.Equal(3, Convert.ToInt32(panes["extraSlots"]));
            Assert.Equal(640.5, Convert.ToDouble(Map(Map(data["layout"])["settings"])["width"]));
            Assert.Equal(true, Map(data["telemetry"])["enabled"]);
            Assert.Equal(new[] { extDir }, ((IEnumerable)data["extensions"]).Cast<string>());
            Assert.False(File.Exists(_configPath + ".tmp"));
        }

        // JavaScriptSerializer stops at 2,097,152 characters by default. A config past
        // that read as empty and was replaced by the extension list alone.
        [Fact]
        public void ExtensionsAdd_ReadsAndKeepsAConfigPastTheSerializersDefaultLength()
        {
            var notes = new string('x', 2200000);
            File.WriteAllText(_configPath,
                "{ \"runtime2026\": \"D:\\\\r\", \"notes\": \"" + notes + "\", \"extensions\": [\"D:\\\\existing\"] }");
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.Equal(0, _cli.ExtensionsAdd(extDir));

            var data = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(_configPath));
            Assert.Equal(@"D:\r", data["runtime2026"]);
            Assert.Equal(notes.Length, ((string)data["notes"]).Length);
            Assert.Equal(new[] { @"D:\existing", extDir }, ((IEnumerable)data["extensions"]).Cast<string>());
        }

        private static Dictionary<string, object> Map(object node) => (Dictionary<string, object>)node;

        [Fact]
        public void Env_ReportsVersionAndAttachmentState()
        {
            _cli.Attach("2026");

            Assert.Equal(0, _cli.Env());

            var text = _out.ToString();
            Assert.Contains("2026", text);
            Assert.Contains("attached", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NewestAttachedYear_PicksTheHighestAttachedYear()
        {
            foreach (var year in new[] { "2025", "2026" })
            {
                var dir = Path.Combine(_bundleRoot, "Contents", year);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "PyNavis.dll"), "fake loader");
            }
            Directory.CreateDirectory(Path.Combine(_bundleRoot, "Contents", "2027")); // no loader inside

            Assert.Equal("2026", _cli.NewestAttachedYear());
            Assert.True(_cli.IsAttached("2025"));
            Assert.False(_cli.IsAttached("2027"));
        }

        [Fact]
        public void NewestAttachedYear_SkipsAYearWhoseNavisworksIsNotInstalled()
        {
            // A loader left in the bundle for a year with no Navisworks on this PC (an
            // API-only reference drop) must not become the default pynavis run picks.
            foreach (var year in new[] { "2026", "2027" })
            {
                var dir = Path.Combine(_bundleRoot, "Contents", year);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "PyNavis.dll"), "fake loader");
            }

            Assert.Equal("2026", _cli.NewestAttachedYear()); // the resolver knows only 2026
        }

        [Fact]
        public void NewestAttachedYear_IsNull_WhenNothingIsAttached()
        {
            Assert.Null(_cli.NewestAttachedYear());
            Assert.False(_cli.IsAttached("2026"));
        }
    }
}
