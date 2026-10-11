using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PyNavis.Runtime.Ai;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Ask AI pane's construction and its two resting states: no key yet, and
    /// ready to chat. Behaviour beyond that is AiSession's, tested on its own.
    /// </summary>
    public class AiChatPaneTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));

        public AiChatPaneTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { body(); } catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private sealed class Silent : IChatProvider
        {
            public Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
            {
                onText("Title: T\n```python script.py\nfrom pynavis import toast\ntoast.info('x')\n```\n");
                return Task.FromResult(new ChatResult { StopReason = "end_turn" });
            }
        }

        private sealed class NoIcons : IIconWriter
        {
            public void Write(string bundleDir, IconSpec spec) { }
        }

        private AiSession Session() => new AiSession(() => new Silent(), () => new AiSettings(), "P",
            new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons()), () => null);

        [Fact]
        public void WithoutAKey_ShowsTheSetupMessage_AndDisablesSend()
        {
            OnSta(() =>
            {
                var pane = AiChatPane.Build(Session(), hasKey: () => false, openSettings: () => { });

                Assert.Contains("key", AiChatPane.EmptyStateTextOf(pane), StringComparison.OrdinalIgnoreCase);
                Assert.False(AiChatPane.SendButtonOf(pane).IsEnabled);
            });
        }

        [Fact]
        public void WithAKey_TheComposerIsLive_AndTheEmptyStateSaysBeta()
        {
            OnSta(() =>
            {
                var pane = AiChatPane.Build(Session(), hasKey: () => true, openSettings: () => { });

                Assert.Contains("beta", AiChatPane.EmptyStateTextOf(pane), StringComparison.OrdinalIgnoreCase);
                AiChatPane.ComposerOf(pane).Text = "hello";
                Assert.True(AiChatPane.SendButtonOf(pane).IsEnabled);
            });
        }

        [Fact]
        public void TheFooter_AlwaysOffersTheGuide_ForAnyOtherAssistant()
        {
            OnSta(() =>
            {
                var pane = AiChatPane.Build(Session(), hasKey: () => false, openSettings: () => { });

                var footer = AiChatPane.FooterTextOf(pane);
                Assert.Contains("any assistant", footer, StringComparison.OrdinalIgnoreCase);
                Assert.NotNull(AiChatPane.CopyGuideButtonOf(pane));
            });
        }

        private sealed class Blocking : IChatProvider
        {
            public readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>();

            public async Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
            {
                await Release.Task;
                onText("done");
                return new ChatResult { StopReason = "end_turn" };
            }
        }

        [Fact]
        public void WhileWaitingForTheFirstToken_TheReplyBubbleSaysSo_InsteadOfSittingEmpty()
        {
            OnSta(() =>
            {
                var provider = new Blocking();
                var session = new AiSession(() => provider, () => new AiSettings(), "P",
                    new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons()), () => null);
                var pane = AiChatPane.Build(session, hasKey: () => true, openSettings: () => { });
                AiChatPane.ComposerOf(pane).Text = "make a tool";

                AiChatPane.SendForTest(pane);

                Assert.True(session.IsBusy);
                Assert.Contains("Thinking", AiChatPane.LastAssistantTextOf(pane));
                provider.Release.SetResult(true);
            });
        }

        [Fact]
        public void ExistingGeneratedTools_AreOfferedForEditing_AndPickingOneOpensIt()
        {
            OnSta(() =>
            {
                var writer = new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons());
                foreach (var title in new[] { "Beta", "Alpha" })
                {
                    var proposal = new Proposal { Title = title, Icon = new IconSpec("A", "teal") };
                    proposal.Files["script.py"] = "pass\n";
                    proposal.Files["bundle.yaml"] = "title: " + title + "\n";
                    writer.Create(proposal);
                }
                var session = new AiSession(() => new Silent(), () => new AiSettings(), "P", writer, () => null);
                var pane = AiChatPane.Build(session, hasKey: () => true, openSettings: () => { });

                var picker = AiChatPane.ToolPickerOf(pane);
                Assert.Equal(new[] { "Alpha", "Beta" }, AiChatPane.ToolTitlesOf(pane));

                picker.SelectedIndex = 1;      // 0 is the "Edit an existing tool" prompt

                Assert.EndsWith("Alpha.pushbutton", session.CurrentBundleDir);
                Assert.Equal("Alpha", session.CurrentTitle);
                Assert.Contains("Alpha", AiChatPane.ToolLabelOf(pane));
            });
        }

        [Fact]
        public void WithNoGeneratedToolsYet_ThePickerIsHidden()
        {
            OnSta(() =>
            {
                var pane = AiChatPane.Build(Session(), hasKey: () => true, openSettings: () => { });

                Assert.Equal(System.Windows.Visibility.Collapsed, AiChatPane.ToolPickerOf(pane).Visibility);
            });
        }

        [Fact]
        public void UserMessages_SitRight_AndAssistantMessages_SitLeft()
        {
            OnSta(() =>
            {
                var provider = new Blocking();
                var session = new AiSession(() => provider, () => new AiSettings(), "P",
                    new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons()), () => null);
                var pane = AiChatPane.Build(session, hasKey: () => true, openSettings: () => { });
                AiChatPane.ComposerOf(pane).Text = "hi";

                AiChatPane.SendForTest(pane);

                Assert.Equal(System.Windows.HorizontalAlignment.Right, AiChatPane.LastUserBubbleOf(pane).HorizontalAlignment);
                Assert.Equal(System.Windows.HorizontalAlignment.Left, AiChatPane.LastAssistantBubbleOf(pane).HorizontalAlignment);
                provider.Release.SetResult(true);
            });
        }

        [Fact]
        public void TheComposer_ShowsAPlaceholder_OnlyWhileEmpty()
        {
            OnSta(() =>
            {
                var pane = AiChatPane.Build(Session(), hasKey: () => true, openSettings: () => { });
                Assert.Equal(System.Windows.Visibility.Visible, AiChatPane.PlaceholderOf(pane).Visibility);

                AiChatPane.ComposerOf(pane).Text = "x";

                Assert.Equal(System.Windows.Visibility.Collapsed, AiChatPane.PlaceholderOf(pane).Visibility);
            });
        }

        [Fact]
        public void AnEmptyComposer_CannotSend()
        {
            OnSta(() =>
            {
                var pane = AiChatPane.Build(Session(), hasKey: () => true, openSettings: () => { });

                AiChatPane.ComposerOf(pane).Text = "   ";

                Assert.False(AiChatPane.SendButtonOf(pane).IsEnabled);
            });
        }

        // ---- rethemes on Reload --------------------------------------------------------

        /// <summary>Runs body with the pane's theme sources swapped and no live pane
        /// kept from another test, restoring both afterwards.</summary>
        private static void WithTheme(Func<DesignSystem.Tokens> tokens, Func<Color> accent, Action body)
        {
            var oldTokens = AiChatPane.ThemeTokens;
            var oldAccent = AiChatPane.ThemeAccent;
            AiChatPane.ThemeTokens = tokens;
            AiChatPane.ThemeAccent = accent;
            AiChatPane.ForgetLiveForTest();
            try { body(); }
            finally
            {
                AiChatPane.ThemeTokens = oldTokens;
                AiChatPane.ThemeAccent = oldAccent;
                AiChatPane.ForgetLiveForTest();
            }
        }

        private static string[] TranscriptTexts(UIElement pane)
        {
            var texts = new System.Collections.Generic.List<string>();
            void Walk(DependencyObject node)
            {
                if (node is TextBlock block && !string.IsNullOrEmpty(block.Text)) texts.Add(block.Text);
                if (node is TextBox box) texts.Add(box.Text);
                foreach (var child in LogicalTreeHelper.GetChildren(node))
                    if (child is DependencyObject element) Walk(element);
            }
            Walk(AiChatPane.TranscriptOf(pane));
            return texts.ToArray();
        }

        private static Color BackgroundOf(object element) =>
            ((SolidColorBrush)((element as Panel)?.Background ?? ((Border)element).Background)).Color;

        [Fact]
        public void AfterAThemeChange_LiveContent_RebuildsThePane_AroundTheSameConversation()
        {
            // The pane is kept across Reloads so the chat survives the Reload that
            // Create triggers, but that kept the palette of the first show too: after a
            // theme change every other window came up in the new theme and Ask AI stayed
            // in the old one until Navisworks restarted.
            OnSta(() =>
            {
                var light = DesignSystem.Tokens.For(false);
                var dark = DesignSystem.Tokens.For(true);
                var current = light;
                WithTheme(() => current, () => Colors.Teal, () =>
                {
                    var session = Session();
                    UIElement Build() => AiChatPane.Build(session, () => true, () => { });
                    var first = AiChatPane.LiveContent(Build);
                    AiChatPane.ComposerOf(first).Text = "make a tool";
                    AiChatPane.SendForTest(first);      // the reply carries a complete proposal
                    AiChatPane.ComposerOf(first).Text = "a draft";
                    var said = TranscriptTexts(first);
                    Assert.Contains("make a tool", said);
                    Assert.Equal(light.Paper, BackgroundOf(first));

                    Assert.Same(first, AiChatPane.LiveContent(Build));   // same theme: same pane

                    current = dark;
                    var second = AiChatPane.LiveContent(Build);

                    Assert.NotSame(first, second);
                    Assert.Same(session, AiChatPane.SessionOf(second));
                    Assert.Equal(dark.Paper, BackgroundOf(second));
                    Assert.Equal(dark.Surface, BackgroundOf(AiChatPane.LastUserBubbleOf(second)));
                    Assert.Equal(said, TranscriptTexts(second));
                    Assert.Equal(Visibility.Visible, AiChatPane.ProposalCardOf(second).Visibility);
                    Assert.Equal(Visibility.Collapsed, AiChatPane.EmptyPanelOf(second).Visibility);
                    Assert.Equal("a draft", AiChatPane.ComposerOf(second).Text);
                    Assert.Same(second, AiChatPane.LiveContent(Build));
                });
            });
        }

        [Fact]
        public void AfterAnAccentChange_LiveContent_RebuildsThePane()
        {
            OnSta(() =>
            {
                var accent = Colors.Teal;
                WithTheme(() => DesignSystem.Tokens.For(false), () => accent, () =>
                {
                    var session = Session();
                    UIElement Build() => AiChatPane.Build(session, () => true, () => { });
                    var first = AiChatPane.LiveContent(Build);

                    accent = Colors.OrangeRed;
                    var second = AiChatPane.LiveContent(Build);

                    Assert.NotSame(first, second);
                    Assert.Same(session, AiChatPane.SessionOf(second));
                });
            });
        }

        [Fact]
        public void WhileAReplyStreams_ThePaneWaitsForTheTurnToEnd_BeforeRetheming()
        {
            // The turn in flight writes into the pane it started in; swapping the pane
            // under it would lose the reply.
            OnSta(() =>
            {
                var current = DesignSystem.Tokens.For(false);
                WithTheme(() => current, () => Colors.Teal, () =>
                {
                    var provider = new Blocking();
                    var session = new AiSession(() => provider, () => new AiSettings(), "P",
                        new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons()), () => null);
                    UIElement Build() => AiChatPane.Build(session, () => true, () => { });
                    var first = AiChatPane.LiveContent(Build);
                    AiChatPane.ComposerOf(first).Text = "make a tool";
                    AiChatPane.SendForTest(first);

                    current = DesignSystem.Tokens.For(true);
                    Assert.Same(first, AiChatPane.LiveContent(Build));

                    provider.Release.SetResult(true);
                    var second = AiChatPane.LiveContent(Build);

                    Assert.NotSame(first, second);
                    Assert.Contains("done", TranscriptTexts(second));
                });
            });
        }
    }
}
