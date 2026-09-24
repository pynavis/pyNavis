using System;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The shortcuts editor's pure state: rows from parsed bundles plus config
    /// overrides, edit operations, first-wins conflict flags, and the minimal
    /// override map that gets persisted. No WPF anywhere.
    /// </summary>
    public class ShortcutsModelTests
    {
        private static ExtensionModel Ext(params (string panel, string key, string title, string chord)[] buttons)
        {
            var ext = new ExtensionModel { Name = "T", Directory = @"C:\x" };
            var tab = new TabModel { Title = "T", Id = "T" };
            ext.Tabs.Add(tab);
            foreach (var group in buttons.GroupBy(b => b.panel))
            {
                var panel = new PanelModel { Title = group.Key };
                foreach (var b in group)
                    panel.Items.Add(new PushButtonModel
                    {
                        BundleKey = b.key,
                        Title = b.title,
                        Shortcut = b.chord,
                        ScriptPath = b.key,
                    });
                tab.Panels.Add(panel);
            }
            return ext;
        }

        private static PyNavisConfig Config(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), "pynavis-test-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            try { return PyNavisConfig.Load(path); }
            finally { File.Delete(path); }
        }

        private static PyNavisConfig EmptyConfig() => PyNavisConfig.Load(null);

        private static ShortcutsModel TwoPanelModel(PyNavisConfig config = null) => ShortcutsModel.Build(
            new[]
            {
                Ext(("General", "t/g/A.pushbutton", "Alpha", "Ctrl+Shift+A"),
                    ("General", "t/g/B.pushbutton", "Beta", null),
                    ("Memory", "t/m/C.pushbutton", "Gamma", "Ctrl+Shift+G")),
            },
            config ?? EmptyConfig());

        [Fact]
        public void Rows_KeepRibbonOrder_AndPanelTitles()
        {
            var rows = TwoPanelModel().Rows;

            Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, rows.Select(r => r.Title));
            Assert.Equal(new[] { "General", "General", "Memory" }, rows.Select(r => r.PanelTitle));
            Assert.Equal("Ctrl+Shift+A", rows[0].DefaultChord);
            Assert.Equal("Ctrl+Shift+A", rows[0].Current);
            Assert.Null(rows[1].DefaultChord);
            Assert.Null(rows[1].Current);
        }

        [Fact]
        public void ConfigOverride_ShowsAsCurrent_AndCustom()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/g/A.pushbutton\": \"Ctrl+Alt+Q\"}}}"));

            var row = model.Rows[0];
            Assert.Equal("Ctrl+Alt+Q", row.Current);
            Assert.True(row.IsCustom);
            Assert.False(model.IsDirty); // loaded state, nothing edited yet
        }

        [Fact]
        public void EquivalentOverrideSpelling_IsNotCustom()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/g/A.pushbutton\": \"ctrl+shift+a\"}}}"));

            Assert.False(model.Rows[0].IsCustom);
        }

        [Fact]
        public void NullOverride_ShowsDisabled()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/g/A.pushbutton\": null}}}"));

            var row = model.Rows[0];
            Assert.Null(row.Current);
            Assert.True(row.IsCustom);
        }

        [Fact]
        public void TrySet_BadChord_ReturnsError_AndChangesNothing()
        {
            var model = TwoPanelModel();

            var error = model.TrySet("t/g/A.pushbutton", "Q");

            Assert.Contains("Ctrl or Alt", error);
            Assert.Equal("Ctrl+Shift+A", model.Rows[0].Current);
            Assert.False(model.IsDirty);
        }

        [Fact]
        public void TrySet_DuplicateChord_FlagsTheLaterRow()
        {
            var model = TwoPanelModel();

            Assert.Null(model.TrySet("t/m/C.pushbutton", "Ctrl+Shift+A"));

            Assert.Null(model.Rows[0].ConflictWith);          // first wins
            Assert.Equal("Alpha", model.Rows[2].ConflictWith); // later row loses
            Assert.True(model.HasConflicts);
        }

        [Fact]
        public void Disable_WritesNull_OnlyWhenADefaultExists()
        {
            var model = TwoPanelModel();

            model.Disable("t/g/A.pushbutton"); // has default -> null entry
            model.Disable("t/g/B.pushbutton"); // never had one -> no entry

            var bindings = model.ToBindings();
            Assert.True(bindings.ContainsKey("t/g/A.pushbutton"));
            Assert.Null(bindings["t/g/A.pushbutton"]);
            Assert.False(bindings.ContainsKey("t/g/B.pushbutton"));
        }

        [Fact]
        public void ToBindings_IsMinimal()
        {
            var model = TwoPanelModel();

            Assert.Null(model.TrySet("t/g/B.pushbutton", "Ctrl+Alt+B"));

            var bindings = model.ToBindings();
            Assert.Equal(new[] { "t/g/B.pushbutton" }, bindings.Keys); // rows at default omitted
            Assert.Equal("Ctrl+Alt+B", bindings["t/g/B.pushbutton"]);
        }

        [Fact]
        public void SettingTheDefaultSpelling_RemovesTheOverride()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/g/A.pushbutton\": \"Ctrl+Alt+Q\"}}}"));

            Assert.Null(model.TrySet("t/g/A.pushbutton", "ctrl+shift+a"));

            Assert.Empty(model.ToBindings());
            Assert.True(model.IsDirty);
        }

        [Fact]
        public void ResetToDefault_RestoresTheAuthorChord()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/g/A.pushbutton\": null}}}"));

            model.ResetToDefault("t/g/A.pushbutton");

            Assert.Equal("Ctrl+Shift+A", model.Rows[0].Current);
            Assert.Empty(model.ToBindings());
            Assert.True(model.IsDirty);
        }

        [Fact]
        public void ResetAll_ClearsEveryOverride()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/g/A.pushbutton\": \"Ctrl+Alt+Q\", \"t/g/B.pushbutton\": \"Ctrl+Alt+B\"}}}"));

            model.ResetAll();

            Assert.Empty(model.ToBindings());
            Assert.Equal("Ctrl+Shift+A", model.Rows[0].Current);
            Assert.Null(model.Rows[1].Current);
            Assert.True(model.IsDirty);
        }

        [Fact]
        public void StaleOverride_ForUnknownTool_IsDropped()
        {
            var model = TwoPanelModel(Config(
                "{\"shortcuts\": {\"bindings\": {\"t/gone/X.pushbutton\": \"Ctrl+Alt+X\"}}}"));

            Assert.Empty(model.ToBindings());
        }

        [Fact]
        public void AllowBareKeys_FlowsThroughToValidation()
        {
            var model = ShortcutsModel.Build(
                new[] { Ext(("General", "t/g/A.pushbutton", "Alpha", null)) },
                Config("{\"shortcuts\": {\"allowBareKeys\": true}}"));

            Assert.Null(model.TrySet("t/g/A.pushbutton", "F5"));
            Assert.Equal("F5", model.Rows[0].Current);
        }
    }
}
