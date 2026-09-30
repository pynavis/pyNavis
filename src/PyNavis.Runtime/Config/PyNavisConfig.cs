using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Ai;

namespace PyNavis.Runtime.Config
{
    /// <summary>
    /// Full-fidelity reader of %APPDATA%\pyNavis\config.json (the loader reads the same
    /// file with a minimal regex; this is the real parser). Unknown keys are ignored.
    /// </summary>
    public class PyNavisConfig
    {
        /// <summary>Directories scanned for *.extension folders ("extensions" array).</summary>
        public List<string> ExtensionPaths { get; } = new List<string>();

        /// <summary>Directory containing the pynavis python package ("pynavislib" key); null if absent.</summary>
        public string PyNavisLibPath { get; private set; }

        /// <summary>CPython dll or install dir ("cpython" key); null = auto-detect.</summary>
        public string CPythonPath { get; private set; }

        /// <summary>UI theme override ("theme" key: "dark"/"light"); null = detect from the host.</summary>
        public string Theme { get; private set; }

        /// <summary>"shortcuts.allowBareKeys": permit bindings without Ctrl/Alt. Default false.</summary>
        public bool ShortcutsAllowBareKeys { get; private set; }

        /// <summary>"shortcuts.bindings": bundle key -> chord string; null value disables a default.</summary>
        public Dictionary<string, string> ShortcutBindings { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"panes.assignments": dockpane bundle key -> 1-based slot.</summary>
        public Dictionary<string, int> PaneAssignments { get; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"panes.extraSlots": how many slots the generated satellite adds. 0 = none.</summary>
        public int ExtraPaneSlots { get; private set; }

        /// <summary>"ribbon.configMarker": the glyph appended to the caption of a button
        /// whose bundle has a config.py (the Shift+Click secondary action). Default the
        /// wide Shift arrow, U+1F845. Whether it shows at all is "ribbon.showConfigMarker",
        /// a separate boolean, so an empty glyph never doubles as an off switch. The older
        /// boolean "ribbon.configDot" is read as that switch.</summary>
        public string RibbonConfigMarker { get; private set; } = DefaultConfigMarker;

        public const string DefaultConfigMarker = "\U0001F845";

        /// <summary>"ribbon.showConfigMarker": whether the config marker is drawn. Default
        /// false: a stranger reads an unexplained glyph as a typo, and the tooltip already
        /// says Shift+Click.</summary>
        public bool ShowConfigMarker { get; private set; } = DefaultShowConfigMarker;

        public const bool DefaultShowConfigMarker = false;

        /// <summary>"ribbon.shortcutMarker": the glyph appended to the caption of a button
        /// that has a resolved chord. Default a dot. A marker starting with a newline puts
        /// itself on its own caption line.</summary>
        public string RibbonShortcutMarker { get; private set; } = DefaultShortcutMarker;

        public const string DefaultShortcutMarker = "\u25CF";

        /// <summary>"ribbon.showShortcutMarker": whether the shortcut marker is drawn.
        /// Default true.</summary>
        public bool ShowShortcutMarker { get; private set; } = DefaultShowShortcutMarker;

        public const bool DefaultShowShortcutMarker = true;

        /// <summary>"ai": provider, endpoint, model and answer length for the assistant.
        /// The key is not here; SecretStore keeps it.</summary>
        public AiSettings Ai { get; private set; } = new AiSettings();

        /// <summary>"updates.check": ask GitHub once a day whether a newer release is out.
        /// Default true; one anonymous request, nothing about the user is sent.</summary>
        public bool UpdatesCheck { get; private set; } = true;

        /// <summary>"updates.installOnClose": download a newer release in the background
        /// and install it when Navisworks closes. Default true.</summary>
        public bool UpdatesInstallOnClose { get; private set; } = true;

        /// <summary>"updates.skip": the release the user chose to skip; null for none.</summary>
        public string UpdatesSkip { get; private set; }

        /// <summary>"layout.&lt;dialog&gt;.&lt;part&gt;": remembered pane widths, keyed
        /// "dialog/part" (e.g. "viewpoints/tree"). Only positive finite numbers load.</summary>
        public Dictionary<string, double> Layout { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rewrites ONLY layout.&lt;dialog&gt; in config.json, preserving every other key
        /// (including other dialogs' layouts). Values are rounded to whole pixels;
        /// non-positive ones drop the part. Same failure contract as SaveShortcutBindings.
        /// </summary>
        public static void SaveLayout(string path, string dialog, IReadOnlyDictionary<string, double> parts)
        {
            var root = ReadRootForUpdate(path);

            var layout = root.TryGetValue("layout", out var existing) && existing is Dictionary<string, object> section
                ? section : new Dictionary<string, object>();

            var map = new Dictionary<string, object>();
            foreach (var pair in parts)
                if (pair.Value > 0 && !double.IsInfinity(pair.Value) && !double.IsNaN(pair.Value))
                    map[pair.Key] = (int)Math.Round(pair.Value);

            if (map.Count == 0) layout.Remove(dialog);
            else layout[dialog] = map;

            if (layout.Count == 0) root.Remove("layout");
            else root["layout"] = layout;

            WriteRoot(path, root);
        }

        /// <summary>
        /// The existing config as a mutable tree, for a read-modify-write save. A missing
        /// or empty file is an empty root. A file that exists but cannot be read or parsed
        /// THROWS rather than yielding an empty root: every saver writes the whole tree
        /// back, so "start fresh" here would drop the extension paths, the for-life pane
        /// slots and the runtimeNNNN key the loader boots from, all over one stray comma
        /// or a transient lock. IO errors propagate as they are.
        /// </summary>
        private static Dictionary<string, object> ReadRootForUpdate(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();

            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text)
                    ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                Log.Error($"Config '{path}' is not valid JSON - refusing to overwrite it.", ex);
                throw new InvalidDataException(
                    $"'{path}' is not valid JSON, so nothing was saved. Fix the file or delete it.", ex);
            }
        }

        /// <summary>
        /// Writes beside the target and swaps it in, so a crash or a scan mid-write can
        /// never leave a truncated config.json (the loader regex-reads this same file).
        /// </summary>
        private static void WriteRoot(string path, Dictionary<string, object> root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonPretty.Write(root));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>
        /// Everything the Settings window edits. Deliberately NOT the whole file: the
        /// keys the runtime maintains for itself (shortcuts.bindings, panes.assignments,
        /// layout) stay where they are, because a window that rewrote them would fight
        /// the Shortcuts editor and the pane registry for ownership.
        /// </summary>
        public sealed class UserSettings
        {
            /// <summary>"light", "dark", or null/empty to follow Navisworks.</summary>
            public string Theme;
            public string RibbonConfigMarker = DefaultConfigMarker;
            public bool ShowConfigMarker = DefaultShowConfigMarker;
            public string RibbonShortcutMarker = DefaultShortcutMarker;
            public bool ShowShortcutMarker = DefaultShowShortcutMarker;
            public bool ShortcutsAllowBareKeys;
            public List<string> ExtensionPaths = new List<string>();
            /// <summary>Null or empty removes the key and lets the runtime resolve it.</summary>
            public string PyNavisLibPath;
            public string CPythonPath;
            public AiSettings Ai = new AiSettings();
            public bool UpdatesCheck = true;
            public bool UpdatesInstallOnClose = true;
            public string UpdatesSkip;
        }

        /// <summary>The current file as a UserSettings, for a window to edit and hand back.</summary>
        public UserSettings ToUserSettings() => new UserSettings
        {
            Theme = Theme,
            RibbonConfigMarker = RibbonConfigMarker,
            ShowConfigMarker = ShowConfigMarker,
            RibbonShortcutMarker = RibbonShortcutMarker,
            ShowShortcutMarker = ShowShortcutMarker,
            ShortcutsAllowBareKeys = ShortcutsAllowBareKeys,
            ExtensionPaths = new List<string>(ExtensionPaths),
            PyNavisLibPath = PyNavisLibPath,
            CPythonPath = CPythonPath,
            Ai = Ai.Clone(),
            UpdatesCheck = UpdatesCheck,
            UpdatesInstallOnClose = UpdatesInstallOnClose,
            UpdatesSkip = UpdatesSkip,
        };

        /// <summary>
        /// Rewrites the keys the Settings window owns, preserving every other key in the
        /// file. Same failure contract as SaveShortcutBindings: IO errors and an
        /// unparsable existing file propagate.
        ///
        /// A key set to null or empty is REMOVED rather than written blank, so "follow
        /// Navisworks" and "resolve this yourself" are the absence of a key, which is
        /// what a fresh install looks like.
        /// </summary>
        public static void SaveUserSettings(string path, UserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var root = ReadRootForUpdate(path);

            SetOrRemove(root, "theme", settings.Theme);
            SetOrRemove(root, "pynavislib", settings.PyNavisLibPath);
            SetOrRemove(root, "cpython", settings.CPythonPath);

            var roots = new List<object>();
            foreach (var item in settings.ExtensionPaths)
                if (!string.IsNullOrWhiteSpace(item)) roots.Add(item.Trim());
            if (roots.Count == 0) root.Remove("extensions");
            else root["extensions"] = roots;

            // Only the two keys this window owns; bindings belong to the Shortcuts editor.
            var shortcuts = root.TryGetValue("shortcuts", out var sc)
                && sc is Dictionary<string, object> existingShortcuts
                ? existingShortcuts : new Dictionary<string, object>();
            if (settings.ShortcutsAllowBareKeys) shortcuts["allowBareKeys"] = true;
            else shortcuts.Remove("allowBareKeys");
            if (shortcuts.Count == 0) root.Remove("shortcuts");
            else root["shortcuts"] = shortcuts;

            var ribbon = root.TryGetValue("ribbon", out var rb)
                && rb is Dictionary<string, object> existingRibbon
                ? existingRibbon : new Dictionary<string, object>();
            // Written only when it differs from the default, so a file that never
            // touched these keys stays as clean as it was.
            ribbon.Remove("configDot");                 // the pre-marker boolean, superseded
            // An empty glyph is not "off" (the boolean is): it falls back to the default.
            var configMarker = string.IsNullOrEmpty(settings.RibbonConfigMarker)
                ? DefaultConfigMarker : settings.RibbonConfigMarker;
            if (configMarker == DefaultConfigMarker) ribbon.Remove("configMarker");
            else ribbon["configMarker"] = configMarker;
            if (settings.ShowConfigMarker == DefaultShowConfigMarker) ribbon.Remove("showConfigMarker");
            else ribbon["showConfigMarker"] = settings.ShowConfigMarker;
            var marker = string.IsNullOrEmpty(settings.RibbonShortcutMarker)
                ? DefaultShortcutMarker : settings.RibbonShortcutMarker;
            if (marker == DefaultShortcutMarker) ribbon.Remove("shortcutMarker");
            else ribbon["shortcutMarker"] = marker;
            if (settings.ShowShortcutMarker == DefaultShowShortcutMarker) ribbon.Remove("showShortcutMarker");
            else ribbon["showShortcutMarker"] = settings.ShowShortcutMarker;
            if (ribbon.Count == 0) root.Remove("ribbon");
            else root["ribbon"] = ribbon;

            var ai = root.TryGetValue("ai", out var aiRaw)
                && aiRaw is Dictionary<string, object> existingAi
                ? existingAi : new Dictionary<string, object>();
            (settings.Ai ?? new AiSettings()).WriteSection(ai);
            if (ai.Count == 0) root.Remove("ai");
            else root["ai"] = ai;

            // Written only where they differ from the defaults, like the ribbon hints.
            var updates = UpdatesSection(root);
            if (settings.UpdatesCheck) updates.Remove("check");
            else updates["check"] = false;
            if (settings.UpdatesInstallOnClose) updates.Remove("installOnClose");
            else updates["installOnClose"] = false;
            if (string.IsNullOrWhiteSpace(settings.UpdatesSkip)) updates.Remove("skip");
            else updates["skip"] = settings.UpdatesSkip.Trim();
            PutUpdatesSection(root, updates);

            WriteRoot(path, root);
        }

        /// <summary>
        /// Rewrites ONLY updates.skip: the "Skip this version" button acts at once, like
        /// the toast it answers, rather than waiting for the Settings window's Save. Same
        /// failure contract as SaveShortcutBindings. Null or empty clears it.
        /// </summary>
        public static void SaveUpdateSkip(string path, string version)
        {
            var root = ReadRootForUpdate(path);
            var updates = UpdatesSection(root);
            if (string.IsNullOrWhiteSpace(version)) updates.Remove("skip");
            else updates["skip"] = version.Trim();
            PutUpdatesSection(root, updates);
            WriteRoot(path, root);
        }

        private static Dictionary<string, object> UpdatesSection(Dictionary<string, object> root) =>
            root.TryGetValue("updates", out var existing) && existing is Dictionary<string, object> section
                ? section : new Dictionary<string, object>();

        private static void PutUpdatesSection(Dictionary<string, object> root, Dictionary<string, object> updates)
        {
            if (updates.Count == 0) root.Remove("updates");
            else root["updates"] = updates;
        }

        private static void SetOrRemove(Dictionary<string, object> root, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) root.Remove(key);
            else root[key] = value.Trim();
        }

