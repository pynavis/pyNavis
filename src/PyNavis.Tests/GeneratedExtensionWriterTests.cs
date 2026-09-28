using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Ai;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Writing a proposal to disk as a bundle the real BundleParser will load, inside
    /// an AI.extension that this writer alone owns.
    /// </summary>
    public class GeneratedExtensionWriterTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));

        public GeneratedExtensionWriterTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private sealed class FakeIcons : IIconWriter
        {
            public List<(string dir, IconSpec spec)> Calls = new List<(string, IconSpec)>();

            public void Write(string bundleDir, IconSpec spec)
            {
                Calls.Add((bundleDir, spec));
                foreach (var name in new[] { "icon.png", "icon.dark.png", "icon.small.png", "icon.small.dark.png" })
                    File.WriteAllBytes(Path.Combine(bundleDir, name), new byte[] { 1 });
            }
        }

        private static Proposal Proposal(string title = "Isolate Level") => new Proposal
        {
            Title = title,
            Icon = new IconSpec("IL", "teal"),
            Files =
            {
                ["bundle.yaml"] = "title: " + title + "\ntooltip: Test tool.\n",
                ["script.py"] = "from pynavis import toast\ntoast.info('hi')\n",
            },
        };

        private string Root => Path.Combine(_dir, "AI.extension");

        [Fact]
        public void Create_LaysOutAnExtension_TheBundleParserLoads()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());

            var bundle = writer.Create(Proposal());

            Assert.Equal(Path.Combine(Root, "AI.tab", "Generated.panel", "Isolate_Level.pushbutton"), bundle);
            Assert.True(File.Exists(Path.Combine(Root, "extension.yaml")));
            var model = BundleParser.ParseExtension(Root);
            var button = Assert.IsType<PushButtonModel>(model.Tabs.Single().Panels.Single().Items.Single());
            Assert.Equal("Isolate Level", button.Title);
            Assert.Equal("Test tool.", button.Tooltip);
            Assert.Null(button.ConfigScriptPath);
        }

        [Fact]
        public void Create_WritesTheFourIcons_ThroughTheIconWriter()
        {
            var icons = new FakeIcons();
            var writer = new GeneratedExtensionWriter(Root, icons);

            var bundle = writer.Create(Proposal());

            // Painted into the staging folder before the move, so a Reload can never
            // meet a bundle without its icons.
            var call = Assert.Single(icons.Calls);
            Assert.Equal("IL", call.spec.Letters);
            Assert.Equal("teal", call.spec.Colour);
            foreach (var name in new[] { "icon.png", "icon.dark.png", "icon.small.png", "icon.small.dark.png" })
                Assert.True(File.Exists(Path.Combine(bundle, name)), "missing " + name);
        }

        [Fact]
        public void Create_Twice_WithTheSameTitle_NumbersTheSecondFolder()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());

            var first = writer.Create(Proposal());
            var second = writer.Create(Proposal());

            Assert.EndsWith("Isolate_Level.pushbutton", first);
            Assert.EndsWith("Isolate_Level_2.pushbutton", second);
        }

        [Fact]
        public void FolderNames_AreSafe_AndNeverEmpty()
        {
            Assert.Equal("Isolate_Level_3", GeneratedExtensionWriter.SafeFolderName("Isolate Level 3"));
            Assert.Equal("Whats_up", GeneratedExtensionWriter.SafeFolderName("  What's up? <> | / \\ : * "));
            Assert.Equal("Tool", GeneratedExtensionWriter.SafeFolderName("???"));
            Assert.Equal("Tool", GeneratedExtensionWriter.SafeFolderName(null));
            // A leading digit run would be read as an ordering prefix and stripped from
            // the title, so it is kept out of the folder name.
            Assert.Equal("Tool_01_Level", GeneratedExtensionWriter.SafeFolderName("01 Level"));
            Assert.Equal(40, GeneratedExtensionWriter.SafeFolderName(new string('a', 100)).Length);
        }

        [Fact]
        public void Update_ReplacesTheFiles_InPlace_AndDropsAConfigThatWentAway()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());
            var withConfig = Proposal();
            withConfig.Files["config.py"] = "pass\n";
            var bundle = writer.Create(withConfig);
            Assert.True(File.Exists(Path.Combine(bundle, "config.py")));

            var revised = Proposal();
            revised.Files["script.py"] = "from pynavis import toast\ntoast.success('changed')\n";
            writer.Update(bundle, revised);

            Assert.Contains("changed", File.ReadAllText(Path.Combine(bundle, "script.py")));
            Assert.False(File.Exists(Path.Combine(bundle, "config.py")));
            Assert.True(File.Exists(Path.Combine(bundle, "icon.png")));
        }

        [Fact]
        public void Update_RefusesAFolderOutsideTheExtension()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());
            var elsewhere = Path.Combine(_dir, "Other.extension", "T.tab", "P.panel", "X.pushbutton");
            Directory.CreateDirectory(elsewhere);

            Assert.Throws<InvalidOperationException>(() => writer.Update(elsewhere, Proposal()));
        }

        [Fact]
        public void Create_WithoutAScript_Refuses()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());
            var p = Proposal();
            p.Files.Remove("script.py");

            Assert.Throws<InvalidOperationException>(() => writer.Create(p));
        }

        [Fact]
        public void Create_WhenBundleYamlIsMissing_WritesOneFromTheTitle()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());
            var p = Proposal();
            p.Files.Remove("bundle.yaml");

            var bundle = writer.Create(p);

            Assert.Contains("title: Isolate Level", File.ReadAllText(Path.Combine(bundle, "bundle.yaml")));
        }

        [Fact]
        public void Owns_TellsGeneratedBundlesFromOthers()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());
            var bundle = writer.Create(Proposal());

            Assert.True(writer.Owns(bundle));
            Assert.True(writer.Owns(bundle.ToLowerInvariant()));
            Assert.False(writer.Owns(Path.Combine(_dir, "Other.extension", "x.pushbutton")));
        }

        [Fact]
        public void Tools_ListsWhatHasBeenCreated_NewestLast()
        {
            var writer = new GeneratedExtensionWriter(Root, new FakeIcons());
            Assert.Empty(writer.Tools());
            writer.Create(Proposal("Beta"));
            writer.Create(Proposal("Alpha"));

            var tools = writer.Tools();

            Assert.Equal(new[] { "Alpha", "Beta" }, tools.Select(t => t.Title).ToArray());
            Assert.All(tools, t => Assert.True(Directory.Exists(t.Directory)));
        }
    }
}
