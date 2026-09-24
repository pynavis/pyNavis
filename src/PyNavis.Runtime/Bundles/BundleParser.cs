using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// Folder tree -> ExtensionModel. Pure filesystem/data logic - no ribbon, no engine -
    /// so it is fully unit-testable. Layout:
    ///   Name.extension\Name.tab\Name.panel\Name.pushbutton\{script.py, icon.png, bundle.yaml}
    /// Metadata precedence: bundle.yaml > script (__title__/docstring) > folder name.
    /// </summary>
    public static class BundleParser
    {
        /// <summary>Parses every *.extension directory directly under <paramref name="rootDir"/>.</summary>
        public static List<ExtensionModel> ParseRoot(string rootDir)
        {
            var extensions = new List<ExtensionModel>();
            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) return extensions;

            foreach (var dir in SortedDirs(rootDir, "*.extension"))
            {
                var ext = ParseExtension(dir);
                if (ext != null) extensions.Add(ext);
            }
            return extensions;
        }

        /// <summary>Every *.lib directory directly under the root: shared-python-only
        /// extensions whose folder joins the global engine search path.</summary>
        public static List<string> FindLibraries(string rootDir)
        {
            var libs = new List<string>();
            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) return libs;
            foreach (var dir in SortedDirs(rootDir, "*.lib")) libs.Add(dir);
            return libs;
        }

        public static ExtensionModel ParseExtension(string extensionDir)
        {
            if (string.IsNullOrEmpty(extensionDir) || !Directory.Exists(extensionDir)) return null;

            var ext = new ExtensionModel
            {
                Directory = extensionDir,
                Name = BaseName(extensionDir, ".extension"),
            };

            var lib = Path.Combine(extensionDir, "lib");
            if (Directory.Exists(lib)) ext.LibDirectory = lib;

            var startup = Path.Combine(extensionDir, "startup.py");
            if (File.Exists(startup)) ext.StartupScriptPath = startup;

            var extYaml = ReadYaml(Path.Combine(extensionDir, "extension.yaml"));
            if (extYaml.TryGetValue("name", out var name)) ext.Name = name;
            if (extYaml.TryGetValue("engine", out var engine)) ext.DefaultEngineId = engine;

            var hooksDir = Path.Combine(extensionDir, "hooks");
            if (Directory.Exists(hooksDir))
            {
                foreach (var file in Directory.GetFiles(hooksDir, "*.py")
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var eventName = Path.GetFileNameWithoutExtension(file);
                    if (!Events.EventNames.TryParse(eventName, out var evt))
                    {
                        Log.Error($"Hook '{file}': unknown event '{eventName}' - skipped. " +
                                  "Valid names: " + string.Join(", ", Events.EventNames.All));
                        continue;
                    }
                    var hook = new HookModel
                    {
                        EventName = Events.EventNames.NameOf(evt),
                        ScriptPath = file,
                        EngineId = ext.DefaultEngineId,
                        ExtensionName = ext.Name,
                    };
                    hook.SearchPaths.Add(hooksDir);
                    if (ext.LibDirectory != null) hook.SearchPaths.Add(ext.LibDirectory);
                    ext.Hooks.Add(hook);
                }
            }

            foreach (var tabDir in SortedDirs(extensionDir, "*.tab"))
            {
                var tabName = BaseName(tabDir, ".tab");
                var tab = new TabModel
                {
                    Title = ToTitle(tabName),
                    Id = "PYNAVIS_TAB_" + Sanitize(ext.Name + "_" + tabName),
                };

                foreach (var panelDir in SortedDirs(tabDir, "*.panel"))
                {
                    var panel = new PanelModel { Title = ToTitle(BaseName(panelDir, ".panel")) };
                    ParsePanelItems(panelDir, ext, panel.Items, panel.Slideout);
                    if (panel.Items.Count > 0 || panel.Slideout.Count > 0) tab.Panels.Add(panel);
                }
                if (tab.Panels.Count > 0) ext.Tabs.Add(tab);
            }
            return ext;
        }

        /// <summary>Reads one folder of panel items into <paramref name="items"/>; a
        /// *.slideout folder's contents go to <paramref name="slideout"/> instead.</summary>
        private static void ParsePanelItems(string dir, ExtensionModel ext,
            List<PanelItem> items, List<PanelItem> slideout)
        {
            foreach (var itemDir in SortedDirs(dir, "*"))
            {
                var itemName = Path.GetFileName(itemDir);
                if (itemName.EndsWith(".slideout", StringComparison.OrdinalIgnoreCase))
                {
                    // Only one level: a slideout inside a slideout is a typo, so its
                    // nested folders fall through the suffix checks and are skipped.
                    if (slideout == null)
                        Log.Error($"Slideout '{itemDir}' sits inside another slideout - skipped.");
                    else
                        ParsePanelItems(itemDir, ext, slideout, null);
                }
                else if (itemName.EndsWith(".splitpushbutton", StringComparison.OrdinalIgnoreCase))
                {
                    var split = ParsePulldown(itemDir, ext, PulldownKind.SplitFixed, ".splitpushbutton");
                    if (split != null) items.Add(split);
                }
                else if (itemName.EndsWith(".dockpane", StringComparison.OrdinalIgnoreCase))
                {
                    var pane = ParseDockPane(itemDir, ext);
                    if (pane != null) items.Add(pane);
                }
                else if (itemName.EndsWith(".pushbutton", StringComparison.OrdinalIgnoreCase))
                {
                    var button = ParsePushButton(itemDir, ext);
                    if (button != null) items.Add(button);
                }
                else if (itemName.EndsWith(".nobutton", StringComparison.OrdinalIgnoreCase))
                {
                    var button = ParsePushButton(itemDir, ext, ".nobutton", noUi: true);
                    if (button != null) items.Add(button);
                }
                else if (itemName.EndsWith(".urlbutton", StringComparison.OrdinalIgnoreCase))
                {
                    var url = ParseUrlButton(itemDir);
                    if (url != null) items.Add(url);
                }
                else if (itemName.EndsWith(".linkbutton", StringComparison.OrdinalIgnoreCase))
                {
                    var link = ParseLinkButton(itemDir);
                    if (link != null) items.Add(link);
                }
                else if (itemName.EndsWith(".splitbutton", StringComparison.OrdinalIgnoreCase))
                {
                    var split = ParsePulldown(itemDir, ext, PulldownKind.SplitLastUsed, ".splitbutton");
                    if (split != null) items.Add(split);
                }
                else if (itemName.EndsWith(".toggle", StringComparison.OrdinalIgnoreCase))
                {
                    var button = ParsePushButton(itemDir, ext, ".toggle", toggle: true);
                    if (button != null) items.Add(button);
                }
                else if (itemName.EndsWith(".stack", StringComparison.OrdinalIgnoreCase))
                {
                    var stack = ParseStack(itemDir, ext);
                    if (stack != null) items.Add(stack);
                }
                else if (itemName.EndsWith(".pulldown", StringComparison.OrdinalIgnoreCase))
                {
                    var pulldown = ParsePulldown(itemDir, ext);
                    if (pulldown != null) items.Add(pulldown);
                }
                else if (itemName.EndsWith(".smartbutton", StringComparison.OrdinalIgnoreCase))
                {
                    var button = ParsePushButton(itemDir, ext, ".smartbutton");
                    if (button != null) { button.IsSmart = true; items.Add(button); }
                }
                else if (LooksLikeABundle(itemName))
                {
                    Report(ext, $"'{itemDir}' is not a bundle kind pyNavis knows (check the folder suffix) - skipped.");
                }
            }
        }

        // A dotted folder name inside a panel is meant to be a bundle; "assets", "lib" or
        // "__pycache__" are just folders. Dot-first names (.git, .vs) are tooling.
        private static bool LooksLikeABundle(string folderName) =>
            folderName.IndexOf('.') > 0;

        private static void Report(ExtensionModel ext, string problem)
        {
            Log.Error(problem);
            ext?.Problems.Add(problem);
        }

        /// <summary>Stacks and pulldowns hold *.pushbutton only; any other bundle-looking
        /// folder in one is a mistake the author needs to hear about.</summary>
        private static void ReportNonPushButtons(string containerDir, ExtensionModel ext)
        {
            foreach (var dir in SortedDirs(containerDir, "*"))
            {
                var name = Path.GetFileName(dir);
                if (LooksLikeABundle(name) && !name.EndsWith(".pushbutton", StringComparison.OrdinalIgnoreCase))
                    Report(ext, $"'{dir}' is ignored: a {Path.GetExtension(containerDir).TrimStart('.')} holds only *.pushbutton folders.");
            }
        }

        private static StackModel ParseStack(string stackDir, ExtensionModel ext)
        {
            var stack = new StackModel { Directory = stackDir };
            ReportNonPushButtons(stackDir, ext);
            foreach (var buttonDir in SortedDirs(stackDir, "*.pushbutton"))
            {
                var button = ParsePushButton(buttonDir, ext);
                if (button != null) stack.Buttons.Add(button);
            }

            // A RibbonRowPanel holds two or three small rows; anything else would render
            // wrong, so say so at parse time rather than silently on the ribbon.
            if (stack.Buttons.Count < 2 || stack.Buttons.Count > 3)
            {
                Log.Error($"Stack '{stackDir}' holds {stack.Buttons.Count} button(s); a stack needs 2 or 3 - skipped.");
                return null;
            }
            return stack;
        }

        private static PulldownModel ParsePulldown(string pulldownDir, ExtensionModel ext,
            PulldownKind kind = PulldownKind.Menu, string suffix = ".pulldown")
        {
            var yaml = ReadYaml(Path.Combine(pulldownDir, "bundle.yaml"));
            var icons = IconSet(pulldownDir);
            var pulldown = new PulldownModel
            {
                Directory = pulldownDir,
                Kind = kind,
                Title = yaml.TryGetValue("title", out var t) ? t : ToTitle(BaseName(pulldownDir, suffix)),
                Tooltip = yaml.TryGetValue("tooltip", out var tip) ? tip : null,
                KeyTipOverride = yaml.TryGetValue("keytip", out var keyTip) ? keyTip : null,
                IconPath = icons.large,
                DarkIconPath = icons.dark,
                SmallIconPath = icons.small,
                SmallDarkIconPath = icons.smallDark,
            };

            ReportNonPushButtons(pulldownDir, ext);
            foreach (var buttonDir in SortedDirs(pulldownDir, "*.pushbutton"))
            {
                var button = ParsePushButton(buttonDir, ext);
                if (button != null) pulldown.Buttons.Add(button);
            }

            if (pulldown.Buttons.Count == 0)
            {
                Log.Error($"Pulldown '{pulldownDir}' has no usable pushbuttons - skipped.");
                return null;
            }
            return pulldown;
        }

        private static PushButtonModel ParsePushButton(string buttonDir, ExtensionModel ext, string suffix = ".pushbutton", bool noUi = false, bool toggle = false)
        {
            var script = Path.Combine(buttonDir, "script.py");
            if (!File.Exists(script))
            {
                Report(ext, $"'{buttonDir}' has no script.py - skipped.");
                return null;
            }

            var yaml = ReadYaml(Path.Combine(buttonDir, "bundle.yaml"));
            ScriptMetadata scanned;
            try { scanned = ScriptMetadata.Scan(TextFiles.Read(script)); }
            catch { scanned = new ScriptMetadata(); }

            var icons = IconSet(buttonDir);
            var config = Path.Combine(buttonDir, "config.py");
            var button = new PushButtonModel
            {
                Directory = buttonDir,
                ScriptPath = script,
                ConfigScriptPath = File.Exists(config) ? config : null,
                Title = yaml.TryGetValue("title", out var t) ? t : (scanned.Title ?? ToTitle(BaseName(buttonDir, suffix))),
                Tooltip = yaml.TryGetValue("tooltip", out var tip) ? tip : scanned.Docstring,
                EngineId = yaml.TryGetValue("engine", out var eng) ? eng : ext.DefaultEngineId,
                Shortcut = yaml.TryGetValue("shortcut", out var chord) ? chord : null,
                KeyTipOverride = yaml.TryGetValue("keytip", out var keyTip) ? keyTip : null,
                BundleKey = RelativeKey(ext.Directory, buttonDir),
                IconPath = icons.large,
                DarkIconPath = icons.dark,
                SmallIconPath = icons.small,
                SmallDarkIconPath = icons.smallDark,
                NoUi = noUi,
            };

            button.SearchPaths.Add(buttonDir);
            if (ext.LibDirectory != null) button.SearchPaths.Add(ext.LibDirectory);

            if (yaml.TryGetValue("context", out var context))
            {
                try
                {
                    button.ContextRule = ContextRule.Parse(context);
                    // Unknown names are legal but evaluate false; tell the author now, not at runtime.
                    var known = new HashSet<string>(ContextRule.KnownConditions, StringComparer.OrdinalIgnoreCase);
                    // Case-insensitive to match ContextRule's own case-folded tokenizing:
                    // a case-sensitive scan reads "Selection" as the token "election".
                    foreach (Match m in Regex.Matches(context, "[a-z][a-z0-9-]*", RegexOptions.IgnoreCase))
                        if (!known.Contains(m.Value))
                            Log.Error($"Bundle '{buttonDir}': unknown context condition '{m.Value}'.");
                }
                catch (FormatException ex)
                {
                    Log.Error($"Bundle '{buttonDir}': bad context rule - {ex.Message} Button stays always enabled.");
                }
            }
            if (yaml.TryGetValue("min_host_version", out var minVer))
            {
                if (int.TryParse(minVer, out var minYear)) button.MinHostYear = minYear;
                else Log.Error($"Bundle '{buttonDir}': min_host_version '{minVer}' is not a year - ignored.");
            }
            if (yaml.TryGetValue("max_host_version", out var maxVer))
            {
                if (int.TryParse(maxVer, out var maxYear)) button.MaxHostYear = maxYear;
                else Log.Error($"Bundle '{buttonDir}': max_host_version '{maxVer}' is not a year - ignored.");
            }

            if (toggle)
            {
                string Find(string name)
                {
                    var path = Path.Combine(buttonDir, name);
                    return File.Exists(path) ? path : null;
                }
                button.IsToggle = true;
                button.OffIconPath = Find("icon.off.png") ?? icons.large;
                button.OffDarkIconPath = Find("icon.off.dark.png") ?? button.OffIconPath;
                button.OnIconPath = Find("icon.on.png") ?? icons.large;
                button.OnDarkIconPath = Find("icon.on.dark.png") ?? button.OnIconPath;
                button.IconPath = button.OffIconPath;      // initial render: off
                button.DarkIconPath = button.OffDarkIconPath;
            }

            return button;
        }

        private static UrlButtonModel ParseUrlButton(string dir)
        {
            var yaml = ReadYaml(Path.Combine(dir, "bundle.yaml"));
            if (!yaml.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            {
                Log.Error($"Urlbutton '{dir}' has no url: in bundle.yaml - skipped.");
                return null;
            }
            var icons = IconSet(dir);
            return new UrlButtonModel
            {
                Directory = dir,
                Url = url.Trim(),
                Title = yaml.TryGetValue("title", out var t) ? t : ToTitle(BaseName(dir, ".urlbutton")),
                Tooltip = yaml.TryGetValue("tooltip", out var tip) ? tip : url.Trim(),
                KeyTipOverride = yaml.TryGetValue("keytip", out var keyTip) ? keyTip : null,
                IconPath = icons.large, DarkIconPath = icons.dark,
                SmallIconPath = icons.small, SmallDarkIconPath = icons.smallDark,
            };
        }

        private static LinkButtonModel ParseLinkButton(string dir)
        {
            var yaml = ReadYaml(Path.Combine(dir, "bundle.yaml"));
            if (!yaml.TryGetValue("plugin", out var plugin) || string.IsNullOrWhiteSpace(plugin))
            {
                Log.Error($"Linkbutton '{dir}' has no plugin: in bundle.yaml - skipped.");
                return null;
            }
            var icons = IconSet(dir);
            return new LinkButtonModel
            {
                Directory = dir,
                PluginId = plugin.Trim(),
                Title = yaml.TryGetValue("title", out var t) ? t : ToTitle(BaseName(dir, ".linkbutton")),
                Tooltip = yaml.TryGetValue("tooltip", out var tip) ? tip : null,
                KeyTipOverride = yaml.TryGetValue("keytip", out var keyTip) ? keyTip : null,
                IconPath = icons.large, DarkIconPath = icons.dark,
                SmallIconPath = icons.small, SmallDarkIconPath = icons.smallDark,
            };
        }

        private static DockPaneModel ParseDockPane(string dir, ExtensionModel ext)
        {
            var xaml = Path.Combine(dir, "pane.xaml");
            if (!File.Exists(xaml))
            {
                Log.Error($"Dockpane '{dir}' has no pane.xaml - skipped.");
                return null;
            }

            var yaml = ReadYaml(Path.Combine(dir, "bundle.yaml"));
            var icons = IconSet(dir);
            var script = Path.Combine(dir, "script.py");
            var pane = new DockPaneModel
            {
                Directory = dir,
                XamlPath = xaml,
                ScriptPath = File.Exists(script) ? script : null,
                Title = yaml.TryGetValue("title", out var t) ? t : ToTitle(BaseName(dir, ".dockpane")),
                Tooltip = yaml.TryGetValue("tooltip", out var tip) ? tip : null,
                EngineId = yaml.TryGetValue("engine", out var eng) ? eng : ext.DefaultEngineId,
                Shortcut = yaml.TryGetValue("shortcut", out var chord) ? chord : null,
                KeyTipOverride = yaml.TryGetValue("keytip", out var keyTip) ? keyTip : null,
                BundleKey = RelativeKey(ext.Directory, dir),
                IconPath = icons.large,
                DarkIconPath = icons.dark,
                SmallIconPath = icons.small,
                SmallDarkIconPath = icons.smallDark,
            };

            pane.SearchPaths.Add(dir);
            if (ext.LibDirectory != null) pane.SearchPaths.Add(ext.LibDirectory);
            return pane;
        }

        /// <summary>Bundle path relative to its extension folder, forward slashes: the
        /// stable identity shortcut config keys use.</summary>
        private static string RelativeKey(string extensionDir, string bundleDir)
        {
            var full = Path.GetFullPath(bundleDir);
            var root = Path.GetFullPath(extensionDir).TrimEnd(Path.DirectorySeparatorChar);
            var rel = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar)
                : full;
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>The four icon variants for a bundle folder; missing ones fall back to
        /// the plain icon.png so bundles that predate the variants keep working.</summary>
        private static (string large, string dark, string small, string smallDark) IconSet(string dir)
        {
            string Find(string name)
            {
                var path = Path.Combine(dir, name);
                return File.Exists(path) ? path : null;
            }

            var large = Find("icon.png");
            return (large,
                    Find("icon.dark.png") ?? large,
                    Find("icon.small.png") ?? large,
                    Find("icon.small.dark.png") ?? large);
        }

        // A folder the user cannot list (ACL, over-long path, a network or cloud root that
        // dropped mid-scan) is skipped with a log line: one bad folder must cost only
        // itself, never the whole ribbon.
        private static IEnumerable<string> SortedDirs(string parent, string pattern)
        {
            try
            {
                return Directory.GetDirectories(parent, pattern).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not list '{parent}' for {pattern} - skipped.", ex);
                return Enumerable.Empty<string>();
            }
        }

        private static Dictionary<string, string> ReadYaml(string path)
        {
            try
            {
                return File.Exists(path)
                    ? BundleYaml.Parse(TextFiles.Read(path))
                    : new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Log.Error($"Could not read '{path}' - its title, tooltip, context and shortcut are ignored.", ex);
                return new Dictionary<string, string>();
            }
        }

        private static string BaseName(string dir, string suffix)
        {
            var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }

        // Folders may carry an NN_ prefix purely to order them on the panel; it is not
        // part of the title. Two or more digits, so a tool genuinely called "1_Thing"
        // keeps its name (a bundle.yaml title always wins anyway).
        private static readonly System.Text.RegularExpressions.Regex OrderPrefix =
            new System.Text.RegularExpressions.Regex(@"^\d{2,}_");

        private static string ToTitle(string folderName) =>
            OrderPrefix.Replace(folderName, "").Replace('_', ' ');

        private static string Sanitize(string value)
        {
            var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            return new string(chars);
        }
    }
}
