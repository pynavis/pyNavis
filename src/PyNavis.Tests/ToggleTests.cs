using System.IO;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Ribbon;
using Xunit;

namespace PyNavis.Tests
{
    public class ToggleTests
    {
        [Fact]
        public void Store_Defaults_Off_And_Remembers()
        {
            Assert.False(ToggleStateStore.Get("x/y.toggle"));
            ToggleStateStore.Set("x/y.toggle", true);
            Assert.True(ToggleStateStore.Get("x/y.toggle"));
            ToggleStateStore.Set("x/y.toggle", false);
            Assert.False(ToggleStateStore.Get("x/y.toggle"));
        }

        [Fact]
        public void Store_Raises_Changed_With_The_Key()
        {
            string seen = null;
            System.Action<string> handler = key => seen = key;
            ToggleStateStore.Changed += handler;
            try { ToggleStateStore.Set("a/b.toggle", true); }
            finally { ToggleStateStore.Changed -= handler; }
            Assert.Equal("a/b.toggle", seen);
        }
    }

    public class ToggleParsingTests : System.IDisposable
    {
        private readonly string _root;

        public ToggleParsingTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void Toggle_Bundle_Parses_With_OnOff_Icons()
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", "B.toggle");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "script.py"), "pass\n");
            File.WriteAllBytes(Path.Combine(dir, "icon.on.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(dir, "icon.off.png"), new byte[] { 2 });

            var ext = BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));
            var button = (PushButtonModel)ext.Tabs[0].Panels[0].Items[0];
            Assert.True(button.IsToggle);
            Assert.EndsWith("icon.on.png", button.OnIconPath);
            Assert.EndsWith("icon.off.png", button.OffIconPath);
            Assert.Equal(button.OffIconPath, button.IconPath); // renders off initially
        }

        [Fact]
        public void Toggle_Without_Variant_Icons_Falls_Back_To_Plain_Icon()
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", "B.toggle");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "script.py"), "pass\n");
            File.WriteAllBytes(Path.Combine(dir, "icon.png"), new byte[] { 1 });

            var ext = BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));
            var button = (PushButtonModel)ext.Tabs[0].Panels[0].Items[0];
            Assert.True(button.IsToggle);
            Assert.EndsWith("icon.png", button.OnIconPath);
            Assert.EndsWith("icon.png", button.OffIconPath);
        }

        private PushButtonModel Parse(string folder, string yaml)
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "script.py"), "pass\n");
            File.WriteAllText(Path.Combine(dir, "bundle.yaml"), yaml);
            var ext = BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));
            return (PushButtonModel)ext.Tabs[0].Panels[0].Items[0];
        }

        [Fact]
        public void Toggle_Reads_Its_On_State_Title_And_Tooltip()
        {
            // A toggle whose caption names the action ("Hide Tabs") reads wrong
            // once pressed; title_on names the action a click takes then.
            var button = Parse("B.toggle",
                "title: Hide\\nTabs\ntitle_on: Show\\nTabs\ntooltip: Takes them off\ntooltip_on: Puts them back\n");
            Assert.Equal("Show Tabs", button.TitleOn);
            Assert.Equal("Show\nTabs", button.RibbonTitleOn);
            Assert.Equal("Puts them back", button.TooltipOn);
        }

        [Fact]
        public void Toggle_Without_On_State_Keys_Keeps_One_Title_And_A_Pushbutton_Ignores_Them()
        {
            Assert.Null(Parse("B.toggle", "title: Hide\n").TitleOn);
            var plain = Parse("C.pushbutton", "title: Hide\ntitle_on: Show\ntooltip_on: Back\n");
            Assert.Null(plain.TitleOn);
            Assert.Null(plain.TooltipOn);
        }
    }

    /// <summary>
    /// What a pyNavis button's caption and tooltip say, in either toggle state:
    /// the one place the ribbon build and a toggle's state change both read, so
    /// a Reload never draws a pressed toggle under its resting name.
    /// </summary>
    public class ButtonCaptionTests : System.IDisposable
    {
        public ButtonCaptionTests() => RibbonMarkers.ResetForTests();

        public void Dispose() => RibbonMarkers.ResetForTests();

        private static PushButtonModel Toggle(bool withOnKeys = true, bool withConfig = false) =>
            new PushButtonModel
            {
                Title = @"Hide\nTabs",
                Tooltip = "Takes them off",
                IsToggle = true,
                TitleOn = withOnKeys ? @"Show\nTabs" : null,
                TooltipOn = withOnKeys ? "Puts them back" : null,
                ConfigScriptPath = withConfig ? "config.py" : null,
            };

        [Fact]
        public void Text_Follows_The_State_Large_And_Small()
        {
            var t = Toggle();
            Assert.Equal("Hide\nTabs", ButtonCaption.Text(t, small: false, on: false, hasChord: false));
            Assert.Equal("Show\nTabs", ButtonCaption.Text(t, small: false, on: true, hasChord: false));
            Assert.Equal("Show Tabs", ButtonCaption.Text(t, small: true, on: true, hasChord: false));
            // no title_on: one caption for both states, as before
            Assert.Equal("Hide\nTabs", ButtonCaption.Text(Toggle(withOnKeys: false), false, true, false));
        }

        [Fact]
        public void Text_Carries_The_Markers_In_Either_State()
        {
            var t = Toggle(withConfig: true);
            Assert.Equal(RibbonMarkers.WithShortcutMarker(RibbonMarkers.WithConfigMarker("Show\nTabs")),
                ButtonCaption.Text(t, small: false, on: true, hasChord: true));
            Assert.Equal(RibbonMarkers.WithConfigMarker("Hide\nTabs"),
                ButtonCaption.Text(t, small: false, on: false, hasChord: false));
        }

        [Fact]
        public void Tooltip_Follows_The_State_And_Names_The_Chord()
        {
            var t = Toggle();
            Assert.Equal("Takes them off", ButtonCaption.Tooltip(t, on: false, chord: null));
            Assert.Equal("Puts them back (Ctrl+H)", ButtonCaption.Tooltip(t, on: true, chord: "Ctrl+H"));
            Assert.Equal("Takes them off", ButtonCaption.Tooltip(Toggle(withOnKeys: false), on: true, chord: null));
            Assert.Equal("(Ctrl+H)", ButtonCaption.Tooltip(new PushButtonModel { Title = "X" }, false, "Ctrl+H"));
            Assert.Null(ButtonCaption.Tooltip(new PushButtonModel { Title = "X" }, false, null));
        }
    }
}
