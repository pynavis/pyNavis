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
    }
}
