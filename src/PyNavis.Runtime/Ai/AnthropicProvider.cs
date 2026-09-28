using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// The Anthropic Messages API, streamed. The system prompt goes as one text block
    /// with cache_control, because it is the authoring pack and it never changes
    /// between turns: the second request on is mostly a cache read.
    /// </summary>
    internal sealed class AnthropicProvider : HttpChatProvider
    {
        public AnthropicProvider(string baseUrl, string apiKey, HttpMessageHandler handler = null)
            : base(baseUrl, apiKey, handler) { }

        protected override void Authenticate(HttpRequestMessage message)
        {
            message.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
            message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }

        public override async Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
        {
            var messages = new List<object>();
            foreach (var m in request.Messages)
                messages.Add(new Dictionary<string, object> { ["role"] = m.Role, ["content"] = m.Content ?? "" });

            var body = new Dictionary<string, object>
            {
                ["model"] = request.Model,
                ["max_tokens"] = request.MaxTokens,
                ["stream"] = true,
                ["messages"] = messages,
            };
            if (!string.IsNullOrEmpty(request.System))
            {
                body["system"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "text",
                        ["text"] = request.System,
                        ["cache_control"] = new Dictionary<string, object> { ["type"] = "ephemeral" },
                    },
                };
            }

            var result = new ChatResult();
            await PostStreamAsync("/v1/messages", body, evt =>
            {
                if (evt.Data == null) return true;
                var node = Object(evt.Data);
                var type = evt.Event ?? Text(node, "type");
                switch (type)
                {
                    case "content_block_delta":
                        var delta = Child(node, "delta");
                        if (Text(delta, "type") == "text_delta")
                        {
                            var text = Text(delta, "text");
                            if (!string.IsNullOrEmpty(text)) onText(text);
                        }
                        return true;
                    case "message_delta":
                        var stop = Text(Child(node, "delta"), "stop_reason");
                        if (stop != null) result.StopReason = stop;
                        return true;
                    case "error":
                        var error = Child(node, "error");
                        throw new ProviderException(
                            "The provider reported an error: " + (Text(error, "message") ?? evt.Data));
                    case "message_stop":
                        return false;
                    default:
                        return true;        // message_start, content_block_start/stop, ping
                }
            }, token).ConfigureAwait(false);
            return result;
        }
    }
}
