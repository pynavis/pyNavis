using System;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class NewButtonTypeTests : IDisposable
    {
        private readonly string _root;

        public NewButtonTypeTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeBundle(string suffix, string yaml = null, bool script = false)
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", "B" + suffix);
            Directory.CreateDirectory(dir);
            if (script) File.WriteAllText(Path.Combine(dir, "script.py"), "pass\n");
            if (yaml != null) File.WriteAllText(Path.Combine(dir, "bundle.yaml"), yaml);
            return dir;
        }

        private ExtensionModel Parse() =>
            BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));

        [Fact]
        public void NoButton_Parses_With_NoUi_And_Joins_Shortcut_Targets()
        {
            MakeBundle(".nobutton", script: true);
            var panel = Parse().Tabs[0].Panels[0];
            var button = Assert.IsType<PushButtonModel>(panel.Items[0]);
            Assert.True(button.NoUi);
            Assert.Contains(panel.Buttons, b => b.NoUi); // chords/editor see it
        }

        [Fact]
        public void UrlButton_Requires_Url()
        {
            MakeBundle(".urlbutton", yaml: "url: https://example.com/docs\ntitle: Docs\n");
            var item = Assert.IsType<UrlButtonModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.Equal("https://example.com/docs", item.Url);
            Assert.Equal("Docs", item.Title);
        }

        [Fact]
        public void UrlButton_Without_Url_Is_Skipped()
        {
            MakeBundle(".urlbutton", yaml: "title: Broken\n");
            Assert.Empty(Parse().Tabs);
        }

        [Fact]
        public void LinkButton_Requires_Plugin()
        {
            MakeBundle(".linkbutton", yaml: "plugin: MyPlugin.Vendor\n");
            var item = Assert.IsType<LinkButtonModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.Equal("MyPlugin.Vendor", item.PluginId);
        }

        [Fact]
        public void LinkButton_Without_Plugin_Is_Skipped()
        {
            MakeBundle(".linkbutton");
            Assert.Empty(Parse().Tabs);
        }

        [Fact]
        public void Titles_Fall_Back_To_Folder_Name()
        {
            MakeBundle(".urlbutton", yaml: "url: https://example.com\n");
            Assert.Equal("B", ((UrlButtonModel)Parse().Tabs[0].Panels[0].Items[0]).Title);
        }

        private string MakeChild(string parentSuffix, string child)
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel",
                "S" + parentSuffix, child + ".pushbutton");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "script.py"), "pass\n");
            return dir;
        }

        [Fact]
        public void SplitButton_Parses_As_SplitLastUsed()
        {
            MakeChild(".splitbutton", "A");
            MakeChild(".splitbutton", "B");
            var pulldown = Assert.IsType<PulldownModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.Equal(PulldownKind.SplitLastUsed, pulldown.Kind);
            Assert.Equal(2, pulldown.Buttons.Count);
            Assert.Equal("S", pulldown.Title);
        }

        [Fact]
        public void SplitPushButton_Parses_As_SplitFixed()
        {
            MakeChild(".splitpushbutton", "A");
            var pulldown = Assert.IsType<PulldownModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.Equal(PulldownKind.SplitFixed, pulldown.Kind);
        }

        [Fact]
        public void Plain_Pulldown_Stays_Menu()
        {
            MakeChild(".pulldown", "A");
            Assert.Equal(PulldownKind.Menu,
                ((PulldownModel)Parse().Tabs[0].Panels[0].Items[0]).Kind);
        }

        [Fact]
        public void SmartButton_Parses_As_Pushbutton_With_IsSmart()
        {
            MakeBundle(".smartbutton", script: true);
            var button = Assert.IsType<PushButtonModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.True(button.IsSmart);
            Assert.False(button.NoUi);
        }
    }
}
