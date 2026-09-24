using System;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Install;
using Xunit;

namespace PyNavis.Tests
{
    public class PackageManifestTests
    {
        [Fact]
        public void Register_Creates_The_Year_Block_With_Its_Series()
        {
            var doc = PackageManifest.CreateEmpty("0.4.0");

            PackageManifest.Register(doc, "2026", "PyNavis.dll", "pyNavis", "0.4.0");

            var block = Assert.Single(doc.Root.Elements("Components"));
            var req = block.Element("RuntimeRequirements");
            Assert.Equal("Nw23", (string)req.Attribute("SeriesMin"));
            Assert.Equal("Nw23", (string)req.Attribute("SeriesMax"));
            Assert.Equal("NAVMAN|NAVSIM", (string)req.Attribute("Platform"));
            var entry = Assert.Single(block.Elements("ComponentEntry"));
            Assert.Equal("./Contents/2026/PyNavis.dll", (string)entry.Attribute("ModuleName"));
            Assert.Equal("ManagedPlugin", (string)entry.Attribute("AppType"));
        }

        [Fact]
        public void Register_Twice_Adds_One_Entry_And_A_Second_Dll_Joins_The_Same_Block()
        {
            var doc = PackageManifest.CreateEmpty("0.4.0");
            PackageManifest.Register(doc, "2026", "PyNavis.dll", "pyNavis", "0.4.0");
            PackageManifest.Register(doc, "2026", "PyNavis.dll", "pyNavis", "0.4.0");
            PackageManifest.Register(doc, "2026", "PyNavisPanes.dll", "pyNavis panel slots", "0.4.0");

            var block = Assert.Single(doc.Root.Elements("Components"));
            Assert.Equal(2, block.Elements("ComponentEntry").Count());
        }

        [Fact]
        public void Unregister_Removes_Only_That_Entry_And_Drops_An_Emptied_Block()
        {
            var doc = PackageManifest.CreateEmpty("0.4.0");
            PackageManifest.Register(doc, "2026", "PyNavis.dll", "pyNavis", "0.4.0");
            PackageManifest.Register(doc, "2026", "PyNavisPanes.dll", "pyNavis panel slots", "0.4.0");
            PackageManifest.Register(doc, "2025", "PyNavis.dll", "pyNavis", "0.4.0");

            Assert.True(PackageManifest.Unregister(doc, "2026", "PyNavisPanes.dll"));
            Assert.False(PackageManifest.Unregister(doc, "2026", "PyNavisPanes.dll"));
            Assert.Single(PackageManifest.FindYear(doc, "2026").Elements("ComponentEntry"));

            Assert.True(PackageManifest.Unregister(doc, "2025", "PyNavis.dll"));
            Assert.Null(PackageManifest.FindYear(doc, "2025"));
            Assert.False(PackageManifest.IsEmpty(doc));
        }

        [Fact]
        public void Save_And_Load_Round_Trip_With_Declaration()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "PackageContents.xml");
            try
            {
                var doc = PackageManifest.CreateEmpty("0.4.0");
                PackageManifest.Register(doc, "2024", "PyNavis.dll", "pyNavis", "0.4.0");
                PackageManifest.Save(doc, path);
                PackageManifest.Save(doc, path);   // overwrite path too

                var text = File.ReadAllText(path);
                Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
                Assert.False(File.Exists(path + ".tmp"));
                var back = PackageManifest.Load(path);
                Assert.Equal("Nw21", (string)PackageManifest.FindYear(back, "2024").Element("RuntimeRequirements").Attribute("SeriesMin"));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void BundleLayout_Recognises_Only_The_Bundle_Shape()
        {
            var inBundle = BundleLayout.FromLoaderPath(@"C:\Users\x\AppData\Roaming\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\2026\PyNavis.dll");
            Assert.NotNull(inBundle);
            Assert.Equal("2026", inBundle.Year);
            Assert.EndsWith(@"pyNavis.bundle", inBundle.Root);
            Assert.EndsWith(@"pyNavis.bundle\Contents\2026", inBundle.ContentsDir);
            Assert.EndsWith(@"pyNavis.bundle\PackageContents.xml", inBundle.ManifestPath);

            Assert.Null(BundleLayout.FromLoaderPath(@"C:\Program Files\Autodesk\Navisworks Manage 2026\Plugins\PyNavis\PyNavis.dll"));
            Assert.Null(BundleLayout.FromLoaderPath(@"D:\repo\bin\2026\PyNavis.dll"));
            Assert.Null(BundleLayout.FromLoaderPath(@"X:\Other.bundle\Contents\notayear\PyNavis.dll"));
            Assert.Null(BundleLayout.FromLoaderPath(null));
        }

        [Fact]
        public void SeriesFor_Is_Year_Minus_2003()
        {
            Assert.Equal("Nw20", BundleLayout.SeriesFor("2023"));
            Assert.Equal("Nw23", BundleLayout.SeriesFor("2026"));
        }
    }
}
