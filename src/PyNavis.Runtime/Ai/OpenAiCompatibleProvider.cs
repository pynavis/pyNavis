using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// POST {base}/chat/completions with stream:true, the shape OpenAI, Azure OpenAI,
    /// Ollama, LM Studio and most local servers all speak. max_tokens rather than the
    /// newer max_completion_tokens because the local servers only know the old name.
    /// No key means no Authorization header, which is what a local server expects.
    /// </summary>
    internal sealed class OpenAiCompatibleProvider : HttpChatProvider
    {
        public OpenAiCompatibleProvider(string baseUrl, string apiKey, HttpMessageHandler handler = null)
            : base(baseUrl, apiKey, handler) { }

        protected override void Authenticate(HttpRequestMessage message)
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey.Trim());
        }

        public override async Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token)
        {
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(request.System))
                messages.Add(new Dictionary<string, object> { ["role"] = "system", ["content"] = request.System });
            foreach (var m in request.Messages)
                messages.Add(new Dictionary<string, object> { ["role"] = m.Role, ["content"] = m.Content ?? "" });

            var body = new Dictionary<string, object>
            {
                ["model"] = request.Model,
                ["max_tokens"] = request.MaxTokens,
                ["stream"] = true,
                ["messages"] = messages,
            };

            var result = new ChatResult();
            await PostStreamAsync("/chat/completions", body, evt =>
            {
                if (evt.Data == null) return true;
                if (evt.Data.Trim() == "[DONE]") return false;
                var node = Object(evt.Data);
                if (node == null) return true;

                var error = Child(node, "error");
                if (error != null)
                    throw new ProviderException("The provider reported an error: " + (Text(error, "message") ?? evt.Data));

                if (!node.TryGetValue("choices", out var rawChoices) || !(rawChoices is IList choices) || choices.Count == 0)
                    return true;
                var choice = choices[0] as Dictionary<string, object>;
                var content = Text(Child(choice, "delta"), "content");
                if (!string.IsNullOrEmpty(content)) onText(content);
                var finish = Text(choice, "finish_reason");
                if (finish != null) result.StopReason = finish;
                return true;
            }, token).ConfigureAwait(false);
            return result;
        }
    }
}