        /// <summary>
        /// Rewrites ONLY shortcuts.bindings in config.json, preserving every other key
        /// (engines, extensions, theme, allowBareKeys, anything future). An empty map
        /// removes the bindings key, and the shortcuts section too when nothing else
        /// is under it. IO failures propagate to the caller, and so does an unparsable
        /// existing file (InvalidDataException): see ReadRootForUpdate.
        /// </summary>
        public static void SaveShortcutBindings(string path, IReadOnlyDictionary<string, string> bindings)
        {
            var root = ReadRootForUpdate(path);

            var shortcuts = root.TryGetValue("shortcuts", out var sc) && sc is Dictionary<string, object> existing
                ? existing : new Dictionary<string, object>();

            if (bindings.Count == 0)
            {
                shortcuts.Remove("bindings");
            }
            else
            {
                var map = new Dictionary<string, object>();
                foreach (var pair in bindings)
                    map[pair.Key] = pair.Value;
                shortcuts["bindings"] = map;
            }

            if (shortcuts.Count == 0) root.Remove("shortcuts");
            else root["shortcuts"] = shortcuts;

            WriteRoot(path, root);
        }

        /// <summary>
        /// Rewrites ONLY the "panes" section, preserving every other key. Same failure
        /// contract as SaveShortcutBindings: IO errors and an unparsable file propagate.
        /// </summary>
        public static void SavePaneAssignments(string path,
            IReadOnlyDictionary<string, int> assignments, int extraSlots)
        {
            var root = ReadRootForUpdate(path);

            var panes = root.TryGetValue("panes", out var existing) && existing is Dictionary<string, object> section
                ? section : new Dictionary<string, object>();

            var map = new Dictionary<string, object>();
            foreach (var pair in assignments)
                if (pair.Value > 0) map[pair.Key] = pair.Value;

            if (map.Count == 0) panes.Remove("assignments");
            else panes["assignments"] = map;

            if (extraSlots > 0) panes["extraSlots"] = extraSlots;
            else panes.Remove("extraSlots");

            if (panes.Count == 0) root.Remove("panes");
            else root["panes"] = panes;

            WriteRoot(path, root);
        }

