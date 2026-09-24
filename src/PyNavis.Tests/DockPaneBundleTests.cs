using System;
using System.IO;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class DockPaneBundleTests : IDisposable
    {
        private readonly string _root;

        public DockPaneBundleTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakePane(string name = "Clash_Nav", string yaml = null,
                                bool xaml = true, bool script = false)
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", name + ".dockpane");
            Directory.CreateDirectory(dir);
            if (xaml) File.WriteAllText(Path.Combine(dir, "pane.xaml"),
                "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
            if (script) File.WriteAllText(Path.Combine(dir, "script.py"), "pass\n");
            if (yaml != null) File.WriteAllText(Path.Combine(dir, "bundle.yaml"), yaml);
            return dir;
        }

        private ExtensionModel Parse() =>
            BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));

        [Fact]
        public void DockPane_Parses_With_Title_From_Folder_Name()
        {
            MakePane();
            var pane = Assert.IsType<DockPaneModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.Equal("Clash Nav", pane.Title);
            Assert.EndsWith("pane.xaml", pane.XamlPath);
            Assert.Null(pane.ScriptPath);
        }

        [Fact]
        public void DockPane_Without_Xaml_Is_Skipped()
        {
            MakePane(xaml: false);
            Assert.Empty(Parse().Tabs);
        }

        [Fact]
        public void DockPane_Reads_Yaml_Overrides()
        {
            MakePane(yaml: "title: Clash navigator\ntooltip: Browse clashes\n" +
                           "shortcut: Ctrl+Shift+N\nkeytip: CN\n",
                     script: true);
            var pane = Assert.IsType<DockPaneModel>(Parse().Tabs[0].Panels[0].Items[0]);
            Assert.Equal("Clash navigator", pane.Title);
            Assert.Equal("Browse clashes", pane.Tooltip);
            Assert.Equal("Ctrl+Shift+N", pane.Shortcut);
            Assert.Equal("CN", pane.KeyTipOverride);
            Assert.EndsWith("script.py", pane.ScriptPath);
        }

        [Fact]
        public void DockPane_BundleKey_Is_Extension_Relative_With_Forward_Slashes()
        {
            MakePane();
            var pane = (DockPaneModel)Parse().Tabs[0].Panels[0].Items[0];
            Assert.Equal("T.tab/P.panel/Clash_Nav.dockpane", pane.BundleKey);
        }

        [Fact]
        public void DockPane_SearchPaths_Start_At_The_Bundle_Folder()
        {
            var dir = MakePane(script: true);
            var pane = (DockPaneModel)Parse().Tabs[0].Panels[0].Items[0];
            Assert.Equal(dir, pane.SearchPaths[0]);
        }
    }
}
