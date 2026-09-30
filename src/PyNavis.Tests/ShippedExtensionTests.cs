using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The repo's shipped pyNavis.extension, parsed with the real BundleParser:
    /// catches broken bundle folders (missing script.py, renamed panels) at test time
    /// instead of as silently missing ribbon buttons.
    /// </summary>
    public class ShippedExtensionTests
    {
        private static ExtensionModel ParseShipped()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "extensions", "pyNavis.extension");
                if (Directory.Exists(candidate)) return BundleParser.ParseExtension(candidate);
            }
            throw new DirectoryNotFoundException("shipped pyNavis.extension not found above test dir");
        }

        [Fact]
        public void ThePanels_ReadAsSixNouns_InThisOrder()
        {
            // The tab's layout list orders the panels: setup first, then the model work
            // (selection, clash, viewpoints), then the AI panel, and data in and out last.
            // Name order would put AI first, so this only holds while the list does.
            var tab = Assert.Single(ParseShipped().Tabs);
            Assert.Equal(new[] { "pyNavis", "Selection", "Clash", "Viewpoints", "AI (beta)", "Data" },
                tab.Panels.Select(p => p.Title).ToArray());
        }

        [Fact]
        public void ShippedExtension_ParsesWithoutProblems()
        {
            // A layout entry that names no folder, a mistyped suffix, a button without a
            // script: all land here, and none may ship.
            Assert.Empty(ParseShipped().Problems);
        }

        // Folders carry no ordering prefixes any more, so the ribbon order is whatever the
        // layout lists say. Every container must have one and it must name every child,
        // or a new tool silently lands at the end of its panel in name order.
        [Fact]
        public void EveryShippedContainer_ListsEveryChild_InItsLayout()
        {
            var suffixes = new[] { ".tab", ".panel", ".stack", ".pulldown", ".slideout", ".splitbutton", ".splitpushbutton" };
            var containers = Directory.GetDirectories(ShippedExtensionDir(), "*", SearchOption.AllDirectories)
                .Where(d => suffixes.Any(s => d.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            Assert.NotEmpty(containers);

            foreach (var dir in containers)
            {
                var yamlPath = Path.Combine(dir, "bundle.yaml");
                Assert.True(File.Exists(yamlPath), $"{dir} has no bundle.yaml with a layout");
                var listed = BundleYaml.ListOf(BundleYaml.Parse(File.ReadAllText(yamlPath)), "layout")
                    .Select(BareName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                var children = Directory.GetDirectories(dir).Select(Path.GetFileName)
                    .Where(n => n.IndexOf('.') > 0)
                    .Select(BareName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                Assert.True(children.SequenceEqual(listed, StringComparer.OrdinalIgnoreCase),
                    $"{dir}: layout lists [{string.Join(", ", listed)}] but the folders are [{string.Join(", ", children)}]");
            }
        }

        [Fact]
        public void NoShippedFolder_CarriesAnOrderingPrefix()
        {
            var prefixed = Directory.GetDirectories(ShippedExtensionDir(), "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^\d{2,}_"))
                .ToArray();
            Assert.Empty(prefixed);
        }

        private static string BareName(string folderOrEntry)
        {
            var dot = folderOrEntry.LastIndexOf('.');
            return dot > 0 ? folderOrEntry.Substring(0, dot) : folderOrEntry;
        }

        [Fact]
        public void AiPanel_ShipsTheChatPane_AndASetupStack_AllMarkedBeta()
        {
            var ai = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "AI (beta)");

            Assert.Collection(ai.Items,
                i =>
                {
                    var pane = Assert.IsType<DockPaneModel>(i);
                    Assert.Equal("Ask AI", pane.Title);
                    Assert.Contains("beta", pane.Tooltip, StringComparison.OrdinalIgnoreCase);
                    Assert.True(File.Exists(pane.XamlPath), "missing pane.xaml");
                    Assert.NotNull(pane.ScriptPath);
                    foreach (var name in new[] { "icon.on.png", "icon.on.dark.png" })
                        Assert.True(File.Exists(Path.Combine(pane.Directory, name)), "missing " + name);
                },
                i => Assert.Equal(new[] { "AI Settings", "Open AI Folder" },
                        Assert.IsType<StackModel>(i).Buttons.Select(b => b.Title).ToArray()));
            Assert.All(ai.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));
            Assert.Empty(ai.Slideout);
        }

        [Fact]
        public void PyNavisPanel_Ships_Settings_Shortcuts_HideTabs_Console_Reload_AndPanelSlots()
        {
            var tab = Assert.Single(ParseShipped().Tabs);
            var general = tab.Panels.Single(p => p.Title == "pyNavis");

            // Settings first, the developer tools last: a coordinator should not
            // meet "Console" before anything else.
            Assert.Equal(new[] { "Settings", "Shortcuts", "Hide Tabs", "Console", "Reload" },
                general.Items.OfType<PushButtonModel>().Select(b => b.Title).ToArray());
            Assert.Equal(new[] { "Console", "Hide Tabs", "Panel slots", "Reload", "Settings", "Shortcuts" },
                general.Buttons.Select(b => b.Title).OrderBy(t => t).ToArray());

            // A toggle, with Shift+Click to choose the tabs, that says what a click
            // does in each state rather than "Hide Tabs" on both.
            var hide = general.Items.OfType<PushButtonModel>().Single(b => b.Title == "Hide Tabs");
            Assert.True(hide.IsToggle);
            Assert.NotNull(hide.ConfigScriptPath);
            Assert.Equal("Show Tabs", hide.TitleOn);
            Assert.False(string.IsNullOrWhiteSpace(hide.Tooltip));
            Assert.False(string.IsNullOrWhiteSpace(hide.TooltipOn));
        }

        [Fact]
        public void ShippedExtension_PutsHiddenTabsBack_BeforeNavisworksCloses()
        {
            // Hide Tabs takes tabs off the ribbon for the session; the app-closing
            // hook puts them back so Navisworks never saves a ribbon without them.
            var hook = Assert.Single(ParseShipped().Hooks);
            Assert.Equal("app-closing", hook.EventName);
            Assert.Contains("tabhider.show()", File.ReadAllText(hook.ScriptPath));
        }

        [Fact]
        public void PyNavisPanel_KeepsPanelSlotsInTheSlideout_NotOnThePanelItself()
        {
            // Panel slots is a once-per-install tool: it generates a satellite DLL and
            // then needs a restart. It does not deserve a permanent slot beside the
            // five things people press, so it lives behind the panel title.
            var general = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "pyNavis");

            Assert.Collection(general.Slideout,
                i => Assert.Equal("Panel slots", Assert.IsType<PushButtonModel>(i).Title));
            Assert.DoesNotContain(general.Items.OfType<PushButtonModel>(),
                b => b.Title == "Panel slots");
            Assert.Equal(new[] { "Console", "Hide Tabs", "Reload", "Settings", "Shortcuts" },
                general.Items.OfType<PushButtonModel>().Select(b => b.Title).OrderBy(t => t).ToArray());
        }

        /// <summary>PNG width/height straight out of the IHDR header, so the test needs
        /// no imaging dependency.</summary>
        private static (int width, int height) PngSize(string path)
        {
            var head = new byte[24];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(head, 0, head.Length) < head.Length)
                    throw new InvalidDataException(path + " is too short to be a PNG");
            }
            int Be(int offset) => (head[offset] << 24) | (head[offset + 1] << 16)
                                | (head[offset + 2] << 8) | head[offset + 3];
            return (Be(16), Be(20));
        }

        [Fact]
        public void ViewpointTrackerPane_ShipsBothOnStateIcons()
        {
            // A dockpane's pressed art has NO cross-fallback: shipping only
            // icon.on.png leaves the dark theme with no pressed icon at all.
            var viewpoints = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "Viewpoints");
            var pane = viewpoints.Items.OfType<DockPaneModel>().Single(p => p.Title == "Viewpoint Tracker");

            foreach (var name in new[] { "icon.on.png", "icon.on.dark.png" })
            {
                var path = Path.Combine(pane.Directory, name);
                Assert.True(File.Exists(path), $"missing {name} in {pane.Directory}");
                var (width, height) = PngSize(path);
                Assert.True(width == 96 && height == 96, $"{path} is {width}x{height}, expected 96x96");
            }
        }

        [Fact]
        public void EveryShippedBundle_HasAllFourIconVariants_AtTheRightSizes()
        {
            var bundles = ParseShipped().Tabs
                .SelectMany(t => t.Panels)
                .SelectMany(p => p.Items.Concat(p.Slideout))
                .Where(i => !(i is PushButtonModel hidden && hidden.NoUi))   // chord-only, never drawn
                .SelectMany(i =>
                    i is PulldownModel pulldown
                        ? new[] { pulldown.Directory }.Concat(pulldown.Buttons.Select(b => b.Directory))
                        : i is StackModel stack
                            ? stack.Buttons.Select(b => b.Directory)
                            : i is DockPaneModel pane
                                ? new[] { pane.Directory }
                                : new[] { ((PushButtonModel)i).Directory })
                .ToList();

            Assert.Equal(44, bundles.Count);

            var expected = new[]
            {
                ("icon.png", 96), ("icon.dark.png", 96),
                ("icon.small.png", 32), ("icon.small.dark.png", 32),
            };
            // A toggle draws its large art per state instead of one icon.png.
            var toggle = new[]
            {
                ("icon.off.png", 96), ("icon.off.dark.png", 96),
                ("icon.on.png", 96), ("icon.on.dark.png", 96),
                ("icon.small.png", 32), ("icon.small.dark.png", 32),
            };
            var toggles = new HashSet<string>(ParseShipped().Tabs
                .SelectMany(t => t.Panels).SelectMany(p => p.Buttons)
                .Where(b => b.IsToggle).Select(b => b.Directory));
            Assert.NotEmpty(toggles);

            foreach (var dir in bundles)
            {
                foreach (var (name, size) in toggles.Contains(dir) ? toggle : expected)
                {
                    var path = Path.Combine(dir, name);
                    Assert.True(File.Exists(path), $"missing {name} in {dir}");

                    var (width, height) = PngSize(path);
                    Assert.True(width == size && height == size,
                        $"{path} is {width}x{height}, expected {size}x{size}");
                }
            }
        }

        private static string ShippedExtensionDir()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "extensions", "pyNavis.extension");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("shipped pyNavis.extension not found above test dir");
        }

        [Fact]
        public void ExtensionLibModules_Compile()
        {
            // The extension's lib/ is importable from every bundle in it but is
            // reached by no button of its own, so nothing else here would notice
            // a syntax error in it until a click failed inside Navisworks.
            var lib = Path.Combine(ShippedExtensionDir(), "lib");
            var modules = Directory.GetFiles(lib, "*.py", SearchOption.AllDirectories);
            Assert.NotEmpty(modules);

            var engine = new Runtime.Engine.IronPythonEngine();
            var config = new Runtime.Engine.EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            engine.Initialize(config);

            foreach (var path in modules)
            {
                var req = new Runtime.Engine.ScriptRequest
                {
                    Code = "with open(__source_path__) as f:\n"
                         + "    compile(f.read(), __source_path__, 'exec')",
                };
                req.Globals["__source_path__"] = path;
                var result = engine.Execute(req);
                Assert.True(result.Succeeded, path + "\n" + result.ErrorText);
            }
        }

        [Fact]
        public void AllShippedScripts_Compile()
        {
            var engine = new Runtime.Engine.IronPythonEngine();
            var config = new Runtime.Engine.EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            engine.Initialize(config);

            var model = ParseShipped();
            var scripts = model.Tabs
                .SelectMany(t => t.Panels)
                .SelectMany(p => p.Buttons)
                .SelectMany(b => new[] { b.ScriptPath, b.ConfigScriptPath })
                .Concat(model.Tabs
                    .SelectMany(t => t.Panels)
                    .SelectMany(p => p.Items.Concat(p.Slideout))
                    .OfType<DockPaneModel>()
                    .Select(d => d.ScriptPath))
                .Where(p => p != null)
                .ToList();

            // The grouper ships the first Shift+Click secondary action; this keeps
            // every future config.py compiling alongside its script.py.
            Assert.Contains(scripts, s => s.EndsWith("config.py"));

            // A dockpane's script.py hangs off no PushButtonModel, so without the
            // Concat above it would ship uncompiled and break only at pane build.
            Assert.Contains(scripts, s => s.Contains(".dockpane"));

            foreach (var path in scripts)
            {
                var req = new Runtime.Engine.ScriptRequest
                {
                    Code = "with open(__source_path__) as f:\n"
                         + "    compile(f.read(), __source_path__, 'exec')",
                };
                req.Globals["__source_path__"] = path;

                var r = engine.Execute(req);
                Assert.True(r.Succeeded, $"{path} failed to compile:\n{r.ErrorText}");
            }
        }

        [Fact]
        public void ViewpointsPanel_Ships_TheManageSectionStateAndSpeedsStacks()
        {
            var tab = Assert.Single(ParseShipped().Tabs);
            var viewpoints = tab.Panels.Single(p => p.Title == "Viewpoints");

            // Six chord-only nobuttons sit beside these; they never render.
            var drawn = viewpoints.Items.Where(i => !(i is PushButtonModel b && b.NoUi)).ToList();
            Assert.Equal(6, drawn.Count);
            var manage = Assert.IsType<StackModel>(viewpoints.Items[0]);
            Assert.Equal(new[] { "Rename", "Delete", "Manage" },
                manage.Buttons.Select(b => b.Title).ToArray());

            var section = Assert.IsType<StackModel>(viewpoints.Items[1]);
            // No Section Clear: Navisworks' own Enable Sectioning toggle does exactly that.
            Assert.Equal(new[] { "Section Fit", "Section Plan" },
                section.Buttons.Select(b => b.Title).ToArray());
            Assert.All(section.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));

            // Fit carries the Shift+Click settings dialog; Plan does not.
            Assert.NotNull(section.Buttons.Single(b => b.Title == "Section Fit").ConfigScriptPath);
            Assert.Null(section.Buttons.Single(b => b.Title == "Section Plan").ConfigScriptPath);

            var state = Assert.IsType<StackModel>(viewpoints.Items[2]);
            Assert.Equal(new[] { "Copy State", "Paste State" },
                state.Buttons.Select(b => b.Title).ToArray());
            Assert.All(state.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));

            var speeds = Assert.IsType<StackModel>(viewpoints.Items[3]);
            Assert.Equal(new[] { "Apply Speeds", "Speeds to Saved" },
                speeds.Buttons.Select(b => b.Title).ToArray());
            Assert.All(speeds.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));

            var tracker = Assert.IsType<DockPaneModel>(viewpoints.Items[4]);
            Assert.Equal("Viewpoint Tracker", tracker.Title);
            Assert.False(string.IsNullOrWhiteSpace(tracker.Tooltip));
            // pane.xaml is required; a dockpane without one is dropped silently.
            Assert.True(File.Exists(tracker.XamlPath), "missing pane.xaml");
            Assert.NotNull(tracker.ScriptPath);

            var nudge = Assert.IsType<DockPaneModel>(viewpoints.Items[5]);
            Assert.Equal("Section Nudge", nudge.Title);
            Assert.False(string.IsNullOrWhiteSpace(nudge.Tooltip));
            Assert.True(File.Exists(nudge.XamlPath), "missing pane.xaml");
            Assert.NotNull(nudge.ScriptPath);
        }

        [Fact]
        public void ViewpointsPanel_ShipsSixSectionNudgeChords_AsNoButtons()
        {
            // The panel's keys, reachable over the 3D view without the panel
            // focused. Chord-only, so no ribbon presence and no icons.
            var viewpoints = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "Viewpoints");
            var chords = viewpoints.Buttons.Where(b => b.NoUi).ToList();

            Assert.Equal(new[]
            {
                ("Nudge In", "Ctrl+Alt+Up"),
                ("Nudge Out", "Ctrl+Alt+Down"),
                ("Nudge Next Target", "Ctrl+Alt+Right"),
                ("Nudge Previous Target", "Ctrl+Alt+Left"),
                ("Nudge Step Double", "Ctrl+Alt+PgUp"),
                ("Nudge Step Halve", "Ctrl+Alt+PgDn"),
            }, chords.Select(b => (b.Title, b.Shortcut)).ToArray());
            Assert.All(chords, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));
            Assert.All(chords, b => Assert.NotNull(b.ScriptPath));
        }

        [Fact]
        public void SpeedsStack_BothButtons_OpenTheSameSharedSettingsDialog_AndKeepTheirChords()
        {
            var viewpoints = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "Viewpoints");
            var stack = viewpoints.Items.OfType<StackModel>()
                                  .Single(s => s.Buttons.Any(b => b.Title == "Apply Speeds"));

            // Shift+Click on either button reaches the settings the two share,
            // so neither may lose its config.py.
            Assert.All(stack.Buttons, b => Assert.NotNull(b.ConfigScriptPath));

            // The chords the C# add-in shipped with, carried over as defaults.
            Assert.Equal("Alt+Q", stack.Buttons.Single(b => b.Title == "Apply Speeds").Shortcut);
            Assert.Equal("Alt+Z", stack.Buttons.Single(b => b.Title == "Speeds to Saved").Shortcut);
        }

        [Fact]
        public void ViewpointsPanel_Slideout_ShipsTheFloatingTrackerWindow()
        {
            // The panel is already five slots wide, so the second tracker shell
            // lives behind the panel title the way Element ID Settings does.
            var viewpoints = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "Viewpoints");

            Assert.Collection(viewpoints.Slideout,
                i => Assert.Equal("Tracker Window", Assert.IsType<PushButtonModel>(i).Title));
            Assert.All(viewpoints.Slideout.OfType<PushButtonModel>(),
                b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));
        }

        [Fact]
        public void ClashPanel_Ships_TheFourClashTools_InWorkflowOrder()
        {
            var tab = Assert.Single(ParseShipped().Tabs);
            var clash = tab.Panels.Single(p => p.Title == "Clash");

            Assert.Collection(clash.Items,
                i => Assert.Equal("Clash Report", Assert.IsType<PushButtonModel>(i).Title),
                i => Assert.Equal("Clash Grouper", Assert.IsType<PushButtonModel>(i).Title),
                i => Assert.Equal("Clear Clash", Assert.IsType<PushButtonModel>(i).Title),
                i => Assert.Equal("True Distance", Assert.IsType<PushButtonModel>(i).Title));
            Assert.Empty(clash.Slideout);

            // Clear Clash asks for the gap on every run, pre-filled with the last
            // one, so a Shift+Click default would only be a second place to set it.
            // Set Gap was folded into it and must not come back.
            Assert.Null(clash.Buttons.Single(b => b.Title == "Clear Clash").ConfigScriptPath);
            Assert.DoesNotContain(clash.Buttons, b => b.Title == "Set Gap");
        }

        [Fact]
        public void DataPanel_Ships_TwoStacks_AndTwoLargeTools()
        {
            var tab = Assert.Single(ParseShipped().Tabs);
            var data = tab.Panels.Single(p => p.Title == "Data");

            Assert.Collection(data.Items,
                i => Assert.Equal(new[] { "Get Coordinates", "Go to Coordinates" },
                                  Assert.IsType<StackModel>(i).Buttons.Select(b => b.Title).ToArray()),
                i => Assert.Equal(new[] { "Select by IDs", "IDs of Selection" },
                                  Assert.IsType<StackModel>(i).Buttons.Select(b => b.Title).ToArray()),
                i => Assert.Equal("Excel Sets", Assert.IsType<PushButtonModel>(i).Title),
                i => Assert.Equal("Export Viewpoints", Assert.IsType<PushButtonModel>(i).Title));
            Assert.Collection(data.Slideout,
                i => Assert.Equal("Element ID Settings", Assert.IsType<PushButtonModel>(i).Title));
        }

        [Fact]
        public void ElementIdStack_BothButtons_OpenTheSameSharedSettingsDialog()
        {
            var tools = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "Data");
            var stack = tools.Items.OfType<StackModel>()
                             .Single(s => s.Buttons.Any(b => b.Title == "Select by IDs"));

            // Shift+Click on either button reaches the settings the two share,
            // so neither may lose its config.py.
            Assert.All(stack.Buttons, b => Assert.NotNull(b.ConfigScriptPath));
            Assert.All(stack.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));
        }

        [Fact]
        public void ClashAndDataPanels_LargeCaptions_WrapOntoTwoLines()
        {
            // Six long captions stretched the ribbon; each breaks once so the
            // panels stay narrow. Stacked rows are one line each by construction,
            // so they are excluded.
            var panels = ParseShipped().Tabs.Single().Panels.Where(p => p.Title == "Clash" || p.Title == "Data");

            Assert.Equal(
                new[] { "Clash\nGrouper", "Clash\nReport", "Clear\nClash", "Excel\nSets", "Export\nViewpoints", "True\nDistance" },
                panels.SelectMany(p => p.Items.OfType<PushButtonModel>())
                      .Select(b => b.RibbonTitle).OrderBy(t => t).ToArray());
        }

        [Fact]
        public void SelectionPanel_Ships_TwoLargeButtons_TwoStacks_AndAPulldown()
        {
            // Plain verbs, no M prefix: the panel name carries "selection".
            var tab = Assert.Single(ParseShipped().Tabs);
            var memory = tab.Panels.Single(p => p.Title == "Selection");

            Assert.Collection(memory.Items,
                i => Assert.Equal("Remember", Assert.IsType<PushButtonModel>(i).Title),
                i => Assert.Equal("Recall", Assert.IsType<PushButtonModel>(i).Title),
                i => Assert.Equal(new[] { "Add", "Subtract", "Intersect" },
                        Assert.IsType<StackModel>(i).Buttons.Select(b => b.Title).ToArray()),
                i => Assert.Equal(new[] { "Previous", "Next", "Forget" },
                        Assert.IsType<StackModel>(i).Buttons.Select(b => b.Title).ToArray()),
                i => Assert.Equal(new[] { "Show Contents", "Save as Set", "Purge" },
                        Assert.IsType<PulldownModel>(i).Buttons.Select(b => b.Title).ToArray()));
        }

        [Fact]
        public void SelectionPanel_ShipsElevenButtons_EachWithATooltip()
        {
            var tab = Assert.Single(ParseShipped().Tabs);
            var memory = tab.Panels.Single(p => p.Title == "Selection");

            Assert.Equal(11, memory.Buttons.Count);
            Assert.All(memory.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));
        }

        [Fact]
        public void SelectionPanel_ShipsTheFourDefaultShortcuts()
        {
            var buttons = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "Selection").Buttons;

            Assert.Equal("Ctrl+Shift+M", buttons.Single(b => b.Title == "Remember").Shortcut);
            Assert.Equal("Ctrl+Shift+R", buttons.Single(b => b.Title == "Recall").Shortcut);
            Assert.Equal("Ctrl+Shift+Left", buttons.Single(b => b.Title == "Previous").Shortcut);
            Assert.Equal("Ctrl+Shift+Right", buttons.Single(b => b.Title == "Next").Shortcut);
            Assert.Equal(4, buttons.Count(b => b.Shortcut != null));
        }
    }
}