        /// <summary>Loads config; a missing or unreadable file yields defaults, never throws.</summary>
        public static PyNavisConfig Load(string path)
        {
            var config = new PyNavisConfig();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return config;

                var data = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (data != null && data.TryGetValue("extensions", out var raw)
                    && raw is IEnumerable list && !(raw is string))
                {
                    foreach (var item in list)
                        if (item is string s && !string.IsNullOrWhiteSpace(s))
                            config.ExtensionPaths.Add(s);
                }
                if (data != null && data.TryGetValue("pynavislib", out var lib)
                    && lib is string libPath && !string.IsNullOrWhiteSpace(libPath))
                {
                    config.PyNavisLibPath = libPath;
                }
                if (data != null && data.TryGetValue("cpython", out var cpy)
                    && cpy is string cpyPath && !string.IsNullOrWhiteSpace(cpyPath))
                {
                    config.CPythonPath = cpyPath;
                }
                if (data != null && data.TryGetValue("theme", out var theme)
                    && theme is string themeName && !string.IsNullOrWhiteSpace(themeName))
                {
                    config.Theme = themeName;
                }
                if (data != null && data.TryGetValue("shortcuts", out var sc)
                    && sc is Dictionary<string, object> shortcuts)
                {
                    if (shortcuts.TryGetValue("allowBareKeys", out var bare) && bare is bool flag)
                        config.ShortcutsAllowBareKeys = flag;

                    if (shortcuts.TryGetValue("bindings", out var b)
                        && b is Dictionary<string, object> bindings)
                    {
                        // Keys written before a folder lost its NN_ prefix still name the
                        // same tool: normalise on the way in, and the next save writes
                        // the clean form.
                        foreach (var pair in bindings)
                            config.ShortcutBindings[Bundles.BundleKeys.Normalize(pair.Key)] = pair.Value as string; // null stays null = disabled
                    }
                }
                if (data != null && data.TryGetValue("panes", out var panes)
                    && panes is Dictionary<string, object> paneSection)
                {
                    if (paneSection.TryGetValue("extraSlots", out var extra))
                    {
                        int parsed;
                        if (int.TryParse(Convert.ToString(extra), out parsed) && parsed > 0)
                            config.ExtraPaneSlots = parsed;
                    }
                    if (paneSection.TryGetValue("assignments", out var rawAssignments)
                        && rawAssignments is Dictionary<string, object> assignments)
                    {
                        foreach (var pair in assignments)
                        {
                            int slot;
                            if (int.TryParse(Convert.ToString(pair.Value), out slot) && slot > 0)
                                config.PaneAssignments[Bundles.BundleKeys.Normalize(pair.Key)] = slot;
                        }
                    }
                }
                if (data != null && data.TryGetValue("ribbon", out var ribbon)
                    && ribbon is Dictionary<string, object> ribbonSection)
                {
                    // The glyphs: an empty string is not a setting, it keeps the default.
                    // On or off is the boolean beside each, never the glyph.
                    if (ribbonSection.TryGetValue("configMarker", out var cm) && cm is string configText
                        && configText.Length > 0)
                        config.RibbonConfigMarker = configText;
                    if (ribbonSection.TryGetValue("showConfigMarker", out var showCm) && showCm is bool showConfig)
                        config.ShowConfigMarker = showConfig;
                    else if (ribbonSection.TryGetValue("configDot", out var dot) && dot is bool showDot)
                        config.ShowConfigMarker = showDot;        // the older name for the switch

                    if (ribbonSection.TryGetValue("shortcutMarker", out var marker)
                        && marker is string markerText && markerText.Length > 0)
                        config.RibbonShortcutMarker = markerText;
                    if (ribbonSection.TryGetValue("showShortcutMarker", out var showSm) && showSm is bool showShortcut)
                        config.ShowShortcutMarker = showShortcut;
                }
                if (data != null && data.TryGetValue("ai", out var aiSection)
                    && aiSection is Dictionary<string, object> aiMap)
                {
                    config.Ai = AiSettings.FromSection(aiMap);
                }
                if (data != null && data.TryGetValue("updates", out var up)
                    && up is Dictionary<string, object> updates)
                {
                    if (updates.TryGetValue("check", out var check) && check is bool checkOn)
                        config.UpdatesCheck = checkOn;
                    if (updates.TryGetValue("installOnClose", out var onClose) && onClose is bool installOn)
                        config.UpdatesInstallOnClose = installOn;
                    if (updates.TryGetValue("skip", out var skip) && skip is string skipped
                        && !string.IsNullOrWhiteSpace(skipped))
                        config.UpdatesSkip = skipped.Trim();
                }
                if (data != null && data.TryGetValue("layout", out var layout)
                    && layout is Dictionary<string, object> layoutSection)
                {
                    foreach (var dialog in layoutSection)
                    {
                        if (!(dialog.Value is Dictionary<string, object> parts)) continue;
                        foreach (var part in parts)
                        {
                            double width;
                            if (double.TryParse(Convert.ToString(part.Value, CultureInfo.InvariantCulture),
                                    NumberStyles.Float, CultureInfo.InvariantCulture, out width)
                                && width > 0 && !double.IsInfinity(width))
                                config.Layout[dialog.Key + "/" + part.Key] = width;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load config '{path}' - using defaults.", ex);
            }
            return config;
        }
    }
}
