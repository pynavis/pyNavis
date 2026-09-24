using System;
using System.Collections.Generic;
using System.IO;
using PyNavis.Runtime.Config;
using Xunit;

namespace PyNavis.Tests
{
    public class PyNavisConfigTests : IDisposable
    {
        private readonly string _dir;

        public PyNavisConfigTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string WriteConfig(string json)
        {
            var p = Path.Combine(_dir, "config.json");
            File.WriteAllText(p, json);
            return p;
        }

        [Fact]
        public void MissingFile_YieldsDefaults()
        {
            var c = PyNavisConfig.Load(Path.Combine(_dir, "nope.json"));
            Assert.Empty(c.ExtensionPaths);
            Assert.False(c.ShortcutsAllowBareKeys);
            Assert.Empty(c.ShortcutBindings);
        }

        [Fact]
        public void Shortcuts_Section_ParsesBindings_NullsAndFlag()
        {
            var c = PyNavisConfig.Load(WriteConfig(@"{
                ""shortcuts"": {
                    ""allowBareKeys"": true,
                    ""bindings"": {
                        ""pyNavis.tab/Memory.panel/02_Recall.pushbutton"": ""Ctrl+Alt+R"",
                        ""pyNavis.tab/Memory.panel/01_Memorize.pushbutton"": null
                    }
                }
            }"));

            Assert.True(c.ShortcutsAllowBareKeys);
            Assert.Equal("Ctrl+Alt+R", c.ShortcutBindings["pyNavis.tab/Memory.panel/02_Recall.pushbutton"]);
            Assert.True(c.ShortcutBindings.ContainsKey("pyNavis.tab/Memory.panel/01_Memorize.pushbutton"));
            Assert.Null(c.ShortcutBindings["pyNavis.tab/Memory.panel/01_Memorize.pushbutton"]);
        }

        [Fact]
        public void Reads_ExtensionsArray()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"extensions\": [\"D:\\\\a\", \"D:\\\\b\"] }"));
            Assert.Equal(new[] { @"D:\a", @"D:\b" }, c.ExtensionPaths);
        }

        [Fact]
        public void UnknownKeys_AreIgnored()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"runtime2026\": \"D:\\\\r\", \"extensions\": [\"D:\\\\a\"] }"));
            Assert.Single(c.ExtensionPaths);
        }

        [Fact]
        public void InvalidJson_YieldsDefaults_DoesNotThrow()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ not valid json"));
            Assert.Empty(c.ExtensionPaths);
        }

        [Fact]
        public void MissingExtensionsKey_YieldsEmptyList()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"runtime2026\": \"D:\\\\r\" }"));
            Assert.Empty(c.ExtensionPaths);
        }

        [Fact]
        public void Reads_PyNavisLibPath()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"pynavislib\": \"D:\\\\repo\\\\pynavislib\" }"));
            Assert.Equal(@"D:\repo\pynavislib", c.PyNavisLibPath);
        }

        [Fact]
        public void MissingPyNavisLibKey_YieldsNull()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"extensions\": [\"D:\\\\a\"] }"));
            Assert.Null(c.PyNavisLibPath);
        }

        [Fact]
        public void NonStringPyNavisLibKey_YieldsNull()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"pynavislib\": [\"D:\\\\a\"] }"));
            Assert.Null(c.PyNavisLibPath);
        }

        [Fact]
        public void Reads_CPythonKey()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"cpython\": \"C:\\\\Py311\\\\python311.dll\" }"));
            Assert.Equal(@"C:\Py311\python311.dll", c.CPythonPath);
        }

        [Fact]
        public void MissingCPythonKey_YieldsNull()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ }"));
            Assert.Null(c.CPythonPath);
        }

        [Fact]
        public void Reads_ThemeKey()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ \"theme\": \"dark\" }"));
            Assert.Equal("dark", c.Theme);
        }

        [Fact]
        public void MissingThemeKey_YieldsNull()
        {
            var c = PyNavisConfig.Load(WriteConfig("{ }"));
            Assert.Null(c.Theme);
        }

        // ---- SaveShortcutBindings: surgical writes, everything else untouched ----

        private static Dictionary<string, string> Bindings(params (string key, string chord)[] pairs)
        {
            var map = new Dictionary<string, string>();
            foreach (var (key, chord) in pairs) map[key] = chord;
            return map;
        }

        [Fact]
        public void Save_PreservesUnknownKeys_AndReplacesOnlyBindings()
        {
            var path = WriteConfig(@"{
                ""runtime2026"": ""D:\\r"",
                ""extensions"": [""D:\\a"", ""D:\\b""],
                ""theme"": ""dark"",
                ""shortcuts"": { ""allowBareKeys"": true, ""bindings"": { ""old/key"": ""Ctrl+Alt+O"" } }
            }");

            PyNavisConfig.SaveShortcutBindings(path, Bindings(("new/key", "Ctrl+Alt+N")));

            var c = PyNavisConfig.Load(path);
            Assert.Equal(new[] { @"D:\a", @"D:\b" }, c.ExtensionPaths);
            Assert.Equal("dark", c.Theme);
            Assert.True(c.ShortcutsAllowBareKeys);
            Assert.Equal("Ctrl+Alt+N", c.ShortcutBindings["new/key"]);
            Assert.False(c.ShortcutBindings.ContainsKey("old/key"));
            Assert.Contains("\"runtime2026\"", File.ReadAllText(path)); // unknown key survives verbatim
        }

        [Fact]
        public void SaveLayout_RoundTrips_AndLeavesOtherDialogsAlone()
        {
            var path = WriteConfig(@"{
                ""theme"": ""dark"",
                ""layout"": { ""other"": { ""tree"": 99 } }
            }");

            var parts = new Dictionary<string, double> { ["tree"] = 240.6, ["panel"] = 300, ["gone"] = 0 };
            PyNavisConfig.SaveLayout(path, "viewpoints", parts);

            var c = PyNavisConfig.Load(path);
            Assert.Equal("dark", c.Theme);
            Assert.Equal(241, c.Layout["viewpoints/tree"]);
            Assert.Equal(300, c.Layout["viewpoints/panel"]);
            Assert.False(c.Layout.ContainsKey("viewpoints/gone"));
            Assert.Equal(99, c.Layout["other/tree"]);
        }

        [Fact]
        public void Load_Layout_IgnoresJunkValues()
        {
            var path = WriteConfig(@"{ ""layout"": { ""viewpoints"": { ""tree"": ""wide"", ""panel"": -5, ""ok"": 12.5 } } }");

            var c = PyNavisConfig.Load(path);
            Assert.Single(c.Layout);
            Assert.Equal(12.5, c.Layout["viewpoints/ok"]);
        }

        [Fact]
        public void Save_NullValue_RoundTripsAsDisable()
        {
            var path = WriteConfig("{ }");

            PyNavisConfig.SaveShortcutBindings(path, Bindings(("some/key", null)));

            var c = PyNavisConfig.Load(path);
            Assert.True(c.ShortcutBindings.ContainsKey("some/key"));
            Assert.Null(c.ShortcutBindings["some/key"]);
        }

        [Fact]
        public void Save_MissingFile_CreatesDirAndMinimalConfig()
        {
            var path = Path.Combine(_dir, "sub", "config.json");

            PyNavisConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K")));

            var c = PyNavisConfig.Load(path);
            Assert.Equal("Ctrl+Alt+K", c.ShortcutBindings["some/key"]);
        }

        [Fact]
        public void Save_EmptyMap_RemovesBindings_AndEmptyShortcutsSection()
        {
            var path = WriteConfig(@"{ ""shortcuts"": { ""bindings"": { ""k"": ""Ctrl+Alt+K"" } }, ""theme"": ""dark"" }");

            PyNavisConfig.SaveShortcutBindings(path, Bindings());

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("shortcuts", text);
            Assert.Equal("dark", PyNavisConfig.Load(path).Theme);
        }

        [Fact]
        public void Save_EmptyMap_KeepsShortcutsSection_WhenAllowBareKeysSet()
        {
            var path = WriteConfig(@"{ ""shortcuts"": { ""allowBareKeys"": true, ""bindings"": { ""k"": ""F5"" } } }");

            PyNavisConfig.SaveShortcutBindings(path, Bindings());

            var c = PyNavisConfig.Load(path);
            Assert.True(c.ShortcutsAllowBareKeys);
            Assert.Empty(c.ShortcutBindings);
        }

        // A save over a file we cannot parse used to start from an empty root, which
        // dropped the extension paths, the for-life pane slots and the runtimeNNNN key
        // the loader boots from. One stray comma must never cost the user all of that.
        [Fact]
        public void SaveShortcutBindings_UnparsableExistingFile_Refuses_AndLeavesFileUntouched()
        {
            var broken = "{ \"runtime2026\": \"C:/rt\", not valid json";
            var path = WriteConfig(broken);

            Assert.Throws<InvalidDataException>(() =>
                PyNavisConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K"))));

            Assert.Equal(broken, File.ReadAllText(path));
        }

        [Fact]
        public void SaveLayout_UnparsableExistingFile_Refuses_AndLeavesFileUntouched()
        {
            var broken = "{ \"runtime2026\": \"C:/rt\", not valid json";
            var path = WriteConfig(broken);

            Assert.Throws<InvalidDataException>(() =>
                PyNavisConfig.SaveLayout(path, "viewpoints", new Dictionary<string, double> { ["tree"] = 200 }));

            Assert.Equal(broken, File.ReadAllText(path));
        }

        [Fact]
        public void SavePaneAssignments_UnparsableExistingFile_Refuses_AndLeavesFileUntouched()
        {
            var broken = "{ \"runtime2026\": \"C:/rt\", not valid json";
            var path = WriteConfig(broken);

            Assert.Throws<InvalidDataException>(() =>
                PyNavisConfig.SavePaneAssignments(path, new Dictionary<string, int> { ["a/b"] = 1 }, 0));

            Assert.Equal(broken, File.ReadAllText(path));
        }

        // A zero-byte file is what a crash mid-write leaves behind: nothing to preserve,
        // so saving over it is safe and is the way back to a working config.
        [Fact]
        public void Save_EmptyExistingFile_StartsFresh()
        {
            var path = WriteConfig("");

            PyNavisConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K")));

            Assert.Equal("Ctrl+Alt+K", PyNavisConfig.Load(path).ShortcutBindings["some/key"]);
        }

        // A locked file (another Navisworks session mid-save, antivirus) is not
        // "unreadable - start fresh": the save must fail and the content must survive.
        [Fact]
        public void Save_LockedExistingFile_Throws_AndLeavesFileUntouched()
        {
            var original = "{ \"runtime2026\": \"C:/rt\", \"extensions\": [\"C:/ext\"] }";
            var path = WriteConfig(original);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() =>
                    PyNavisConfig.SaveLayout(path, "viewpoints", new Dictionary<string, double> { ["tree"] = 200 }));
            }

            Assert.Equal(original, File.ReadAllText(path));
        }

        [Fact]
        public void Save_LeavesNoTempFileBehind()
        {
            var path = WriteConfig("{ \"theme\": \"dark\" }");

            PyNavisConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K")));

            Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
        }
    }
}
