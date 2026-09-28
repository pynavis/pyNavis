using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The two wire formats, checked against canned responses: what each provider
    /// sends (URL, auth header, body) and how it turns the stream back into text.
    /// </summary>
    public class ChatProviderTests
    {
        private static ChatRequest Request() => new ChatRequest
        {
            System = "You write pyNavis tools.",
            Model = "test-model",
            MaxTokens = 123,
            Messages =
            {
                new ChatMessage("user", "Make a tool"),
                new ChatMessage("assistant", "Here it is"),
                new ChatMessage("user", "Change it"),
            },
        };

        private static Dictionary<string, object> Json(string text) =>
            new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);

        private static async Task<(string text, ChatResult result)> Stream(IChatProvider provider)
        {
            var sb = new StringBuilder();
            var result = await provider.StreamAsync(Request(), s => sb.Append(s), CancellationToken.None);
            return (sb.ToString(), result);
        }

        // ---- Anthropic -----------------------------------------------------------

        private const string AnthropicStream =
            "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\"}}\n\n" +
            "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n" +
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}\n\n" +
            "event: ping\ndata: {\"type\":\"ping\"}\n\n" +
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"lo\"}}\n\n" +
            "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n" +
            "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":2}}\n\n" +
            "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";

        [Fact]
        public async Task Anthropic_PostsToMessages_WithKeyHeader_AndTheDocumentedBody()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, AnthropicStream);
            var provider = new AnthropicProvider("https://api.example.test", "sk-test", handler);

            var (text, result) = await Stream(provider);

            Assert.Equal("https://api.example.test/v1/messages", handler.LastRequest.RequestUri.ToString());
            Assert.Equal("sk-test", string.Join("", handler.LastRequest.Headers.GetValues("x-api-key")));
            Assert.Equal("2023-06-01", string.Join("", handler.LastRequest.Headers.GetValues("anthropic-version")));

            var body = Json(handler.LastBody);
            Assert.Equal("test-model", body["model"]);
            Assert.Equal(123, Convert.ToInt32(body["max_tokens"]));
            Assert.Equal(true, body["stream"]);
            // The system prompt is one cached text block: it is the big authoring pack
            // and it is identical every turn.
            var system = Assert.IsType<ArrayList>(body["system"]);
            var block = Assert.IsType<Dictionary<string, object>>(system[0]);
            Assert.Equal("You write pyNavis tools.", block["text"]);
            Assert.Equal("ephemeral", ((Dictionary<string, object>)block["cache_control"])["type"]);
            var messages = Assert.IsType<ArrayList>(body["messages"]);
            Assert.Equal(3, messages.Count);
            Assert.Equal("assistant", ((Dictionary<string, object>)messages[1])["role"]);

            Assert.Equal("Hello", text);
            Assert.Equal("end_turn", result.StopReason);
        }

        [Fact]
        public async Task Anthropic_ARefusalStop_IsReportedAsTheStopReason()
        {
            var stream = AnthropicStream.Replace("\"stop_reason\":\"end_turn\"", "\"stop_reason\":\"refusal\"");
            var provider = new AnthropicProvider("https://api.example.test", "k", new FakeHttpHandler(HttpStatusCode.OK, stream));

            var (_, result) = await Stream(provider);

            Assert.Equal("refusal", result.StopReason);
        }

        [Fact]
        public async Task Anthropic_AnErrorEventMidStream_Throws_WithTheServersMessage()
        {
            var stream = "event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}\n\n";
            var provider = new AnthropicProvider("https://api.example.test", "k", new FakeHttpHandler(HttpStatusCode.OK, stream));

            var ex = await Assert.ThrowsAsync<ProviderException>(() => Stream(provider));

            Assert.Contains("Overloaded", ex.Message);
        }

        [Fact]
        public async Task Anthropic_A401_Throws_AnAuthFailure_WithTheServersMessage()
        {
            var body = "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}";
            var provider = new AnthropicProvider("https://api.example.test", "bad",
                new FakeHttpHandler(HttpStatusCode.Unauthorized, body, "application/json"));

            var ex = await Assert.ThrowsAsync<ProviderException>(() => Stream(provider));

            Assert.Equal(401, ex.StatusCode);
            Assert.True(ex.IsAuthFailure);
            Assert.Contains("invalid x-api-key", ex.Message);
        }

        // ---- OpenAI-compatible -----------------------------------------------------

        private const string OpenAiStream =
            "data: {\"id\":\"c1\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hel\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"lo\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        [Fact]
        public async Task OpenAi_PostsToChatCompletions_WithBearer_SystemAsFirstMessage()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, OpenAiStream);
            var provider = new OpenAiCompatibleProvider("http://localhost:11434/v1", "sk-x", handler);

            var (text, result) = await Stream(provider);

            Assert.Equal("http://localhost:11434/v1/chat/completions", handler.LastRequest.RequestUri.ToString());
            Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
            Assert.Equal("sk-x", handler.LastRequest.Headers.Authorization.Parameter);

            var body = Json(handler.LastBody);
            Assert.Equal("test-model", body["model"]);
            Assert.Equal(true, body["stream"]);
            Assert.Equal(123, Convert.ToInt32(body["max_tokens"]));
            var messages = Assert.IsType<ArrayList>(body["messages"]);
            Assert.Equal(4, messages.Count);
            Assert.Equal("system", ((Dictionary<string, object>)messages[0])["role"]);
            Assert.Equal("You write pyNavis tools.", ((Dictionary<string, object>)messages[0])["content"]);

            Assert.Equal("Hello", text);
            Assert.Equal("stop", result.StopReason);
        }

        [Fact]
        public async Task OpenAi_WithNoKey_SendsNoAuthorizationHeader_ForLocalServers()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, OpenAiStream);
            var provider = new OpenAiCompatibleProvider("http://localhost:11434/v1", "", handler);

            await Stream(provider);

            Assert.Null(handler.LastRequest.Headers.Authorization);
        }

        [Fact]
        public async Task OpenAi_A429_Throws_WithStatusAndMessage()
        {
            var body = "{\"error\":{\"message\":\"Rate limit reached\",\"type\":\"rate_limit_error\"}}";
            var provider = new OpenAiCompatibleProvider("https://api.example.test/v1", "k",
                new FakeHttpHandler((HttpStatusCode)429, body, "application/json"));

            var ex = await Assert.ThrowsAsync<ProviderException>(() => Stream(provider));

            Assert.Equal(429, ex.StatusCode);
            Assert.False(ex.IsAuthFailure);
            Assert.Contains("Rate limit reached", ex.Message);
        }

        // ---- factory -----------------------------------------------------------------

        [Fact]
        public void Factory_PicksTheProviderFromSettings()
        {
            var anthropic = new AiSettings { Provider = AiProvider.Anthropic };
            var openai = new AiSettings { Provider = AiProvider.OpenAiCompatible, BaseUrl = "http://x/v1/" };

            Assert.IsType<AnthropicProvider>(ChatProviders.Create(anthropic, "k"));
            Assert.IsType<OpenAiCompatibleProvider>(ChatProviders.Create(openai, "k"));
        }
    }
}
