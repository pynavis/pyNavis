using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PyNavis.Runtime.Ai;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Output;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Everything in config.json that a person is meant to change, in a window, so
    /// nobody has to find a JSON file to turn off a dot.
    ///
    /// It deliberately does NOT edit the three sections the runtime maintains for
    /// itself: shortcuts.bindings belongs to the Shortcuts editor, panes.assignments
    /// to the pane registry, and layout to whichever dialog remembered its width.
    /// A window that rewrote those would fight their owners.
    ///
    /// Each section says what its change costs, because they differ: appearance is a
    /// Reload, the engine paths are a Navisworks restart, and a wrong engine path
    /// stops pyNavis loading at all. Saying so beside the field is the only warning
    /// that arrives before the mistake.
    /// </summary>
    public static class SettingsDialog
    {
        private sealed class Parts
        {
            public Window Window;
            public Tokens T;
            public PyNavisConfig.UserSettings Values;
            public ComboBox Theme;
            public CheckBox ShowConfigMarker;
            public TextBox ConfigMarker;
            public CheckBox ShowMarker;
            public TextBox Marker;
            public CheckBox BareKeys;
            public ListBox Roots;
            public TextBox Lib;
            public TextBox CPython;
            public ComboBox AiProvider;
            public TextBox AiBaseUrl;
            public ComboBox AiModel;
            public PasswordBox AiKey;
            public bool AiKeyTouched;
            public TextBox AiMaxTokens;
            public TextBlock AiStatus;
            public Grid AiFrame;
            public System.Text.StringBuilder AiText = new System.Text.StringBuilder();
            public TextBlock VersionLine;
            public bool Saved;
        }

        private static readonly Dictionary<Window, Parts> Live = new Dictionary<Window, Parts>();

        private static Parts PartsOf(Window window) => Live[window];

        private const string FollowHost = "Follow Navisworks";

        /// <summary>What a save asks of the user, because the keys differ in cost.</summary>
        public enum Outcome
        {
            Cancelled,
            /// <summary>Appearance, shortcuts policy or extension roots changed.</summary>
            NeedsReload,
            /// <summary>An engine path changed, which a Reload cannot pick up.</summary>
            NeedsRestart,
        }

        // ---- script-facing API -------------------------------------------------

        /// <summary>
        /// Reads config.json, shows the window, writes it back on Save, and says what
        /// the change costs. The script-facing entry, so a bundle stays a few lines.
        /// IO and parse failures propagate to the caller to report.
        /// </summary>
        public static Outcome ShowAndSave() => ShowAndSave(null);

        /// <summary>As above, scrolled to one section ("ai") when asked. The API key is
        /// saved to the secret store, never to config.json.</summary>
        public static Outcome ShowAndSave(string section)
        {
            var path = RuntimeHost.UserConfigPath;
            var current = PyNavisConfig.Load(path).ToUserSettings();
            var secrets = SecretStore.Default;

            var window = Build(current, secrets.Has(SecretStore.AiKeyName));
            if (section == "ai")
                window.Loaded += (s, e) => PartsOf(window).AiFrame?.BringIntoView();
            window.ShowDialog();
            var parts = PartsOf(window);
            Live.Remove(window);
            if (!parts.Saved) return Outcome.Cancelled;
            var edited = Collect(parts);
            var enteredKey = EnteredKey(parts);
            if (enteredKey != null) secrets.Set(SecretStore.AiKeyName, enteredKey);

            // Compared before the write, because afterwards there is nothing left to
            // compare against and the restart notice would be a guess.
            var restart = !SameText(edited.PyNavisLibPath, current.PyNavisLibPath)
                          || !SameText(edited.CPythonPath, current.CPythonPath);

            PyNavisConfig.SaveUserSettings(path, edited);
            return restart ? Outcome.NeedsRestart : Outcome.NeedsReload;
        }

        private static bool SameText(string left, string right) =>
            string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.Ordinal);

        /// <summary>
        /// Shows the window; returns the edited settings on Save, or null on cancel.
        /// The caller writes them and decides whether to Reload.
        /// </summary>
        public static PyNavisConfig.UserSettings Show(PyNavisConfig.UserSettings current)
        {
            var window = Build(current);
            window.ShowDialog();
            var parts = PartsOf(window);
            Live.Remove(window);
            return parts.Saved ? Collect(parts) : null;
        }

        public static Window Build(PyNavisConfig.UserSettings current) => Build(current, false);

        /// <summary><paramref name="hasKey"/>: whether a key is already stored, so the
        /// key box can say "saved" instead of looking empty.</summary>
        public static Window Build(PyNavisConfig.UserSettings current, bool hasKey)
        {
            var t = Tokens.Current;
            var values = current ?? new PyNavisConfig.UserSettings();

            var window = new Window
            {
                Title = "pyNavis settings",
                Width = 560,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 760,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = DesignSystem.Brush(t.Surface),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            };
            var parts = new Parts { Window = window, T = t, Values = values };
            Live[window] = parts;

            var page = new StackPanel { Margin = new Thickness(18) };
            page.Children.Add(Appearance(parts));
            page.Children.Add(Shortcuts(parts));
            page.Children.Add(Roots(parts));
            page.Children.Add(Engines(parts));
            page.Children.Add(Assistant(parts, hasKey));
            page.Children.Add(Actions(parts));
            page.Children.Add(About(parts));

            var scroll = new ScrollViewer
            {
                Content = page,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            DesignSystem.SlimScroll(scroll);
            window.Content = scroll;
            FluentChrome.Apply(window);
            return window;
        }

        // ---- sections ----------------------------------------------------------

        private static UIElement Appearance(Parts parts)
        {
            var t = parts.T;
            var body = new StackPanel();

            body.Children.Add(Caption(t, "Theme"));
            parts.Theme = new ComboBox { Width = 200, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
            parts.Theme.Items.Add(FollowHost);
            parts.Theme.Items.Add("Light");
            parts.Theme.Items.Add("Dark");
            parts.Theme.SelectedItem =
                string.Equals(parts.Values.Theme, "dark", StringComparison.OrdinalIgnoreCase) ? "Dark"
                : string.Equals(parts.Values.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "Light"
                : FollowHost;
            body.Children.Add(parts.Theme);

            // Each hint is a checkbox (on or off) beside the glyph it draws. The glyph box
            // is never the switch: empty just means the default glyph.
            parts.ShowMarker = new CheckBox
            {
                Content = "Mark tools that have a keyboard shortcut with",
                IsChecked = parts.Values.ShowShortcutMarker,
                Foreground = DesignSystem.Brush(t.Ink),
                VerticalAlignment = VerticalAlignment.Center,
            };
            parts.Marker = new TextBox { Width = 60, Text = parts.Values.RibbonShortcutMarker ?? "" };
            var markerRow = new StackPanel { Orientation = Orientation.Horizontal };
            markerRow.Children.Add(parts.ShowMarker);
            markerRow.Children.Add(DesignSystem.InputField(t, parts.Marker));
            markerRow.Children.Add(Hint(t, "after the name. The tooltip names the keys either way.",
                new Thickness(8, 0, 0, 0)));
            body.Children.Add(markerRow);

            parts.ShowConfigMarker = new CheckBox
            {
                Content = "Mark tools that have a Shift+Click action with",
                IsChecked = parts.Values.ShowConfigMarker,
                Foreground = DesignSystem.Brush(t.Ink),
                VerticalAlignment = VerticalAlignment.Center,
            };
            parts.ConfigMarker = new TextBox { Width = 60, Text = parts.Values.RibbonConfigMarker ?? "" };
            var configRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            configRow.Children.Add(parts.ShowConfigMarker);
            configRow.Children.Add(DesignSystem.InputField(t, parts.ConfigMarker));
            configRow.Children.Add(Hint(t, "after the name.", new Thickness(8, 0, 0, 0)));
            body.Children.Add(configRow);

            body.Children.Add(Note(t, "Takes effect on the next Reload."));
            return DesignSystem.GroupFrame(t, "Appearance", body);
        }

        private static UIElement Shortcuts(Parts parts)
        {
            var t = parts.T;
            var body = new StackPanel();
            parts.BareKeys = new CheckBox
            {
                Content = "Allow shortcuts without Ctrl or Alt",
                IsChecked = parts.Values.ShortcutsAllowBareKeys,
                Foreground = DesignSystem.Brush(t.Ink),
            };
            body.Children.Add(parts.BareKeys);
            body.Children.Add(Hint(t,
                "Off, a binding must contain Ctrl or Alt. On, a bare letter works and will "
                + "swallow that key everywhere in Navisworks.", new Thickness(24, 4, 0, 0)));
            body.Children.Add(Note(t, "Which tool has which chord lives in the Shortcuts editor."));
            return DesignSystem.GroupFrame(t, "Shortcuts", body);
        }

        private static UIElement Roots(Parts parts)
        {
            var t = parts.T;
            var body = new StackPanel();

            parts.Roots = new ListBox
            {
                Height = 96,
                Background = DesignSystem.Brush(t.Paper),
                Foreground = DesignSystem.Brush(t.Ink),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
            };
            foreach (var path in parts.Values.ExtensionPaths) parts.Roots.Items.Add(path);
            body.Children.Add(parts.Roots);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(DesignSystem.Quiet(t, "Add", () =>
            {
                var picked = FolderPicker.Pick("Choose a folder holding *.extension folders", null);
                if (!string.IsNullOrWhiteSpace(picked)
                    && !parts.Roots.Items.Cast<object>().Any(
                        i => string.Equals(Convert.ToString(i), picked, StringComparison.OrdinalIgnoreCase)))
                    parts.Roots.Items.Add(picked);
            }));
            var remove = DesignSystem.Quiet(t, "Remove", () =>
            {
                if (parts.Roots.SelectedItem != null)
                    parts.Roots.Items.Remove(parts.Roots.SelectedItem);
            });
            remove.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(remove);
            body.Children.Add(buttons);

            body.Children.Add(Note(t,
                "A new folder is picked up on Reload. A *.lib folder inside one needs a "
                + "Navisworks restart."));
            return DesignSystem.GroupFrame(t, "Where extensions are loaded from", body);
        }

        private static UIElement Engines(Parts parts)
        {
            var t = parts.T;
            var body = new StackPanel();

            body.Children.Add(Caption(t, "pynavislib folder"));
            parts.Lib = new TextBox { Text = parts.Values.PyNavisLibPath ?? "" };
            body.Children.Add(DesignSystem.InputField(t, parts.Lib));

            body.Children.Add(Caption(t, "CPython executable"));
            parts.CPython = new TextBox { Text = parts.Values.CPythonPath ?? "" };
            body.Children.Add(DesignSystem.InputField(t, parts.CPython));

            body.Children.Add(Hint(t,
                "Leave either empty to let pyNavis resolve it. CPython is only needed by "
                + "bundles that ask for the cpython engine.", new Thickness(0, 8, 0, 0)));
            body.Children.Add(Warn(t,
                "These two need a Navisworks restart, and a wrong pynavislib folder stops "
                + "pyNavis loading at all. Empty is safer than a guess."));
            return DesignSystem.GroupFrame(t, "Engines", body);
        }

        private static UIElement Assistant(Parts parts, bool hasKey)
        {
            var t = parts.T;
            var ai = parts.Values.Ai ?? new AiSettings();
            var body = new StackPanel();

            body.Children.Add(Caption(t, "Provider"));
            parts.AiProvider = new ComboBox { Width = 200, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
            parts.AiProvider.Items.Add(ProviderAnthropic);
            parts.AiProvider.Items.Add(ProviderOpenAi);
            parts.AiProvider.SelectedItem = ai.Provider == AiProvider.OpenAiCompatible ? ProviderOpenAi : ProviderAnthropic;
            body.Children.Add(parts.AiProvider);

            body.Children.Add(Caption(t, "Base URL"));
            parts.AiBaseUrl = new TextBox { Text = ai.BaseUrl ?? "" };
            body.Children.Add(DesignSystem.InputField(t, parts.AiBaseUrl));
            body.Children.Add(AiHint(parts,
                "Leave empty for the provider's public endpoint. For OpenAI-compatible, this is where "
                + "Azure OpenAI, Ollama or a local server lives, ending in /v1."));

            body.Children.Add(Caption(t, "Model"));
            parts.AiModel = new ComboBox { IsEditable = true, Width = 260, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
            FillModels(parts, ai.Provider);
            parts.AiModel.Text = ai.Model ?? "";
            body.Children.Add(parts.AiModel);
            parts.AiProvider.SelectionChanged += (s, e) =>
            {
                var chosen = Convert.ToString(parts.AiProvider.SelectedItem) == ProviderOpenAi
                    ? AiProvider.OpenAiCompatible : AiProvider.Anthropic;
                FillModels(parts, chosen);
            };
            body.Children.Add(AiHint(parts, "Empty picks the provider's default. Any model name can be typed."));

            body.Children.Add(Caption(t, "API key"));
            parts.AiKey = new PasswordBox { Height = 26, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
            parts.AiKey.PasswordChanged += (s, e) => parts.AiKeyTouched = true;
            var keyRow = new Grid();
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            keyRow.Children.Add(parts.AiKey);
            var test = DesignSystem.Quiet(t, "Test", () => TestConnection(parts));
            test.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(test, 1);
            keyRow.Children.Add(test);
            body.Children.Add(keyRow);
            body.Children.Add(AiHint(parts, hasKey
                ? "A key is saved. Leave the box empty to keep it, or type a new one to replace it."
                : "Stored encrypted for your Windows account in secrets.json, never in config.json. "
                  + "A local server needs no key."));
            parts.AiStatus = Hint(t, "", new Thickness(0, 6, 0, 0));
            body.Children.Add(parts.AiStatus);

            body.Children.Add(Caption(t, "Longest answer (tokens)"));
            parts.AiMaxTokens = new TextBox { Width = 100, Text = ai.MaxTokens.ToString(), HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(DesignSystem.InputField(t, parts.AiMaxTokens));

            body.Children.Add(Note(t, AiHintText(parts,
                "Ask AI is a beta. Every request sends the authoring guide plus your chat to the provider "
                + "you chose here, and nothing else. You can also build tools with any assistant: the "
                + "Copy authoring guide button on the Ask AI panel gives you the same guide to paste "
                + "into the chatbot you already use.")));
            parts.AiFrame = DesignSystem.GroupFrame(t, "AI assistant (beta)", body);
            return parts.AiFrame;
        }

        private const string ProviderAnthropic = "Anthropic";
        private const string ProviderOpenAi = "OpenAI-compatible";

        private static void FillModels(Parts parts, AiProvider provider)
        {
            var typed = parts.AiModel.Text;
            parts.AiModel.Items.Clear();
            foreach (var name in provider == AiProvider.OpenAiCompatible ? AiSettings.OpenAiModels : AiSettings.AnthropicModels)
                parts.AiModel.Items.Add(name);
            parts.AiModel.Text = typed;
        }

        private static TextBlock AiHint(Parts parts, string text) =>
            Hint(parts.T, AiHintText(parts, text), new Thickness(0, 6, 0, 0));

        private static string AiHintText(Parts parts, string text)
        {
            parts.AiText.AppendLine(text);
            return text;
        }

        private static string EnteredKey(Parts parts)
        {
            if (parts.AiKey == null || !parts.AiKeyTouched) return null;
            var key = parts.AiKey.Password?.Trim();
            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static AiSettings CollectAi(Parts parts)
        {
            var ai = new AiSettings
            {
                Provider = Convert.ToString(parts.AiProvider.SelectedItem) == ProviderOpenAi
                    ? AiProvider.OpenAiCompatible : AiProvider.Anthropic,
                BaseUrl = string.IsNullOrWhiteSpace(parts.AiBaseUrl.Text) ? null : parts.AiBaseUrl.Text.Trim(),
                Model = string.IsNullOrWhiteSpace(parts.AiModel.Text) ? null : parts.AiModel.Text.Trim(),
            };
            int tokens;
            ai.MaxTokens = int.TryParse((parts.AiMaxTokens.Text ?? "").Trim(), out tokens) && tokens > 0
                ? tokens : AiSettings.DefaultMaxTokens;
            return ai;
        }

        /// <summary>One tiny request with the settings as they stand in the window,
        /// reported on the status line. Runs off the UI thread; the window stays live.</summary>
        private static async void TestConnection(Parts parts)
        {
            var settings = CollectAi(parts);
            var key = EnteredKey(parts) ?? SecretStore.Default.Get(SecretStore.AiKeyName) ?? "";
            parts.AiStatus.Text = "Testing " + settings.EffectiveModel + " at " + settings.EffectiveBaseUrl + "...";
            parts.AiStatus.Foreground = DesignSystem.Brush(parts.T.Muted);
            try
            {
                var provider = ChatProviders.Create(settings, key);
                var request = new ChatRequest { Model = settings.EffectiveModel, MaxTokens = 16 };
                request.Messages.Add(new ChatMessage("user", "Reply with the single word OK."));
                var reply = new System.Text.StringBuilder();
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45)))
                    await provider.StreamAsync(request, s => reply.Append(s), cts.Token);
                parts.AiStatus.Text = "Connected. The model replied: " + Trim(reply.ToString(), 60);
                parts.AiStatus.Foreground = DesignSystem.Brush(parts.T.Success);
            }
            catch (ProviderException ex)
            {
                parts.AiStatus.Text = ex.Message;
                parts.AiStatus.Foreground = DesignSystem.Brush(parts.T.Error);
            }
            catch (Exception ex)
            {
                parts.AiStatus.Text = "Could not connect: " + ex.Message;
                parts.AiStatus.Foreground = DesignSystem.Brush(parts.T.Error);
            }
        }

        private static string Trim(string text, int max)
        {
            var t = (text ?? "").Trim().Replace("\n", " ");
            return t.Length <= max ? t : t.Substring(0, max) + "...";
        }

        private static UIElement Actions(Parts parts)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };
            var save = DesignSystem.Primary(parts.T, "Save", () =>
            {
                parts.Saved = true;
                parts.Window.Close();
            });
            var cancel = DesignSystem.Secondary(parts.T, "Cancel", () => parts.Window.Close());
            cancel.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(save);
            row.Children.Add(cancel);
            return row;
        }

        /// <summary>The version line at the very bottom, with Copy: the first thing to
        /// paste into a bug report, and the only place in the UI that says which build
        /// this is.</summary>
        private static UIElement About(Parts parts)
        {
            var t = parts.T;
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            parts.VersionLine = Hint(t, PyNavisVersion.Summary(), new Thickness(0));
            parts.VersionLine.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(parts.VersionLine);
            var copy = DesignSystem.Quiet(t, "Copy", () =>
            {
                try { Clipboard.SetText(PyNavisVersion.Summary()); }
                catch (Exception ex) { Log.Error("Could not copy the version line", ex); }
            });
            copy.ToolTip = "Copy the version line for a bug report";
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            return grid;
        }

        // ---- reading the window back -------------------------------------------

        private static PyNavisConfig.UserSettings Collect(Parts parts) =>
            new PyNavisConfig.UserSettings
            {
                Theme = Convert.ToString(parts.Theme.SelectedItem) == "Dark" ? "dark"
                    : Convert.ToString(parts.Theme.SelectedItem) == "Light" ? "light" : null,
                RibbonConfigMarker = parts.ConfigMarker.Text ?? "",
                ShowConfigMarker = parts.ShowConfigMarker.IsChecked == true,
                RibbonShortcutMarker = parts.Marker.Text ?? "",
                ShowShortcutMarker = parts.ShowMarker.IsChecked == true,
                ShortcutsAllowBareKeys = parts.BareKeys.IsChecked == true,
                ExtensionPaths = parts.Roots.Items.Cast<object>()
                    .Select(Convert.ToString).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
                PyNavisLibPath = parts.Lib.Text,
                CPythonPath = parts.CPython.Text,
                Ai = CollectAi(parts),
            };

        // ---- test seams --------------------------------------------------------

        public static PyNavisConfig.UserSettings CollectForTest(Window window) => Collect(PartsOf(window));

        public static void SetThemeForTest(Window window, string label) =>
            PartsOf(window).Theme.SelectedItem = label;

        public static void SetMarkerForTest(Window window, string marker) =>
            PartsOf(window).Marker.Text = marker;

        public static void SetConfigMarkerForTest(Window window, string marker) =>
            PartsOf(window).ConfigMarker.Text = marker;

        public static void SetShowMarkerForTest(Window window, bool show) =>
            PartsOf(window).ShowMarker.IsChecked = show;

        public static void SetShowConfigMarkerForTest(Window window, bool show) =>
            PartsOf(window).ShowConfigMarker.IsChecked = show;

        public static void AddRootForTest(Window window, string path) =>
            PartsOf(window).Roots.Items.Add(path);

        public static int RootCountOf(Window window) => PartsOf(window).Roots.Items.Count;

        public static void SetAiKeyForTest(Window window, string key) =>
            PartsOf(window).AiKey.Password = key;

        public static string EnteredKeyForTest(Window window) => EnteredKey(PartsOf(window));

        public static void SetAiMaxTokensForTest(Window window, string text) =>
            PartsOf(window).AiMaxTokens.Text = text;

        public static string AiSectionTextForTest(Window window) => PartsOf(window).AiText.ToString();

        public static string VersionLineOf(Window window) => PartsOf(window).VersionLine.Text;

        // ---- small chrome ------------------------------------------------------

        private static TextBlock Caption(Tokens t, string text)
        {
            var block = DesignSystem.Text(text, 12, t.Muted);
            block.Margin = new Thickness(0, 12, 0, 5);
            return block;
        }

        private static TextBlock Hint(Tokens t, string text, Thickness margin)
        {
            var block = DesignSystem.Text(text, 11.5, t.Muted);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = margin;
            return block;
        }

        private static TextBlock Note(Tokens t, string text)
        {
            var block = DesignSystem.Text(text, 11.5, t.Muted);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 12, 0, 0);
            return block;
        }

        private static TextBlock Warn(Tokens t, string text)
        {
            var block = DesignSystem.Text(text, 11.5, t.Ink);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 10, 0, 0);
            return block;
        }
    }
}
