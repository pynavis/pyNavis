using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The conversation loop behind the pane, driven with a scripted provider: ask,
    /// get a proposal, create it, revise it, send the last error, start over.
    /// </summary>
    public class AiSessionTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));

        public AiSessionTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private sealed class ScriptedProvider : IChatProvider
        {
            public readonly Queue<string> Replies = new Queue<string>();
            public readonly List<ChatRequest> Requests = new List<ChatRequest>();
            public Exception Throw;

            public async Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
            {
                Requests.Add(request);
                if (Throw != null) throw Throw;
                var reply = Replies.Dequeue();
                foreach (var chunk in Chunks(reply, 7)) { onText(chunk); await Task.Yield(); }
                return new ChatResult { StopReason = "end_turn" };
            }

            private static IEnumerable<string> Chunks(string s, int size)
            {
                for (var i = 0; i < s.Length; i += size) yield return s.Substring(i, Math.Min(size, s.Length - i));
            }
        }

        private sealed class NoIcons : IIconWriter
        {
            public void Write(string bundleDir, IconSpec spec)
            {
                foreach (var name in new[] { "icon.png", "icon.dark.png", "icon.small.png", "icon.small.dark.png" })
                    File.WriteAllBytes(Path.Combine(bundleDir, name), new byte[] { 1 });
            }
        }

        // What the reader hands over: names only, never a value.
        private const string SchemaOnly = "2 items selected. Property names only, no values.\nItem\n  Name";

        private const string FirstReply =
            "A tool that says hello.\n\nTitle: Say Hello\nIcon: SH green\n\n" +
            "```yaml bundle.yaml\ntitle: Say Hello\ntooltip: Toasts a greeting.\n```\n\n" +
            "```python script.py\nfrom pynavis import toast\ntoast.success('Hello')\n```\n";

        private const string RevisedReply =
            "Now it says goodbye.\n\nTitle: Say Hello\n\n" +
            "```yaml bundle.yaml\ntitle: Say Hello\ntooltip: Toasts a farewell.\n```\n\n" +
            "```python script.py\nfrom pynavis import toast\ntoast.success('Goodbye')\n```\n";

        private (AiSession session, ScriptedProvider provider, GeneratedExtensionWriter writer) Make(string selection = null)
        {
            var provider = new ScriptedProvider();
            var writer = new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons());
            var session = new AiSession(
                () => provider,
                () => new AiSettings(),
                "PACK",
                writer,
                () => selection);
            return (session, provider, writer);
        }

        [Fact]
        public async Task Send_StreamsText_RecordsBothTurns_AndParsesTheProposal()
        {
            var (session, provider, _) = Make();
            provider.Replies.Enqueue(FirstReply);
            var streamed = "";
            session.TextStreamed += s => streamed += s;

            var outcome = await session.SendAsync("make a hello tool", false, CancellationToken.None);

            Assert.Null(outcome.Error);
            Assert.Equal(FirstReply, streamed);
            Assert.Equal(2, session.Conversation.Turns.Count);
            Assert.Equal("user", session.Conversation.Turns[0].Role);
            Assert.Equal("assistant", session.Conversation.Turns[1].Role);
            Assert.NotNull(outcome.Proposal);
            Assert.True(outcome.Proposal.IsComplete);
            Assert.Equal("Say Hello", outcome.Proposal.Title);
            Assert.Same(outcome.Proposal, session.LastProposal);
            Assert.Contains("PACK", provider.Requests[0].System);
        }

        [Fact]
        public async Task AttachSelection_PutsTheSummaryOnTheUserTurn_OnlyWhenAsked()
        {
            var (session, provider, _) = Make(SchemaOnly);
            provider.Replies.Enqueue(FirstReply);
            provider.Replies.Enqueue(FirstReply);

            await session.SendAsync("first", true, CancellationToken.None);
            await session.SendAsync("second", false, CancellationToken.None);

            Assert.Equal(SchemaOnly, session.Conversation.Turns[0].Selection);
            Assert.Null(session.Conversation.Turns[2].Selection);
            Assert.Contains("Item\n  Name", provider.Requests[0].Messages[0].Content);
        }

        [Fact]
        public async Task Apply_CreatesTheBundle_ThenMakesItTheCurrentTool()
        {
            var (session, provider, writer) = Make();
            provider.Replies.Enqueue(FirstReply);
            await session.SendAsync("make it", false, CancellationToken.None);
            Assert.Null(session.CurrentBundleDir);
            Assert.False(session.CanApply == false);

            var bundle = session.Apply();

            Assert.True(Directory.Exists(bundle));
            Assert.Equal(bundle, session.CurrentBundleDir);
            Assert.True(writer.Owns(bundle));
            Assert.Equal("Say Hello", session.CurrentTitle);
            Assert.Contains("Hello", session.Conversation.CurrentFiles["script.py"]);
            Assert.False(session.CanApply);       // nothing new to write until the next reply
        }

        [Fact]
        public async Task ASecondReply_AfterCreate_IsAnUpdate_OfTheSameFolder()
        {
            var (session, provider, _) = Make();
            provider.Replies.Enqueue(FirstReply);
            provider.Replies.Enqueue(RevisedReply);
            await session.SendAsync("make it", false, CancellationToken.None);
            var bundle = session.Apply();

            await session.SendAsync("say goodbye instead", false, CancellationToken.None);
            // The revision request carries the files as they are on disk.
            Assert.Contains("toast.success('Hello')", provider.Requests[1].Messages.Last().Content);
            Assert.True(session.CanApply);
            Assert.True(session.IsRevising);

            var again = session.Apply();

            Assert.Equal(bundle, again);
            Assert.Contains("Goodbye", File.ReadAllText(Path.Combine(bundle, "script.py")));
            Assert.Contains("Goodbye", session.Conversation.CurrentFiles["script.py"]);
        }

        [Fact]
        public async Task LastError_ComesFromTheFailureLog_ForTheCurrentTool_AndSendingItClearsIt()
        {
            var (session, provider, _) = Make();
            provider.Replies.Enqueue(FirstReply);
            provider.Replies.Enqueue(RevisedReply);
            await session.SendAsync("make it", false, CancellationToken.None);
            var bundle = session.Apply();
            Assert.Null(session.LastError);

            FailureLog.Record(bundle, "Traceback (most recent call last):\n  NameError: boom");
            Assert.Contains("NameError", session.LastError);

            await session.SendLastErrorAsync(CancellationToken.None);

            Assert.Contains("NameError: boom", provider.Requests[1].Messages.Last().Content);
            Assert.Contains("failed", provider.Requests[1].Messages.Last().Content);
            Assert.Null(session.LastError);
        }

        [Fact]
        public async Task AProviderFailure_LeavesTheUserTurn_AddsNoAssistantTurn_AndReportsTheMessage()
        {
            var (session, provider, _) = Make();
            provider.Throw = new ProviderException("The API key was rejected (HTTP 401).", 401);

            var outcome = await session.SendAsync("hello", false, CancellationToken.None);

            Assert.Equal("The API key was rejected (HTTP 401).", outcome.Error);
            Assert.True(outcome.IsAuthFailure);
            Assert.Null(outcome.Proposal);
            Assert.Single(session.Conversation.Turns);
            Assert.False(session.CanApply);
        }

        [Fact]
        public async Task Cancelling_MidStream_KeepsWhatArrived_AsAnAssistantTurn_MarkedCut()
        {
            var (session, _, _) = Make();
            var slow = new CancelAfterProvider();
            var session2 = new AiSession(() => slow, () => new AiSettings(), "P",
                new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons()), () => null);
            var cts = new CancellationTokenSource();
            slow.Cancel = cts;

            var outcome = await session2.SendAsync("go", false, cts.Token);

            Assert.True(outcome.Cancelled);
            Assert.Equal(2, session2.Conversation.Turns.Count);
            Assert.Contains("partial", session2.Conversation.Turns[1].Text);
        }

        private sealed class CancelAfterProvider : IChatProvider
        {
            public CancellationTokenSource Cancel;

            public async Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
            {
                onText("partial");
                Cancel.Cancel();
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return new ChatResult();
            }
        }

        [Fact]
        public async Task AReplyWithNoFiles_IsAQuestion_NothingToApply()
        {
            var (session, provider, _) = Make();
            provider.Replies.Enqueue("Which level property do you mean, Item or Element?");

            var outcome = await session.SendAsync("isolate level", false, CancellationToken.None);

            Assert.Null(outcome.Error);
            Assert.False(outcome.Proposal.IsComplete);
            Assert.False(session.CanApply);
        }

        [Fact]
        public async Task Reset_StartsANewTool_ForgettingTheOldOne()
        {
            var (session, provider, _) = Make();
            provider.Replies.Enqueue(FirstReply);
            await session.SendAsync("make it", false, CancellationToken.None);
            session.Apply();

            session.Reset();

            Assert.Empty(session.Conversation.Turns);
            Assert.Null(session.CurrentBundleDir);
            Assert.Null(session.LastProposal);
            Assert.False(session.CanApply);
        }

        [Fact]
        public void SendingWhileBusy_IsRefused()
        {
            var (session, provider, _) = Make();
            var gate = new BlockingProvider();
            var busy = new AiSession(() => gate, () => new AiSettings(), "P",
                new GeneratedExtensionWriter(Path.Combine(_dir, "AI.extension"), new NoIcons()), () => null);

            var first = busy.SendAsync("one", false, CancellationToken.None);
            Assert.True(busy.IsBusy);
            Assert.ThrowsAsync<InvalidOperationException>(() => busy.SendAsync("two", false, CancellationToken.None)).Wait();
            gate.Release.SetResult(true);
            first.Wait();
            Assert.False(busy.IsBusy);
        }

        private sealed class BlockingProvider : IChatProvider
        {
            public readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>();

            public async Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
            {
                await Release.Task;
                onText("done");
                return new ChatResult();
            }
        }
    }
}
