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

        // A hooks folder that cannot be listed (no permission, a broken reparse point, a
        // cloud placeholder that went offline) used to throw out of ParseExtension, and
        // the whole extension root was skipped: no ribbon. It must cost only the hooks,
        // and say so.
        [Fact]
        public void An_Unlistable_Hooks_Folder_Costs_Only_The_Hooks_And_Is_Reported()
        {
            var ext = ExtDir();
            MakeHook(ext, "doc-opened.py");
            var button = Path.Combine(ext, "Tools.tab", "General.panel", "Hello.pushbutton");
            Directory.CreateDirectory(button);
            File.WriteAllText(Path.Combine(button, "script.py"), "print(1)");
            var hooks = Path.Combine(ext, "hooks");

            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            var deny = new System.Security.AccessControl.FileSystemAccessRule(user,
                System.Security.AccessControl.FileSystemRights.ListDirectory,
                System.Security.AccessControl.AccessControlType.Deny);
            var security = Directory.GetAccessControl(hooks);
            security.AddAccessRule(deny);
            Directory.SetAccessControl(hooks, security);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFiles(hooks, "*.py")); // the premise

                var model = BundleParser.ParseExtension(ext);

                Assert.Empty(model.Hooks);
                Assert.Single(model.Tabs);
                var problem = Assert.Single(model.Problems);
                Assert.Contains(hooks, problem);
            }
            finally
            {
                security.RemoveAccessRule(deny);
                Directory.SetAccessControl(hooks, security);
            }
        }
    }
}
