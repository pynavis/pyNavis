using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>Builds throwaway extension folder trees and parses them.</summary>
    public class BundleParserTests : IDisposable
    {
        private readonly string _root;

        public BundleParserTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeButton(string ext, string tab, string panel, string button,
            string script = "print(1)\n", string yaml = null, bool icon = false)
        {
            var dir = Path.Combine(_root, ext + ".extension", tab + ".tab", panel + ".panel", button + ".pushbutton");
            Directory.CreateDirectory(dir);
            if (script != null) File.WriteAllText(Path.Combine(dir, "script.py"), script);
            if (yaml != null) File.WriteAllText(Path.Combine(dir, "bundle.yaml"), yaml);
            if (icon) File.WriteAllBytes(Path.Combine(dir, "icon.png"), new byte[] { 137, 80, 78, 71 });
            return dir;
        }

        private string MakeStackButton(string ext, string tab, string panel, string stack, string button,
            string script = "print(1)\n")
        {
            var dir = Path.Combine(_root, ext + ".extension", tab + ".tab", panel + ".panel",
                stack + ".stack", button + ".pushbutton");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "script.py"), script);
            return dir;
        }

        private string MakePulldownButton(string ext, string tab, string panel, string pulldown, string button)
        {
            var dir = Path.Combine(_root, ext + ".extension", tab + ".tab", panel + ".panel",
                pulldown + ".pulldown", button + ".pushbutton");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "script.py"), "print(1)\n");
            return dir;
        }

        private ExtensionModel ParseSingle(string ext = "MyExt") =>
            BundleParser.ParseExtension(Path.Combine(_root, ext + ".extension"));

        [Fact]
        public void Builds_TabPanelButton_Tree()
        {
            MakeButton("MyExt", "Tools", "General", "Hello_World");
            var ext = ParseSingle();

            Assert.Equal("MyExt", ext.Name);
            var tab = Assert.Single(ext.Tabs);
            Assert.Equal("Tools", tab.Title);
            var panel = Assert.Single(tab.Panels);
            Assert.Equal("General", panel.Title);
            var button = Assert.Single(panel.Buttons);
            Assert.Equal("Hello World", button.Title); // underscores -> spaces
            Assert.True(File.Exists(button.ScriptPath));
        }

        // One folder the user cannot list (ACL, a path that went offline mid-scan) used to
        // throw out of the whole scan: no ribbon at all. It must cost only itself.
        [Fact]
        public void UnlistableFolder_IsSkipped_AndItsSiblingsStillParse()
        {
            MakeButton("MyExt", "Tools", "Good", "Hello");
            MakeButton("MyExt", "Tools", "Locked", "Secret");
            var locked = Path.Combine(_root, "MyExt.extension", "Tools.tab", "Locked.panel");

            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            var deny = new System.Security.AccessControl.FileSystemAccessRule(user,
                System.Security.AccessControl.FileSystemRights.ListDirectory,
                System.Security.AccessControl.AccessControlType.Deny);
            var security = Directory.GetAccessControl(locked);
            security.AddAccessRule(deny);
            Directory.SetAccessControl(locked, security);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() => Directory.GetDirectories(locked)); // the premise

                var ext = Assert.Single(BundleParser.ParseRoot(_root));

                var tab = Assert.Single(ext.Tabs);
                Assert.Contains(tab.Panels, p => p.Title == "Good" && p.Buttons.Count == 1);
            }
            finally
            {
                security.RemoveAccessRule(deny);
                Directory.SetAccessControl(locked, security);
            }
        }

        // pyRevit's most common unanswerable support question: "my button is not there".
        // A folder the parser skips must say so, in the log and on the model.
        [Fact]
        public void MistypedBundleSuffix_IsReported_NotSilentlyIgnored()
        {
            MakeButton("MyExt", "Tools", "General", "Good");
            Directory.CreateDirectory(Path.Combine(_root, "MyExt.extension", "Tools.tab", "01_pyNavis.panel", "Oops.pushbuton"));

            var ext = ParseSingle();

            var problem = Assert.Single(ext.Problems);
            Assert.Contains("Oops.pushbuton", problem);
        }

        [Fact]
        public void PlainHelperFolders_AreNotReported()
        {
            MakeButton("MyExt", "Tools", "General", "Good");
            Directory.CreateDirectory(Path.Combine(_root, "MyExt.extension", "Tools.tab", "01_pyNavis.panel", "assets"));
            Directory.CreateDirectory(Path.Combine(_root, "MyExt.extension", "Tools.tab", "01_pyNavis.panel", "__pycache__"));

            Assert.Empty(ParseSingle().Problems);
        }

        [Fact]
        public void PushButtonWithoutScript_IsReported()
        {
            MakeButton("MyExt", "Tools", "General", "Good");
            Directory.CreateDirectory(Path.Combine(_root, "MyExt.extension", "Tools.tab", "01_pyNavis.panel", "Empty.pushbutton"));

            var problem = Assert.Single(ParseSingle().Problems);
            Assert.Contains("Empty.pushbutton", problem);
            Assert.Contains("script.py", problem);
        }

        // Stacks and pulldowns hold *.pushbutton only. A smartbutton or toggle dropped
        // into one used to vanish without a word.
        [Fact]
        public void NonPushButtonInsideAStack_IsReported()
        {
            MakeStackButton("MyExt", "Tools", "General", "Pair", "One");
            MakeStackButton("MyExt", "Tools", "General", "Pair", "Two");
            var smart = Path.Combine(_root, "MyExt.extension", "Tools.tab", "01_pyNavis.panel", "Pair.stack", "Three.smartbutton");
            Directory.CreateDirectory(smart);
            File.WriteAllText(Path.Combine(smart, "script.py"), "");

            var ext = ParseSingle();

            var problem = Assert.Single(ext.Problems);
            Assert.Contains("Three.smartbutton", problem);
            Assert.Contains("pushbutton", problem);
        }

        [Fact]
        public void Title_BackslashN_WrapsOnTheRibbon_ButStaysOneLineEverywhereElse()
        {
            // Long captions stretch a panel wide, so an author breaks them with
            // \n. Only the ribbon caption wraps: logs, toasts, the output window
            // title and __title__ all read the flat one-line form.
            MakeButton("MyExt", "T", "P", "Long", yaml: "title: Export\\nViewpoints\n");
            var button = Assert.Single(ParseSingle().Tabs.Single().Panels.Single().Buttons);

            Assert.Equal("Export Viewpoints", button.Title);
            Assert.Equal("Export\nViewpoints", button.RibbonTitle);
        }

        [Fact]
        public void Title_WithoutBackslashN_HasRibbonTitleMatchingTitle()
        {
            MakeButton("MyExt", "T", "P", "Plain", yaml: "title: Clash Report\n");
            var button = Assert.Single(ParseSingle().Tabs.Single().Panels.Single().Buttons);

            Assert.Equal("Clash Report", button.Title);
            Assert.Equal("Clash Report", button.RibbonTitle);
        }

        [Fact]
        public void Pulldown_Title_AlsoWraps()
        {
            MakePulldownButton("MyExt", "T", "P", "Big_Menu", "Child");
            File.WriteAllText(
                Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel", "Big_Menu.pulldown", "bundle.yaml"),
                "title: Big\\nMenu\n");
            var pulldown = Assert.IsType<PulldownModel>(
                ParseSingle().Tabs.Single().Panels.Single().Items.Single());

            Assert.Equal("Big Menu", pulldown.Title);
            Assert.Equal("Big\nMenu", pulldown.RibbonTitle);
        }

        [Fact]
        public void ConfigScript_NextToScript_IsRecorded()
        {
            var dir = MakeButton("MyExt", "T", "P", "Tool");
            File.WriteAllText(Path.Combine(dir, "config.py"), "print('secondary')\n");

            var button = ParseSingle().Tabs.Single().Panels.Single().Buttons.Single();

            Assert.Equal(Path.Combine(dir, "config.py"), button.ConfigScriptPath);
        }

        [Fact]
        public void MissingConfigScript_YieldsNull()
        {
            MakeButton("MyExt", "T", "P", "Tool");

            var button = ParseSingle().Tabs.Single().Panels.Single().Buttons.Single();

            Assert.Null(button.ConfigScriptPath);
        }

        [Fact]
        public void Button_WithoutScript_IsSkipped()
        {
            MakeButton("MyExt", "T", "P", "NoScript", script: null);
            MakeButton("MyExt", "T", "P", "HasScript");
            var buttons = ParseSingle().Tabs.Single().Panels.Single().Buttons;
            Assert.Single(buttons);
            Assert.Equal("HasScript", buttons[0].Title);
        }

        [Fact]
        public void Title_DunderTitle_OverridesFolderName()
        {
            MakeButton("MyExt", "T", "P", "Folder_Name", script: "__title__ = 'From Script'\n");
            Assert.Equal("From Script", ParseSingle().Tabs[0].Panels[0].Buttons[0].Title);
        }

        [Fact]
        public void Title_Yaml_OverridesEverything()
        {
            MakeButton("MyExt", "T", "P", "Folder_Name",
                script: "__title__ = 'From Script'\n", yaml: "title: From Yaml\n");
            Assert.Equal("From Yaml", ParseSingle().Tabs[0].Panels[0].Buttons[0].Title);
        }

        [Fact]
        public void Tooltip_ComesFromDocstring_YamlWins()
        {
            MakeButton("MyExt", "T", "P", "A", script: "\"\"\"Doc tooltip.\"\"\"\nprint(1)\n");
            MakeButton("MyExt", "T", "P", "B", script: "\"\"\"Doc tooltip.\"\"\"\n", yaml: "tooltip: Yaml tooltip\n");
            var buttons = ParseSingle().Tabs[0].Panels[0].Buttons;
            Assert.Equal("Doc tooltip.", buttons.Single(b => b.Title == "A").Tooltip);
            Assert.Equal("Yaml tooltip", buttons.Single(b => b.Title == "B").Tooltip);
        }

        [Fact]
        public void IconPath_SetWhenPresent_NullOtherwise()
        {
            MakeButton("MyExt", "T", "P", "WithIcon", icon: true);
            MakeButton("MyExt", "T", "P", "NoIcon");
            var buttons = ParseSingle().Tabs[0].Panels[0].Buttons;
            Assert.EndsWith("icon.png", buttons.Single(b => b.Title == "WithIcon").IconPath);
            Assert.Null(buttons.Single(b => b.Title == "NoIcon").IconPath);
        }

        [Fact]
        public void EngineId_DefaultsToIronPython_YamlOverrides()
        {
            MakeButton("MyExt", "T", "P", "Default");
            MakeButton("MyExt", "T", "P", "Cpy", yaml: "engine: cpython\n");
            var buttons = ParseSingle().Tabs[0].Panels[0].Buttons;
            Assert.Equal("ironpython", buttons.Single(b => b.Title == "Default").EngineId);
            Assert.Equal("cpython", buttons.Single(b => b.Title == "Cpy").EngineId);
        }

        [Fact]
        public void SearchPaths_AreBundleDir_ThenExtensionLib()
        {
            var dir = MakeButton("MyExt", "T", "P", "A");
            var lib = Path.Combine(_root, "MyExt.extension", "lib");
            Directory.CreateDirectory(lib);
            var button = ParseSingle().Tabs[0].Panels[0].Buttons[0];
            Assert.Equal(new[] { dir, lib }, button.SearchPaths);
        }

        [Fact]
        public void ExtensionName_FromExtensionYaml_ElseFolderName()
        {
            MakeButton("MyExt", "T", "P", "A");
            File.WriteAllText(Path.Combine(_root, "MyExt.extension", "extension.yaml"), "name: Fancy Name\n");
            Assert.Equal("Fancy Name", ParseSingle().Name);
        }

        [Fact]
        public void Buttons_AreOrderedByFolderName()
        {
            MakeButton("MyExt", "T", "P", "Zeta");
            MakeButton("MyExt", "T", "P", "Alpha");
            var titles = ParseSingle().Tabs[0].Panels[0].Buttons.Select(b => b.Title).ToArray();
            Assert.Equal(new[] { "Alpha", "Zeta" }, titles);
        }

        [Fact]
        public void ParseRoot_FindsOnlyExtensionDirs()
        {
            MakeButton("ExtA", "T", "P", "A");
            MakeButton("ExtB", "T", "P", "B");
            Directory.CreateDirectory(Path.Combine(_root, "NotAnExtension"));
            var exts = BundleParser.ParseRoot(_root);
            Assert.Equal(2, exts.Count);
            Assert.Equal(new[] { "ExtA", "ExtB" }, exts.Select(e => e.Name).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void ParseRoot_MissingDirectory_YieldsEmptyList()
        {
            Assert.Empty(BundleParser.ParseRoot(Path.Combine(_root, "does_not_exist")));
        }

        [Fact]
        public void Title_StripsNumericOrderingPrefix()
        {
            MakeButton("MyExt", "T", "P", "01_Memorize");
            MakeButton("MyExt", "T", "P", "02_Recall_Now");
            var titles = ParseSingle().Tabs[0].Panels[0].Buttons.Select(b => b.Title).ToArray();
            Assert.Equal(new[] { "Memorize", "Recall Now" }, titles);
        }

        [Fact]
        public void Title_SingleDigitPrefix_IsNotStripped()
        {
            MakeButton("MyExt", "T", "P", "1_Keep");
            Assert.Equal("1 Keep", ParseSingle().Tabs[0].Panels[0].Buttons[0].Title);
        }

        [Fact]
        public void Stack_WithThreeButtons_ParsesAsOnePanelItem()
        {
            MakeStackButton("MyExt", "T", "P", "03_Set", "01_Add");
            MakeStackButton("MyExt", "T", "P", "03_Set", "02_Subtract");
            MakeStackButton("MyExt", "T", "P", "03_Set", "03_Intersect");

            var panel = ParseSingle().Tabs[0].Panels[0];
            var stack = Assert.IsType<StackModel>(Assert.Single(panel.Items));
            Assert.Equal(new[] { "Add", "Subtract", "Intersect" },
                stack.Buttons.Select(b => b.Title).ToArray());
        }

        [Fact]
        public void Stack_FlattensIntoPanelButtons_InFolderOrder()
        {
            MakeButton("MyExt", "T", "P", "01_Memorize");
            MakeStackButton("MyExt", "T", "P", "02_Set", "01_Add");
            MakeStackButton("MyExt", "T", "P", "02_Set", "02_Subtract");

            var titles = ParseSingle().Tabs[0].Panels[0].Buttons.Select(b => b.Title).ToArray();
            Assert.Equal(new[] { "Memorize", "Add", "Subtract" }, titles);
        }

        [Fact]
        public void Stack_ChildWithoutScript_IsSkipped()
        {
            MakeStackButton("MyExt", "T", "P", "S", "01_Good");
            Directory.CreateDirectory(Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel",
                "S.stack", "02_NoScript.pushbutton"));
            MakeStackButton("MyExt", "T", "P", "S", "03_AlsoGood");

            var stack = Assert.IsType<StackModel>(Assert.Single(ParseSingle().Tabs[0].Panels[0].Items));
            Assert.Equal(2, stack.Buttons.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void Stack_WithWrongButtonCount_IsSkipped(int count)
        {
            for (var i = 1; i <= count; i++)
                MakeStackButton("MyExt", "T", "P", "S", "0" + i + "_B");
            MakeButton("MyExt", "T", "P", "99_Keeper");

            var items = ParseSingle().Tabs[0].Panels[0].Items;
            var only = Assert.IsType<PushButtonModel>(Assert.Single(items));
            Assert.Equal("Keeper", only.Title);
        }

        [Fact]
        public void Shortcut_And_Keytip_ComeFromBundleYaml()
        {
            MakeButton("MyExt", "T", "P", "A", yaml: "shortcut: Ctrl+Shift+M\nkeytip: MM\n");
            MakeButton("MyExt", "T", "P", "B");
            var buttons = ParseSingle().Tabs[0].Panels[0].Buttons;

            Assert.Equal("Ctrl+Shift+M", buttons.Single(b => b.Title == "A").Shortcut);
            Assert.Equal("MM", buttons.Single(b => b.Title == "A").KeyTipOverride);
            Assert.Null(buttons.Single(b => b.Title == "B").Shortcut);
            Assert.Null(buttons.Single(b => b.Title == "B").KeyTipOverride);
        }

        [Fact]
        public void Slideout_FolderContents_LandBelowThePanelBreak()
        {
            MakeButton("MyExt", "T", "P", "01_Main");
            var below = Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel", "99_More.slideout");
            Directory.CreateDirectory(Path.Combine(below, "01_Settings.pushbutton"));
            File.WriteAllText(Path.Combine(below, "01_Settings.pushbutton", "script.py"), "print(1)\n");
            Directory.CreateDirectory(Path.Combine(below, "02_Pair.stack", "01_A.pushbutton"));
            File.WriteAllText(Path.Combine(below, "02_Pair.stack", "01_A.pushbutton", "script.py"), "print(1)\n");
            Directory.CreateDirectory(Path.Combine(below, "02_Pair.stack", "02_B.pushbutton"));
            File.WriteAllText(Path.Combine(below, "02_Pair.stack", "02_B.pushbutton", "script.py"), "print(1)\n");

            var panel = ParseSingle().Tabs[0].Panels[0];

            Assert.Equal(new[] { "Main" }, panel.Items.Select(i => ((PushButtonModel)i).Title));
            Assert.Equal(2, panel.Slideout.Count);
            Assert.Equal("Settings", Assert.IsType<PushButtonModel>(panel.Slideout[0]).Title);
            Assert.Equal(new[] { "A", "B" }, Assert.IsType<StackModel>(panel.Slideout[1]).Buttons.Select(b => b.Title));
            // Buttons (shortcuts, context, panes) sees the slideout too.
            Assert.Equal(new[] { "Main", "Settings", "A", "B" }, panel.Buttons.Select(b => b.Title));
            Assert.Equal("T.tab/P.panel/99_More.slideout/01_Settings.pushbutton", panel.Buttons[1].BundleKey);
        }

        [Fact]
        public void BundleKey_IsExtensionRelative_WithForwardSlashes()
        {
            MakeStackButton("MyExt", "Tab", "Panel", "02_Set", "01_Add");
            MakeStackButton("MyExt", "Tab", "Panel", "02_Set", "02_Sub");
            var buttons = ParseSingle().Tabs[0].Panels[0].Buttons;
            Assert.Equal("Tab.tab/Panel.panel/02_Set.stack/01_Add.pushbutton", buttons[0].BundleKey);
            Assert.Equal("Tab.tab/Panel.panel/02_Set.stack/02_Sub.pushbutton", buttons[1].BundleKey);
        }

        [Fact]
        public void Pulldown_KeytipOverride_ComesFromBundleYaml()
        {
            MakePulldownButton("MyExt", "T", "P", "M", "01_Show");
            File.WriteAllText(
                Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel", "M.pulldown", "bundle.yaml"),
                "keytip: ME\n");
            var pulldown = Assert.IsType<PulldownModel>(Assert.Single(ParseSingle().Tabs[0].Panels[0].Items));
            Assert.Equal("ME", pulldown.KeyTipOverride);
        }

        [Fact]
        public void IconVariants_AllFourPresent_AreRecorded()
        {
            var dir = MakeButton("MyExt", "T", "P", "A", icon: true);
            foreach (var name in new[] { "icon.dark.png", "icon.small.png", "icon.small.dark.png" })
                File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 137, 80, 78, 71 });

            var b = ParseSingle().Tabs[0].Panels[0].Buttons[0];
            Assert.EndsWith("icon.png", b.IconPath);
            Assert.EndsWith("icon.dark.png", b.DarkIconPath);
            Assert.EndsWith("icon.small.png", b.SmallIconPath);
            Assert.EndsWith("icon.small.dark.png", b.SmallDarkIconPath);
        }

        [Fact]
        public void IconVariants_MissingOnes_FallBackToTheLightLargeIcon()
        {
            MakeButton("MyExt", "T", "P", "A", icon: true);
            var b = ParseSingle().Tabs[0].Panels[0].Buttons[0];

            Assert.Equal(b.IconPath, b.DarkIconPath);
            Assert.Equal(b.IconPath, b.SmallIconPath);
            Assert.Equal(b.IconPath, b.SmallDarkIconPath);
        }

        [Fact]
        public void IconVariants_NoIconAtAll_LeavesEveryPathNull()
        {
            MakeButton("MyExt", "T", "P", "A");
            var b = ParseSingle().Tabs[0].Panels[0].Buttons[0];

            Assert.Null(b.IconPath);
            Assert.Null(b.DarkIconPath);
            Assert.Null(b.SmallIconPath);
            Assert.Null(b.SmallDarkIconPath);
        }

        [Fact]
        public void IconVariants_OnPulldown_AreRecordedToo()
        {
            MakePulldownButton("MyExt", "T", "P", "M", "01_Show");
            var dir = Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel", "M.pulldown");
            foreach (var name in new[] { "icon.png", "icon.small.png" })
                File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 137, 80, 78, 71 });

            var p = Assert.IsType<PulldownModel>(Assert.Single(ParseSingle().Tabs[0].Panels[0].Items));
            Assert.EndsWith("icon.png", p.IconPath);
            Assert.EndsWith("icon.small.png", p.SmallIconPath);
            Assert.Equal(p.IconPath, p.DarkIconPath);   // absent, falls back
        }

        [Fact]
        public void Pulldown_ParsesChildren_AndTitlesFromFolder()
        {
            MakePulldownButton("MyExt", "T", "P", "05_Memory", "01_Show");
            MakePulldownButton("MyExt", "T", "P", "05_Memory", "02_Purge");

            var pulldown = Assert.IsType<PulldownModel>(Assert.Single(ParseSingle().Tabs[0].Panels[0].Items));
            Assert.Equal("Memory", pulldown.Title);
            Assert.Equal(new[] { "Show", "Purge" }, pulldown.Buttons.Select(b => b.Title).ToArray());
        }

        [Fact]
        public void Pulldown_TitleAndTooltip_FromBundleYaml()
        {
            MakePulldownButton("MyExt", "T", "P", "M", "01_Show");
            File.WriteAllText(
                Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel", "M.pulldown", "bundle.yaml"),
                "title: More\ntooltip: Rare memory actions\n");

            var pulldown = Assert.IsType<PulldownModel>(Assert.Single(ParseSingle().Tabs[0].Panels[0].Items));
            Assert.Equal("More", pulldown.Title);
            Assert.Equal("Rare memory actions", pulldown.Tooltip);
        }

        [Fact]
        public void Pulldown_WithNoValidChildren_IsSkipped()
        {
            Directory.CreateDirectory(Path.Combine(_root, "MyExt.extension", "T.tab", "P.panel",
                "Empty.pulldown"));
            MakeButton("MyExt", "T", "P", "Keeper");

            var only = Assert.IsType<PushButtonModel>(Assert.Single(ParseSingle().Tabs[0].Panels[0].Items));
            Assert.Equal("Keeper", only.Title);
        }

        [Fact]
        public void PanelItems_KeepFolderOrder_AcrossButtonsStacksAndPulldowns()
        {
            MakeButton("MyExt", "T", "P", "01_Big");
            MakeStackButton("MyExt", "T", "P", "02_Set", "01_A");
            MakeStackButton("MyExt", "T", "P", "02_Set", "02_B");
            MakePulldownButton("MyExt", "T", "P", "03_More", "01_C");

            var kinds = ParseSingle().Tabs[0].Panels[0].Items.Select(i => i.GetType().Name).ToArray();
            Assert.Equal(new[] { "PushButtonModel", "StackModel", "PulldownModel" }, kinds);
        }

        [Fact]
        public void Context_And_HostVersions_Parse_From_Yaml()
        {
            MakeButton("MyExt", "T", "P", "B",
                yaml: "context: selection & clash-tests\nmin_host_version: 2024\nmax_host_version: 2026\n");
            var button = ParseSingle().Tabs[0].Panels[0].Buttons[0];
            Assert.NotNull(button.ContextRule);
            Assert.Equal("selection & clash-tests", button.ContextRule.Text);
            Assert.Equal(2024, button.MinHostYear);
            Assert.Equal(2026, button.MaxHostYear);
        }

        [Fact]
        public void Context_Conditions_Are_Case_Insensitive()
        {
            // The unknown-condition validator once scanned case-sensitively, so
            // "Selection" matched only "election" and logged a bogus warning.
            MakeButton("MyExt", "T", "P", "B", yaml: "context: Selection & DOC\n");
            var button = ParseSingle().Tabs[0].Panels[0].Buttons[0];
            Assert.NotNull(button.ContextRule);
            Assert.Equal("Selection & DOC", button.ContextRule.Text);
        }

        [Fact]
        public void Missing_Context_Means_Null_Rule_And_No_Gate()
        {
            MakeButton("MyExt", "T", "P", "B");
            var button = ParseSingle().Tabs[0].Panels[0].Buttons[0];
            Assert.Null(button.ContextRule);
            Assert.Null(button.MinHostYear);
            Assert.Null(button.MaxHostYear);
        }

        [Fact]
        public void Malformed_Context_Or_Version_Is_Ignored_Not_Fatal()
        {
            MakeButton("MyExt", "T", "P", "B", yaml: "context: doc &\nmin_host_version: soon\n");
            var button = ParseSingle().Tabs[0].Panels[0].Buttons[0];
            Assert.Null(button.ContextRule);
            Assert.Null(button.MinHostYear);
        }
    }
}
