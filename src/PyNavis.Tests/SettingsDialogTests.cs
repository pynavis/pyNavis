using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Settings window and the writer behind it. The writer matters most: it
    /// rewrites the user's real config.json, and the file also holds three sections
    /// the runtime owns (shortcuts.bindings, panes.assignments, layout) which this
    /// must leave exactly as it found them.
    /// </summary>
    public class SettingsDialogTests : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), "pynavis-settings-" + Guid.NewGuid() + ".json");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private void Given(string json) => File.WriteAllText(_path, json);
        private string Written() => File.ReadAllText(_path);

        private static PyNavisConfig.UserSettings Defaults() => new PyNavisConfig.UserSettings();

        // ---- what the writer must not disturb --------------------------------

        [Fact]
        public void Saving_LeavesTheSectionsTheRuntimeOwns_ExactlyAsTheyWere()
        {
            // The Shortcuts editor owns bindings, the pane registry owns assignments,
            // and each dialog owns its own remembered width. A settings window that
            // rewrote them would fight their owners for the file.
            Given("{\"shortcuts\": {\"bindings\": {\"a/b.pushbutton\": \"Ctrl+Shift+M\"}},"
                + " \"panes\": {\"assignments\": {\"a/b.dockpane\": 2}, \"extraSlots\": 3},"
                + " \"layout\": {\"viewpoints\": {\"tree\": 363}}}");

            PyNavisConfig.SaveUserSettings(_path, Defaults());

            var after = PyNavisConfig.Load(_path);
            Assert.Equal("Ctrl+Shift+M", after.ShortcutBindings["a/b.pushbutton"]);
            Assert.Equal(2, after.PaneAssignments["a/b.dockpane"]);
            Assert.Equal(3, after.ExtraPaneSlots);
            Assert.Equal(363, after.Layout["viewpoints/tree"]);
        }

        [Fact]
        public void Saving_KeepsAKeyItHasNeverHeardOf()
        {
            // A newer pyNavis writing a key this build does not know must not have it
            // silently deleted by an older Settings window.
            Given("{\"somethingFuture\": {\"nested\": 1}}");

            PyNavisConfig.SaveUserSettings(_path, Defaults());

            Assert.Contains("somethingFuture", Written());
        }

        // ---- defaults stay absent --------------------------------------------

        [Fact]
        public void SavingTheDefaults_WritesNeitherRibbonKey_SoAnUntouchedFileStaysClean()
        {
            Given("{}");

            PyNavisConfig.SaveUserSettings(_path, Defaults());

            var text = Written();
            Assert.DoesNotContain("configMarker", text);
            Assert.DoesNotContain("shortcutMarker", text);
            Assert.DoesNotContain("theme", text);
        }

        [Fact]
        public void TurningARibbonHintBackToItsDefault_RemovesTheKeyAgain()
        {
            Given("{\"ribbon\": {\"configMarker\": \"\", \"shortcutMarker\": \"*\"}}");

            PyNavisConfig.SaveUserSettings(_path, Defaults());

            Assert.DoesNotContain("configMarker", Written());
            Assert.DoesNotContain("ribbon", Written());
        }

        [Fact]
        public void TheOldConfigDotBoolean_IsDroppedOnSave_AndReadAsOffUntilThen()
        {
            Given("{\"ribbon\": {\"configDot\": false}}");
            Assert.Equal("", PyNavisConfig.Load(_path).RibbonConfigMarker);

            PyNavisConfig.SaveUserSettings(_path, PyNavisConfig.Load(_path).ToUserSettings());

            Assert.DoesNotContain("configDot", Written());
            Assert.Equal("", PyNavisConfig.Load(_path).RibbonConfigMarker);
        }

        // ---- non-defaults are written ----------------------------------------

        [Fact]
        public void EverySettingRoundTrips_ThroughTheFileAndBack()
        {
            Given("{}");
            var settings = new PyNavisConfig.UserSettings
            {
                Theme = "dark",
                RibbonConfigMarker = "S",
                RibbonShortcutMarker = "*",
                ShortcutsAllowBareKeys = true,
                ExtensionPaths = new List<string> { @"C:\one", @"D:\two" },
                PyNavisLibPath = @"C:\lib",
                CPythonPath = @"C:\py\python.exe",
            };

            PyNavisConfig.SaveUserSettings(_path, settings);
            var back = PyNavisConfig.Load(_path).ToUserSettings();

            Assert.Equal("dark", back.Theme);
            Assert.Equal("S", back.RibbonConfigMarker);
            Assert.Equal("*", back.RibbonShortcutMarker);
            Assert.True(back.ShortcutsAllowBareKeys);
            Assert.Equal(new[] { @"C:\one", @"D:\two" }, back.ExtensionPaths);
            Assert.Equal(@"C:\lib", back.PyNavisLibPath);
            Assert.Equal(@"C:\py\python.exe", back.CPythonPath);
        }

        [Fact]
        public void AnEmptyMarker_IsWritten_BecauseEmptyIsHowItIsTurnedOff()
        {
            Given("{}");
            var settings = Defaults();
            settings.RibbonShortcutMarker = "";

            PyNavisConfig.SaveUserSettings(_path, settings);

            Assert.Equal("", PyNavisConfig.Load(_path).RibbonShortcutMarker);
        }

        [Fact]
        public void ClearingAPath_RemovesTheKey_RatherThanWritingItBlank()
        {
            // Absent is what a fresh install looks like and is what makes the runtime
            // resolve the path itself. An empty string would be a path of "".
            Given("{\"pynavislib\": \"C:\\\\old\", \"cpython\": \"C:\\\\old\\\\py.exe\","
                + " \"theme\": \"dark\"}");
            var settings = Defaults();
            settings.PyNavisLibPath = "   ";
            settings.CPythonPath = null;
            settings.Theme = null;

            PyNavisConfig.SaveUserSettings(_path, settings);

            var text = Written();
            Assert.DoesNotContain("pynavislib", text);
            Assert.DoesNotContain("cpython", text);
            Assert.DoesNotContain("theme", text);
        }

        [Fact]
        public void ExtensionRoots_AreTrimmed_AndBlanksDropped()
        {
            Given("{}");
            var settings = Defaults();
            settings.ExtensionPaths = new List<string> { "  C:\\one  ", "", "   ", "D:\\two" };

            PyNavisConfig.SaveUserSettings(_path, settings);

            Assert.Equal(new[] { @"C:\one", @"D:\two" },
                PyNavisConfig.Load(_path).ExtensionPaths);
        }

        [Fact]
        public void NoExtensionRootsAtAll_RemovesTheKey()
        {
            Given("{\"extensions\": [\"C:\\\\one\"]}");
            var settings = Defaults();
            settings.ExtensionPaths = new List<string>();

            PyNavisConfig.SaveUserSettings(_path, settings);

            Assert.DoesNotContain("extensions", Written());
        }

        [Fact]
        public void SavingNothing_Throws_RatherThanBlankingTheFile()
        {
            Given("{\"theme\": \"dark\"}");

            Assert.Throws<ArgumentNullException>(
                () => PyNavisConfig.SaveUserSettings(_path, null));
            Assert.Contains("dark", Written());
        }

        // ---- the window itself ------------------------------------------------

        [Fact]
        public void TheWindow_ReadsBackWhatItWasGiven()
        {
            var given = new PyNavisConfig.UserSettings
            {
                Theme = "light",
                RibbonConfigMarker = "",
                RibbonShortcutMarker = "+",
                ShortcutsAllowBareKeys = true,
                ExtensionPaths = new List<string> { @"C:\one" },
                PyNavisLibPath = @"C:\lib",
                CPythonPath = @"C:\py.exe",
            };

            OnSta(() =>
            {
                var window = SettingsDialog.Build(given);
                var back = SettingsDialog.CollectForTest(window);

                Assert.Equal("light", back.Theme);
                Assert.Equal("", back.RibbonConfigMarker);
                Assert.Equal("+", back.RibbonShortcutMarker);
                Assert.True(back.ShortcutsAllowBareKeys);
                Assert.Equal(new[] { @"C:\one" }, back.ExtensionPaths);
                Assert.Equal(@"C:\lib", back.PyNavisLibPath);
                Assert.Equal(@"C:\py.exe", back.CPythonPath);
            });
        }

        [Fact]
        public void FollowNavisworks_CollectsAsNoTheme_NotAsTheWordFollow()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings { Theme = "dark" });
                SettingsDialog.SetThemeForTest(window, "Follow Navisworks");

                Assert.Null(SettingsDialog.CollectForTest(window).Theme);
            });
        }

        [Fact]
        public void ANullSettings_StillBuilds_SoAMissingConfigFileOpensTheWindow()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(null);
                var back = SettingsDialog.CollectForTest(window);

                Assert.Null(back.Theme);
                Assert.Equal(PyNavisConfig.DefaultConfigMarker, back.RibbonConfigMarker);
                Assert.Equal(0, SettingsDialog.RootCountOf(window));
            });
        }

        [Fact]
        public void EditsInTheWindow_ReachTheCollectedSettings()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings());
                SettingsDialog.SetConfigMarkerForTest(window, "");
                SettingsDialog.SetMarkerForTest(window, "");
                SettingsDialog.AddRootForTest(window, @"D:\added");

                var back = SettingsDialog.CollectForTest(window);
                Assert.Equal("", back.RibbonConfigMarker);
                Assert.Equal("", back.RibbonShortcutMarker);
                Assert.Equal(new[] { @"D:\added" }, back.ExtensionPaths);
            });
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
