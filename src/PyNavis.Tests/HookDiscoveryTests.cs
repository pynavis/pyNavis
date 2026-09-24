using System;
using System.IO;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class HookDiscoveryTests : IDisposable
    {
        private readonly string _root;

        public HookDiscoveryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string ExtDir()
        {
            var dir = Path.Combine(_root, "MyExt.extension");
            Directory.CreateDirectory(dir);
            // An extension needs at least one button to be interesting, but hooks
            // must parse even for a UI-less extension folder.
            return dir;
        }

        private void MakeHook(string extDir, string name)
        {
            var hooks = Path.Combine(extDir, "hooks");
            Directory.CreateDirectory(hooks);
            File.WriteAllText(Path.Combine(hooks, name), "pass\n");
        }

        [Fact]
        public void Valid_Hook_Files_Are_Discovered()
        {
            var ext = ExtDir();
            MakeHook(ext, "doc-opened.py");
            MakeHook(ext, "selection-changed.py");
            var model = BundleParser.ParseExtension(ext);
            Assert.Equal(2, model.Hooks.Count);
            Assert.Contains(model.Hooks, h => h.EventName == "doc-opened");
            Assert.All(model.Hooks, h => Assert.Equal("MyExt", h.ExtensionName));
            Assert.All(model.Hooks, h => Assert.Equal("ironpython", h.EngineId));
        }

        [Fact]
        public void Unknown_Event_Name_Is_Skipped()
        {
            var ext = ExtDir();
            MakeHook(ext, "doc-exploded.py");
            Assert.Empty(BundleParser.ParseExtension(ext).Hooks);
        }

        [Fact]
        public void No_Hooks_Folder_Means_No_Hooks()
        {
            Assert.Empty(BundleParser.ParseExtension(ExtDir()).Hooks);
        }

        [Fact]
        public void Hook_Id_Is_Extension_Colon_Event()
        {
            var ext = ExtDir();
            MakeHook(ext, "doc-saved.py");
            Assert.Equal("MyExt:doc-saved", BundleParser.ParseExtension(ext).Hooks[0].Id);
        }
    }
}
