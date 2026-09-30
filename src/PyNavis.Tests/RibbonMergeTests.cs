using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Tabs and panels with the same name, from different extensions, become one
    /// ribbon tab and one panel: how a user's own extension adds buttons to the
    /// pyNavis tab, or to one of its panels, without touching pyNavis.extension
    /// (which every update replaces).
    /// </summary>
    public class RibbonMergeTests
    {
        private static PushButtonModel Button(string title) => new PushButtonModel { Title = title };

        private static PanelModel Panel(string title, params string[] buttons)
        {
            var panel = new PanelModel { Title = title };
            foreach (var b in buttons) panel.Items.Add(Button(b));
            return panel;
        }

        private static ExtensionModel Extension(string name, string tab, params PanelModel[] panels)
        {
            var ext = new ExtensionModel { Name = name };
            var model = new TabModel { Title = tab, Id = "PYNAVIS_TAB_" + name.Replace(' ', '_') + "_" + tab };
            model.Panels.AddRange(panels);
            ext.Tabs.Add(model);
            return ext;
        }

        private static string[] Titles(PanelModel panel) =>
            panel.Items.Select(i => ((CaptionedPanelItem)i).Title).ToArray();

        [Fact]
        public void SameTabAndPanel_BecomeOne_WithTheShippedItemsFirst()
        {
            // "My pyNavis" sorts before "pyNavis" in a folder, but the shipped
            // extension still sets the order of what it shares.
            var mine = Extension("My pyNavis", "pyNavis", Panel("Clash", "Mine C"), Panel("Mine", "Mine D"));
            var shipped = Extension("pyNavis", "pyNavis", Panel("Clash", "A", "B"), Panel("Data", "X"));

            var tabs = RibbonMerge.Tabs(new[] { mine, shipped });

            var tab = Assert.Single(tabs);
            Assert.Equal("PYNAVIS_TAB_pyNavis_pyNavis", tab.Id);
            Assert.Equal(new[] { "Clash", "Data", "Mine" }, tab.Panels.Select(p => p.Title).ToArray());
            Assert.Equal(new[] { "A", "B", "Mine C" }, Titles(tab.Panels[0]));
            Assert.Equal(new[] { "X" }, Titles(tab.Panels[1]));
            Assert.Equal(new[] { "Mine D" }, Titles(tab.Panels[2]));
        }

        [Fact]
        public void TabAndPanelNames_MatchWhateverTheCase_AndSlideoutsJoinToo()
        {
            var shipped = Extension("pyNavis", "pyNavis", Panel("Clash", "A"));
            shipped.Tabs[0].Panels[0].Slideout.Add(Button("S1"));
            var mine = Extension("Mine", "PYNAVIS", Panel("clash", "B"));
            mine.Tabs[0].Panels[0].Slideout.Add(Button("S2"));

            var tab = Assert.Single(RibbonMerge.Tabs(new[] { shipped, mine }));
            var panel = Assert.Single(tab.Panels);
            Assert.Equal(new[] { "A", "B" }, Titles(panel));
            Assert.Equal(new[] { "S1", "S2" },
                panel.Slideout.Select(i => ((CaptionedPanelItem)i).Title).ToArray());
        }

        [Fact]
        public void DifferentTabs_StayApart_InLoadOrder()
        {
            // Merging never reorders the ribbon: a tab sits where its first
            // extension in load order puts it.
            var ai = Extension("AI", "AI Tools", Panel("Made", "T"));
            var shipped = Extension("pyNavis", "pyNavis", Panel("Clash", "A"));

            var tabs = RibbonMerge.Tabs(new[] { ai, shipped });
            Assert.Equal(new[] { "AI Tools", "pyNavis" }, tabs.Select(t => t.Title).ToArray());
        }

        [Fact]
        public void TheSameExtensionTwice_IsLoadedOnce_AndSaysSo()
        {
            // Two roots holding pyNavis.extension used to be caught by the tab id;
            // with merging it must be caught first, or every button would double.
            var first = Extension("pyNavis", "pyNavis", Panel("Clash", "A"));
            var second = Extension("pyNavis", "pyNavis", Panel("Clash", "A"));
            var notes = new List<string>();

            var tab = Assert.Single(RibbonMerge.Tabs(new[] { first, second }, notes.Add));
            Assert.Equal(new[] { "A" }, Titles(Assert.Single(tab.Panels)));
            Assert.Contains(notes, n => n.Contains("pyNavis") && n.Contains("skipped"));
        }

        [Fact]
        public void TheExtensionsThemselves_AreLeftAsTheyWere()
        {
            var shipped = Extension("pyNavis", "pyNavis", Panel("Clash", "A"));
            var mine = Extension("Mine", "pyNavis", Panel("Clash", "B"));

            RibbonMerge.Tabs(new[] { shipped, mine });

            Assert.Equal(new[] { "A" }, Titles(shipped.Tabs[0].Panels[0]));
            Assert.Equal(new[] { "B" }, Titles(mine.Tabs[0].Panels[0]));
        }
    }
}
