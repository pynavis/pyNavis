using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PyNavis.Runtime.Ai;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The Ask AI dock panel: a header that names the tool being edited (with a picker
    /// for the ones already made), a transcript laid out the way every chat is laid out
    /// (you on the right in a bubble, the model on the left in plain text with its code
    /// in monospace cards), a proposal card with Create or Update, a composer with
    /// Attach selection, and a footer that always offers the authoring guide for any
    /// other assistant. All logic is in AiSession; this is the WPF over it.
    ///
    /// One instance lives for the Navisworks session (see LiveContent): a Reload rebuilds
    /// the pane's content, and losing the conversation on the very Reload that Create
    /// triggers would make the feature unusable.
    /// </summary>
    public static class AiChatPane
    {
        public const string GuideUrl = "https://pynavis.com/authoring/ai-assistant.html";
        public const string BetaLine =
            "Ask AI is a beta. It is new and lightly tested. Read what it proposes before you click Create.";
        public const string FooterLine =
            "You can build tools with any assistant. Copy the authoring guide and paste it into the chatbot you already use.";
        public const string WaitingText = "Thinking";
        public const string PickerPrompt = "Edit an existing tool";
        public const string ComposerHint = "Describe the tool you want. Ctrl+Enter sends.";

        private sealed class Parts
        {
            public Grid Root;
            public Tokens T;
            public AiSession Session;
            public Func<bool> HasKey;
            public Action OpenSettings;
            public TextBlock ToolLabel;
            public ComboBox Picker;
            public List<GeneratedTool> PickerTools = new List<GeneratedTool>();
            public bool PickerFilling;
            public StackPanel Transcript;
            public ScrollViewer Scroll;
            public StackPanel EmptyPanel;
            public TextBlock EmptyState;
            public Button EmptyAction;
            public Border Card;
            public StackPanel CardBody;
            public TextBox Composer;
            public TextBlock Placeholder;
            public CheckBox Attach;
            public Button Send;
            public Button Cancel;
            public Button NewTool;
            public Button CopyGuide;
            public TextBlock Footer;
            public CancellationTokenSource Cts;
            public MessageView Streaming;
            public FrameworkElement LastUser;
            public FrameworkElement LastAssistant;
            public bool Ready;
        }

        private sealed class MessageView
        {
            public FrameworkElement Container;
            public StackPanel Body;
            public TextBlock Live;
            public string Text = "";
            public DispatcherTimer Waiting;
        }

        private static readonly Dictionary<UIElement, Parts> Live = new Dictionary<UIElement, Parts>();
        private static Parts _session;          // the one instance kept across Reloads

        private static Parts PartsOf(UIElement root) => Live[root];

        // ---- entry points ----------------------------------------------------------

        /// <summary>The pane content for the running Navisworks: built once, reused on
        /// every Reload (detached from the old pane first).</summary>
        public static UIElement LiveContent()
        {
            if (_session == null)
            {
                _session = PartsOf(Build(AiSession.Live(),
                    () => SecretStore.Default.Has(SecretStore.AiKeyName),
                    () =>
                    {
                        try
                        {
                            var outcome = SettingsDialog.ShowAndSave("ai");
                            if (outcome != SettingsDialog.Outcome.Cancelled)
                                Toast.Show("success", "Settings saved", null);
                        }
                        catch (Exception ex)
                        {
                            Toast.Show("error", "Could not save your settings", ex.Message);
                        }
                    }));
            }
            var root = _session.Root;
            if (root.Parent is Panel parent) parent.Children.Remove(root);
            else if (root.Parent is ContentControl holder) holder.Content = null;
            else if (root.Parent is Decorator decorator) decorator.Child = null;
            RefreshKeyState(_session);
            FillPicker(_session);
            return root;
        }

        public static UIElement Build(AiSession session, Func<bool> hasKey, Action openSettings)
        {
            var t = Tokens.Current;
            var p = new Parts
            {
                T = t, Session = session, HasKey = hasKey ?? (() => true), OpenSettings = openSettings ?? (() => { }),
            };
            var root = new Grid { Background = DesignSystem.Brush(t.Paper) };
            TextElement.SetFontFamily(root, new FontFamily("Segoe UI Variable Text, Segoe UI"));
            p.Root = root;
            Live[root] = p;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });        // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // transcript
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });        // proposal card
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });        // composer
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });        // footer

            root.Children.Add(Place(Header(p), 0));
            root.Children.Add(Place(TranscriptArea(p), 1));
            root.Children.Add(Place(ProposalCard(p), 2));
            root.Children.Add(Place(ComposerArea(p), 3));
            root.Children.Add(Place(FooterArea(p), 4));

            session.TextStreamed += delta => root.Dispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => AppendToStreaming(p, delta)));

            FillPicker(p);
            RefreshKeyState(p);
            return root;
        }

        private static UIElement Place(UIElement element, int row)
        {
            Grid.SetRow(element, row);
            return element;
        }

        // ---- header ------------------------------------------------------------------

        private static UIElement Header(Parts p)
        {
            var t = p.T;
            var border = new Border
            {
                BorderBrush = DesignSystem.Brush(t.Line),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 8, 8),
                Background = DesignSystem.Brush(t.Surface),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            p.ToolLabel = DesignSystem.Text("New tool", 12.5, t.Ink, FontWeights.SemiBold);
            p.ToolLabel.VerticalAlignment = VerticalAlignment.Center;
            p.ToolLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            p.ToolLabel.TextWrapping = TextWrapping.NoWrap;
            grid.Children.Add(p.ToolLabel);

            p.Picker = new ComboBox
            {
                Height = 26,
                MinWidth = 150,
                MaxWidth = 220,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Continue a tool Ask AI made earlier: its files are put in front of the model "
                        + "and your next message asks for a change.",
            };
            p.Picker.SelectionChanged += (s, e) => PickerChanged(p);
            Grid.SetColumn(p.Picker, 1);
            grid.Children.Add(p.Picker);

            p.NewTool = DesignSystem.Quiet(t, "New tool", () => StartOver(p));
            p.NewTool.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(p.NewTool, 2);
            grid.Children.Add(p.NewTool);
            border.Child = grid;
            return border;
        }

        /// <summary>Lists the generated tools; hidden until there is one.</summary>
        private static void FillPicker(Parts p)
        {
            p.PickerFilling = true;
            try
            {
                List<GeneratedTool> tools;
                try { tools = p.Session.Writer.Tools(); }
                catch (Exception ex)
                {
                    Log.Error("Could not list the generated tools", ex);
                    tools = new List<GeneratedTool>();
                }
                p.PickerTools = tools;
                p.Picker.Items.Clear();
                p.Picker.Items.Add(PickerPrompt);
                foreach (var tool in tools) p.Picker.Items.Add(tool.Title);
                var current = p.Session.CurrentBundleDir;
                var index = current == null ? -1 : tools.FindIndex(
                    x => string.Equals(x.Directory, current, StringComparison.OrdinalIgnoreCase));
                p.Picker.SelectedIndex = index < 0 ? 0 : index + 1;
                p.Picker.Visibility = tools.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            finally
            {
                p.PickerFilling = false;
            }
        }

        private static void PickerChanged(Parts p)
        {
            if (p.PickerFilling || p.Session.IsBusy) return;
            var index = p.Picker.SelectedIndex - 1;
            if (index < 0 || index >= p.PickerTools.Count) return;
            var tool = p.PickerTools[index];
            if (string.Equals(tool.Directory, p.Session.CurrentBundleDir, StringComparison.OrdinalIgnoreCase)) return;

            p.Session.OpenTool(tool);
            p.Transcript.Children.Clear();
            p.Card.Visibility = Visibility.Collapsed;
            p.ToolLabel.Text = "Editing " + tool.Title;
            ShowEmptyState(p, false);
            AddSystemLine(p, "Editing \"" + tool.Title + "\". Describe the change you want, or send its last error.");
            ShowErrorButton(p);
            p.Composer.Focus();
        }

        // ---- transcript ----------------------------------------------------------------

        private static UIElement TranscriptArea(Parts p)
        {
            var t = p.T;
            var host = new Grid();
            p.Transcript = new StackPanel { Margin = new Thickness(12, 10, 12, 6) };
            p.Scroll = new ScrollViewer
            {
                Content = p.Transcript,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            DesignSystem.SlimScroll(p.Scroll);
            host.Children.Add(p.Scroll);

            p.EmptyPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(28),
                MaxWidth = 340,
            };
            var mark = new Border
            {
                Width = 40, Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = DesignSystem.Brush(t.Surface),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                Child = Badge(t, "AI", 13),
            };
            p.EmptyPanel.Children.Add(mark);
            var title = DesignSystem.Text("Ask AI", 15, t.Ink, FontWeights.SemiBold);
            title.TextAlignment = TextAlignment.Center;
            title.Margin = new Thickness(0, 0, 0, 6);
            p.EmptyPanel.Children.Add(title);
            p.EmptyState = DesignSystem.Text("", 12.5, t.Muted);
            p.EmptyState.TextWrapping = TextWrapping.Wrap;
            p.EmptyState.TextAlignment = TextAlignment.Center;
            p.EmptyPanel.Children.Add(p.EmptyState);
            p.EmptyAction = DesignSystem.Secondary(t, "Open AI settings", () => { p.OpenSettings(); RefreshKeyState(p); });
            p.EmptyAction.Margin = new Thickness(0, 14, 0, 0);
            p.EmptyAction.HorizontalAlignment = HorizontalAlignment.Center;
            p.EmptyPanel.Children.Add(p.EmptyAction);
            host.Children.Add(p.EmptyPanel);
            return host;
        }

        private static TextBlock Badge(Tokens t, string text, double size)
        {
            var badge = DesignSystem.Text(text, size, t.Ink, FontWeights.SemiBold);
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.TextWrapping = TextWrapping.NoWrap;
            return badge;
        }

        // ---- proposal card -------------------------------------------------------------

        private static UIElement ProposalCard(Parts p)
        {
            var t = p.T;
            p.CardBody = new StackPanel();
            var rail = new Border
            {
                Width = 3,
                Background = DesignSystem.Brush(DesignSystem.Accent),
                CornerRadius = new CornerRadius(2, 0, 0, 2),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(rail);
            var inner = new Border { Padding = new Thickness(12, 10, 12, 10), Child = p.CardBody };
            Grid.SetColumn(inner, 1);
            grid.Children.Add(inner);
            p.Card = new Border
            {
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = DesignSystem.Brush(t.Surface),
                Margin = new Thickness(12, 0, 12, 8),
                Child = grid,
                Visibility = Visibility.Collapsed,
            };
            return p.Card;
        }

        // ---- composer ------------------------------------------------------------------

        private static UIElement ComposerArea(Parts p)
        {
            var t = p.T;
            var shell = new Border
            {
                Margin = new Thickness(12, 0, 12, 8),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Background = DesignSystem.Brush(t.Paper),
                Padding = new Thickness(0),
            };
            var stack = new StackPanel();

            var editor = new Grid();
            p.Composer = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60,
                MaxHeight = 160,
                FontSize = 13,
                Foreground = DesignSystem.Brush(t.Ink),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 4),
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FocusVisualStyle = null,
            };
            p.Composer.TextChanged += (s, e) => { UpdatePlaceholder(p); UpdateSendState(p); };
            // Enter is a newline (AcceptsReturn); Ctrl+Enter sends. PreviewKeyDown,
            // because the TextBox marks Enter handled before KeyDown ever fires.
            p.Composer.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    e.Handled = true;
                    if (p.Send.IsEnabled) SendClicked(p);
                }
            };
            editor.Children.Add(p.Composer);
            p.Placeholder = DesignSystem.Text(ComposerHint, 13, t.Muted);
            p.Placeholder.Margin = new Thickness(13, 10, 10, 0);
            p.Placeholder.IsHitTestVisible = false;
            p.Placeholder.VerticalAlignment = VerticalAlignment.Top;
            editor.Children.Add(p.Placeholder);
            stack.Children.Add(editor);

            var bar = new Grid { Margin = new Thickness(8, 0, 8, 8) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            p.Attach = new CheckBox
            {
                Content = "Attach selection",
                FontSize = 12,
                Foreground = DesignSystem.Brush(t.Muted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Sends the selected items' category and property NAMES with your message, so "
                        + "the tool spells them right. No values, no file name, no geometry: nothing from "
                        + "the project itself.",
            };
            bar.Children.Add(p.Attach);
            p.Cancel = DesignSystem.Quiet(t, "Cancel", () => p.Cts?.Cancel());
            p.Cancel.Visibility = Visibility.Collapsed;
            p.Cancel.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(p.Cancel, 1);
            bar.Children.Add(p.Cancel);
            p.Send = DesignSystem.Primary(t, "Send", () => SendClicked(p));
            p.Send.ToolTip = "Ctrl+Enter";
            p.Send.MinWidth = 72;
            Grid.SetColumn(p.Send, 2);
            bar.Children.Add(p.Send);
            stack.Children.Add(bar);

            shell.Child = stack;
            p.Composer.GotKeyboardFocus += (s, e) => shell.BorderBrush = DesignSystem.Brush(DesignSystem.Accent);
            p.Composer.LostKeyboardFocus += (s, e) => shell.BorderBrush = DesignSystem.Brush(t.LineStrong);
            UpdatePlaceholder(p);
            return shell;
        }

        private static void UpdatePlaceholder(Parts p) =>
            p.Placeholder.Visibility = string.IsNullOrEmpty(p.Composer.Text) ? Visibility.Visible : Visibility.Collapsed;

        // ---- footer ----------------------------------------------------------------------

        private static UIElement FooterArea(Parts p)
        {
            var t = p.T;
            var border = new Border
            {
                BorderBrush = DesignSystem.Brush(t.Line),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 6, 6, 6),
                Background = DesignSystem.Brush(t.Surface),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            p.Footer = DesignSystem.Text(FooterLine, 11, t.Muted);
            p.Footer.TextWrapping = TextWrapping.Wrap;
            p.Footer.VerticalAlignment = VerticalAlignment.Center;
            p.Footer.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(p.Footer);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            p.CopyGuide = DesignSystem.Quiet(t, "Copy guide", () => CopyGuide(p));
            p.CopyGuide.FontSize = 12;
            row.Children.Add(p.CopyGuide);
            var docs = DesignSystem.Quiet(t, "Docs", () => OpenUrl(GuideUrl));
            docs.FontSize = 12;
            row.Children.Add(docs);
            Grid.SetColumn(row, 1);
            grid.Children.Add(row);
            border.Child = grid;
            return border;
        }

        // ---- behaviour -------------------------------------------------------------------

        private static void RefreshKeyState(Parts p)
        {
            bool hasKey;
            try { hasKey = p.HasKey(); } catch { hasKey = false; }
            var settingsNeeded = !hasKey && !LooksLocal(p);
            p.EmptyState.Text = settingsNeeded
                ? "Ask AI needs an API key before it can talk to a model. Open AI settings, pick a provider "
                  + "and paste your key. A local server with no key works too: choose OpenAI-compatible and "
                  + "give its base URL."
                : BetaLine + " Describe the tool you want, in plain words. Tick Attach selection when the "
                  + "tool depends on property names, so the model sees the real ones (names only, never values).";
            p.EmptyAction.Visibility = settingsNeeded ? Visibility.Visible : Visibility.Collapsed;
            p.Ready = !settingsNeeded;
            UpdateSendState(p);
        }

        /// <summary>An OpenAI-compatible endpoint that is not api.openai.com is a local or
        /// private server; those run without a key.</summary>
        private static bool LooksLocal(Parts p)
        {
            var settings = p.Session.CurrentSettings;
            return settings.Provider == AiProvider.OpenAiCompatible
                && !settings.EffectiveBaseUrl.Contains("api.openai.com");
        }

        private static void UpdateSendState(Parts p)
        {
            p.Send.IsEnabled = p.Ready && !p.Session.IsBusy && !string.IsNullOrWhiteSpace(p.Composer.Text);
            p.Cancel.Visibility = p.Session.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            p.Composer.IsEnabled = !p.Session.IsBusy;
            p.NewTool.IsEnabled = !p.Session.IsBusy;
            p.Picker.IsEnabled = !p.Session.IsBusy;
        }

        private static async void SendClicked(Parts p)
        {
            var text = p.Composer.Text;
            if (string.IsNullOrWhiteSpace(text) || p.Session.IsBusy || !p.Ready) return;
            p.Composer.Clear();
            ShowEmptyState(p, false);
            AddUserBubble(p, text.Trim());
            if (p.Attach.IsChecked == true) AddSystemLine(p, "Selection attached.");
            var streaming = AddAssistant(p);
            p.Card.Visibility = Visibility.Collapsed;

            p.Cts = new CancellationTokenSource();
            UpdateSendState(p);
            TurnOutcome outcome;
            try
            {
                outcome = await p.Session.SendAsync(text, p.Attach.IsChecked == true, p.Cts.Token);
            }
            finally
            {
                p.Cts.Dispose();
                p.Cts = null;
                UpdateSendState(p);
            }
            FinishTurn(p, streaming, outcome);
        }

        private static async void SendErrorClicked(Parts p)
        {
            if (p.Session.IsBusy || p.Session.LastError == null) return;
            AddUserBubble(p, "The tool failed when I ran it. Here is the error.");
            var streaming = AddAssistant(p);
            p.Card.Visibility = Visibility.Collapsed;
            p.Cts = new CancellationTokenSource();
            UpdateSendState(p);
            TurnOutcome outcome;
            try { outcome = await p.Session.SendLastErrorAsync(p.Cts.Token); }
            finally { p.Cts.Dispose(); p.Cts = null; UpdateSendState(p); }
            FinishTurn(p, streaming, outcome);
        }

        private static void FinishTurn(Parts p, MessageView streaming, TurnOutcome outcome)
        {
            StopWaiting(p, streaming);
            p.Streaming = null;
            if (outcome.Error != null)
            {
                p.Transcript.Children.Remove(streaming.Container);
                var error = AddErrorLine(p, outcome.Error);
                if (outcome.IsAuthFailure)
                {
                    var fix = DesignSystem.Secondary(p.T, "Open AI settings", () => { p.OpenSettings(); RefreshKeyState(p); });
                    fix.Margin = new Thickness(0, 8, 0, 0);
                    fix.HorizontalAlignment = HorizontalAlignment.Left;
                    error.Children.Add(fix);
                }
                return;
            }
            if (outcome.Cancelled)
            {
                if (streaming.Text.Length == 0) p.Transcript.Children.Remove(streaming.Container);
                else AddSystemLine(p, "Cut short: you cancelled.");
                return;
            }
            RenderAssistant(streaming, outcome.Text, p);
            if (outcome.Result != null && outcome.Result.HitLengthLimit)
                AddSystemLine(p, "The answer hit the length limit. Raise \"Longest answer\" in AI settings and ask again.");
            if (outcome.Result != null && outcome.Result.Refused)
                AddSystemLine(p, "The model declined this request.");
            if (outcome.Proposal != null && outcome.Proposal.IsComplete) ShowCard(p, outcome.Proposal);
            else if (outcome.Proposal != null && outcome.Proposal.RefusedFiles.Count > 0)
                AddSystemLine(p, "Ignored files the tool may not have: " + string.Join(", ", outcome.Proposal.RefusedFiles));
            ScrollToEnd(p);
        }

        private static void ShowCard(Parts p, Proposal proposal)
        {
            var t = p.T;
            p.CardBody.Children.Clear();
            var revising = p.Session.IsRevising;
            var title = DesignSystem.Text(proposal.Title, 13, t.Ink, FontWeights.SemiBold);
            p.CardBody.Children.Add(title);
            var files = string.Join(", ", Proposal.AllowedFiles.Where(f => proposal.Files.ContainsKey(f)));
            var sub = DesignSystem.Text(files + (revising ? ". Rewrites the tool in place." : ". A new button on the AI tab."), 11.5, t.Muted);
            sub.Margin = new Thickness(0, 2, 0, 10);
            p.CardBody.Children.Add(sub);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var apply = DesignSystem.Primary(t, revising ? "Update tool" : "Create tool", () => ApplyClicked(p));
            row.Children.Add(apply);
            var view = DesignSystem.Quiet(t, "View files", () => ViewFiles(p, proposal));
            view.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(view);
            p.CardBody.Children.Add(row);
            p.Card.Visibility = Visibility.Visible;
        }

        private static void ApplyClicked(Parts p)
        {
            if (!p.Session.CanApply) return;
            string bundle;
            var wasRevising = p.Session.IsRevising;
            try
            {
                bundle = p.Session.Apply();
            }
            catch (Exception ex)
            {
                Log.Error("Could not write the generated tool", ex);
                AddErrorLine(p, "Could not write the tool: " + ex.Message);
                return;
            }
            p.Card.Visibility = Visibility.Collapsed;
            p.ToolLabel.Text = "Editing " + p.Session.CurrentTitle;
            AddSystemLine(p, wasRevising
                ? "Updated. Click the button on the AI tab to run it again."
                : "Created. Find \"" + p.Session.CurrentTitle + "\" on the AI tab once the ribbon has reloaded.");
            FillPicker(p);
            ShowErrorButton(p);
            // Off the click handler: the Reload rebuilds this very pane's content.
            p.Root.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try { RuntimeHost.Reload(); }
                catch (Exception ex) { Log.Error("Reload after creating a tool failed", ex); }
            }));
            Log.Info("AI tool written: " + bundle);
        }

        private static void ShowErrorButton(Parts p)
        {
            if (p.Session.LastError == null) return;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            line.Children.Add(DesignSystem.Secondary(p.T, "Send last error to AI", () => SendErrorClicked(p)));
            p.Transcript.Children.Add(line);
        }

        /// <summary>Called when the pane is shown again: a failed run since the last
        /// write earns the "Send last error" button.</summary>
        public static void OnShown()
        {
            if (_session == null) return;
            if (_session.Session.LastError != null
                && !_session.Transcript.Children.OfType<StackPanel>().Any(s => s.Children.OfType<Button>().Any()))
                ShowErrorButton(_session);
        }

        private static void ViewFiles(Parts p, Proposal proposal)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var name in Proposal.AllowedFiles)
                if (proposal.Files.TryGetValue(name, out var body))
                    sb.Append("# ").Append(name).Append('\n').Append(body.TrimEnd()).Append("\n\n");
            Dialogs.Alert(sb.ToString().TrimEnd(), proposal.Title, sb.ToString());
        }

        private static void StartOver(Parts p)
        {
            if (p.Session.IsBusy) return;
            p.Session.Reset();
            p.Transcript.Children.Clear();
            p.Card.Visibility = Visibility.Collapsed;
            p.ToolLabel.Text = "New tool";
            ShowEmptyState(p, true);
            FillPicker(p);
            RefreshKeyState(p);
            p.Composer.Focus();
        }

        private static void CopyGuide(Parts p)
        {
            var text = PromptBuilder.OutputRules + p.Session.AuthoringPackText;
            try
            {
                Clipboard.SetText(text);
                Toast.Show("success", "Authoring guide copied",
                    "Paste it into any assistant, then ask for the tool you want.");
            }
            catch (Exception ex)
            {
                Toast.Show("error", "Could not copy the guide", ex.Message);
            }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex) { Toast.Show("error", "Could not open the browser", ex.Message); }
        }

        // ---- transcript rendering -----------------------------------------------------

        private static void ShowEmptyState(Parts p, bool visible) =>
            p.EmptyPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>You, on the right, in a bubble on the surface tint.</summary>
        private static void AddUserBubble(Parts p, string text)
        {
            var t = p.T;
            var block = DesignSystem.Text(text, 12.5, t.Ink);
            block.TextWrapping = TextWrapping.Wrap;
            var bubble = new Border
            {
                Child = block,
                Background = DesignSystem.Brush(t.Surface),
                BorderBrush = DesignSystem.Brush(t.Line),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10, 10, 2, 10),
                Padding = new Thickness(11, 7, 11, 8),
                Margin = new Thickness(36, 4, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            p.Transcript.Children.Add(bubble);
            p.LastUser = bubble;
            ScrollToEnd(p);
        }

        /// <summary>The model, on the left: a small AI mark beside plain text, its code in
        /// monospace cards. Starts as the waiting line until the first token lands.</summary>
        private static MessageView AddAssistant(Parts p)
        {
            var t = p.T;
            var row = new Grid { Margin = new Thickness(0, 4, 24, 6), HorizontalAlignment = HorizontalAlignment.Left };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mark = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = DesignSystem.Brush(t.Surface),
                BorderBrush = DesignSystem.Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0),
                Child = Badge(t, "AI", 9.5),
            };
            row.Children.Add(mark);
            var body = new StackPanel();
            Grid.SetColumn(body, 1);
            row.Children.Add(body);

            var view = new MessageView { Container = row, Body = body };
            view.Live = DesignSystem.Text("", 12.5, t.Ink);
            view.Live.TextWrapping = TextWrapping.Wrap;
            view.Live.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(view.Live);
            p.Transcript.Children.Add(row);
            p.LastAssistant = row;
            p.Streaming = view;
            StartWaiting(p, view);
            ScrollToEnd(p);
            return view;
        }

        /// <summary>The first request carries the whole authoring guide, so the first
        /// token can take several seconds; an empty row reads as nothing happening.</summary>
        private static void StartWaiting(Parts p, MessageView view)
        {
            var dots = 0;
            view.Live.Foreground = DesignSystem.Brush(p.T.Muted);
            view.Live.Text = WaitingText + "...";
            view.Waiting = new DispatcherTimer(DispatcherPriority.Background, p.Root.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(400),
            };
            view.Waiting.Tick += (s, e) =>
            {
                dots = (dots + 1) % 4;
                view.Live.Text = WaitingText + new string('.', dots);
            };
            view.Waiting.Start();
        }

        private static void StopWaiting(Parts p, MessageView view)
        {
            if (view.Waiting == null) return;
            view.Waiting.Stop();
            view.Waiting = null;
            view.Live.Foreground = DesignSystem.Brush(p.T.Ink);
            view.Live.Text = view.Text;
        }

        private static void AppendToStreaming(Parts p, string delta)
        {
            var view = p.Streaming;
            if (view == null || view.Live == null) return;
            view.Text += delta;
            StopWaiting(p, view);
            view.Live.Text = view.Text;
            ScrollToEnd(p);
        }

        /// <summary>Replaces the streamed plain text with prose and code blocks laid
        /// out separately, the code in a monospace card.</summary>
        private static void RenderAssistant(MessageView view, string text, Parts p)
        {
            view.Text = text;
            view.Body.Children.Remove(view.Live);
            view.Live = null;
            var t = p.T;
            var inCode = false;
            var buffer = new System.Text.StringBuilder();
            void Flush()
            {
                var chunk = buffer.ToString().Trim('\n');
                buffer.Clear();
                if (chunk.Length == 0) return;
                if (inCode)
                {
                    var code = new TextBox
                    {
                        Text = chunk,
                        IsReadOnly = true,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 11.5,
                        Foreground = DesignSystem.Brush(t.Ink),
                        Background = DesignSystem.Brush(t.Surface),
                        BorderBrush = DesignSystem.Brush(t.Line),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 4, 0, 6),
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        MaxHeight = 220,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    };
                    view.Body.Children.Add(code);
                }
                else
                {
                    var block = DesignSystem.Text(chunk, 12.5, t.Ink);
                    block.TextWrapping = TextWrapping.Wrap;
                    block.Margin = new Thickness(0, 3, 0, 3);
                    view.Body.Children.Add(block);
                }
            }
            foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
                {
                    Flush();
                    inCode = !inCode;
                    continue;
                }
                buffer.Append(line).Append('\n');
            }
            Flush();
        }

        /// <summary>A quiet centred note: created, attached, cancelled.</summary>
        private static void AddSystemLine(Parts p, string text)
        {
            var line = DesignSystem.Text(text, 11, p.T.Muted);
            line.TextWrapping = TextWrapping.Wrap;
            line.TextAlignment = TextAlignment.Center;
            line.HorizontalAlignment = HorizontalAlignment.Center;
            line.Margin = new Thickness(24, 6, 24, 6);
            p.Transcript.Children.Add(line);
            ScrollToEnd(p);
        }

        private static StackPanel AddErrorLine(Parts p, string text)
        {
            var t = p.T;
            var body = new StackPanel();
            var block = DesignSystem.Text(text, 12, t.Error);
            block.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(block);
            var box = new Border
            {
                Child = body,
                Background = DesignSystem.Brush(t.ErrorWashBg),
                BorderBrush = DesignSystem.Brush(t.ErrorWashLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 24, 6),
            };
            p.Transcript.Children.Add(box);
            ScrollToEnd(p);
            return body;
        }

        private static void ScrollToEnd(Parts p) => p.Scroll?.ScrollToEnd();

        // ---- test seams ----------------------------------------------------------------

        public static string EmptyStateTextOf(UIElement root) => PartsOf(root).EmptyState.Text;
        public static Button SendButtonOf(UIElement root) => PartsOf(root).Send;
        public static TextBox ComposerOf(UIElement root) => PartsOf(root).Composer;
        public static TextBlock PlaceholderOf(UIElement root) => PartsOf(root).Placeholder;
        public static string FooterTextOf(UIElement root) => PartsOf(root).Footer.Text;
        public static Button CopyGuideButtonOf(UIElement root) => PartsOf(root).CopyGuide;
        public static ComboBox ToolPickerOf(UIElement root) => PartsOf(root).Picker;
        public static string[] ToolTitlesOf(UIElement root) => PartsOf(root).PickerTools.Select(x => x.Title).ToArray();
        public static string ToolLabelOf(UIElement root) => PartsOf(root).ToolLabel.Text;
        public static void SendForTest(UIElement root) => SendClicked(PartsOf(root));
        public static string LastAssistantTextOf(UIElement root) => PartsOf(root).Streaming?.Live?.Text;
        public static FrameworkElement LastUserBubbleOf(UIElement root) => PartsOf(root).LastUser;
        public static FrameworkElement LastAssistantBubbleOf(UIElement root) => PartsOf(root).LastAssistant;
    }
}
