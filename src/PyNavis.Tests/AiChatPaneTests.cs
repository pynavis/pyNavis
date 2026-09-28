using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    }
}
