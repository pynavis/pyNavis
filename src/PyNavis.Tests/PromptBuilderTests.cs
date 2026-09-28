using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// What the model is told: the authoring pack and the output rules once, in the
    /// system prompt, and the conversation with the current tool's files and the
    /// attached selection riding on the newest user turn.
    /// </summary>
    public class PromptBuilderTests
    {
        private static Conversation Chat(params (string role, string text)[] turns)
        {
            var c = new Conversation();
            foreach (var (role, text) in turns) c.Turns.Add(new Turn(role, text));
            return c;
        }

        [Fact]
        public void System_IsThePack_PlusTheOutputRules()
        {
            var request = PromptBuilder.Build("THE PACK", Chat(("user", "hi")), new AiSettings());

            Assert.Contains("THE PACK", request.System);
            Assert.Contains("bundle.yaml", request.System);
            Assert.Contains("script.py", request.System);
            Assert.Contains("Icon:", request.System);
            Assert.Equal("claude-opus-5", request.Model);
            Assert.Equal(AiSettings.DefaultMaxTokens, request.MaxTokens);
        }

        [Fact]
        public void Turns_BecomeMessages_InOrder()
        {
            var request = PromptBuilder.Build("P", Chat(("user", "a"), ("assistant", "b"), ("user", "c")), new AiSettings());

            Assert.Equal(new[] { "user", "assistant", "user" }, request.Messages.Select(m => m.Role).ToArray());
            Assert.Equal("a", request.Messages[0].Content);
            Assert.Equal("c", request.Messages[2].Content);
        }

        [Fact]
        public void AnAttachedSelection_RidesOnItsOwnTurn_AsAFencedBlock()
        {
            var chat = Chat(("user", "make it"));
            chat.Turns[0].Selection = "12 items selected\nCategories: Item, Element";

            var request = PromptBuilder.Build("P", chat, new AiSettings());

            var text = request.Messages[0].Content;
            Assert.StartsWith("make it", text);
            Assert.Contains("Current selection", text);
            Assert.Contains("Categories: Item, Element", text);
        }

        [Fact]
        public void TheCurrentToolsFiles_AreAppendedToTheNewestUserTurnOnly()
        {
            var chat = Chat(("user", "first"), ("assistant", "ok"), ("user", "revise it"));
            chat.CurrentFiles = new Dictionary<string, string>
            {
                ["bundle.yaml"] = "title: T\n",
                ["script.py"] = "print(2)\n",
            };

            var request = PromptBuilder.Build("P", chat, new AiSettings());

            Assert.Equal("first", request.Messages[0].Content);
            var last = request.Messages[2].Content;
            Assert.StartsWith("revise it", last);
            Assert.Contains("current files", last);
            Assert.Contains("```python script.py\nprint(2)\n```", last);
            Assert.Contains("```yaml bundle.yaml\ntitle: T\n```", last);
        }

        [Fact]
        public void ALongConversation_DropsTheOldestTurns_InPairs_KeepingTheNewestUserTurn()
        {
            var chat = new Conversation();
            for (var i = 0; i < 40; i++)
            {
                chat.Turns.Add(new Turn("user", "u" + i + new string('x', 5000)));
                chat.Turns.Add(new Turn("assistant", "a" + i + new string('y', 5000)));
            }
            chat.Turns.Add(new Turn("user", "final"));

            var request = PromptBuilder.Build("P", chat, new AiSettings());

            Assert.Equal("final", request.Messages.Last().Content);
            Assert.Equal("user", request.Messages.First().Role);
            Assert.True(request.Messages.Count < 81);
            Assert.True(request.Messages.Sum(m => m.Content.Length) <= PromptBuilder.MessageBudgetChars + 5000);
        }

        [Fact]
        public void ModelAndMaxTokens_ComeFromSettings()
        {
            var settings = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "m", MaxTokens = 77 };

            var request = PromptBuilder.Build("P", Chat(("user", "x")), settings);

            Assert.Equal("m", request.Model);
            Assert.Equal(77, request.MaxTokens);
        }
    }
}
