using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PyNavis.Runtime.Ai
{
    /// <summary>One turn of the conversation, as the wire sees it.</summary>
    public sealed class ChatMessage
    {
        public string Role;         // "user" or "assistant"
        public string Content;

        public ChatMessage() { }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    public sealed class ChatRequest
    {
        public string System;
        public string Model;
        public int MaxTokens = AiSettings.DefaultMaxTokens;
        public List<ChatMessage> Messages { get; } = new List<ChatMessage>();
    }

    public sealed class ChatResult
    {
        /// <summary>The provider's own word: end_turn / stop / max_tokens / length /
        /// refusal, or null when the stream ended without saying.</summary>
        public string StopReason;

        public bool HitLengthLimit =>
            StopReason == "max_tokens" || StopReason == "length";

        public bool Refused => StopReason == "refusal" || StopReason == "content_filter";
    }

    /// <summary>A streaming chat completion. Text arrives through the callback as it
    /// is generated; the task completes when the stream ends.</summary>
    public interface IChatProvider
    {
        Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token);
    }

    /// <summary>What went wrong on the wire, worded for the transcript.</summary>
    public sealed class ProviderException : Exception
    {
        public int? StatusCode { get; }

        public ProviderException(string message, int? statusCode = null, Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
        }

        public bool IsAuthFailure => StatusCode == 401 || StatusCode == 403;
    }

    /// <summary>Picks the provider for the settings.</summary>
    public static class ChatProviders
    {
        public static IChatProvider Create(AiSettings settings, string apiKey, HttpMessageHandler handler = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return settings.Provider == AiProvider.OpenAiCompatible
                ? (IChatProvider)new OpenAiCompatibleProvider(settings.EffectiveBaseUrl, apiKey, handler)
                : new AnthropicProvider(settings.EffectiveBaseUrl, apiKey, handler);
        }
    }

    /// <summary>The HTTP plumbing both providers share: one client, a streamed POST,
    /// error bodies turned into ProviderException, SSE events handed to a callback.</summary>
    internal abstract class HttpChatProvider : IChatProvider
    {
        protected readonly string BaseUrl;
        protected readonly string ApiKey;
        private readonly HttpClient _client;

        protected HttpChatProvider(string baseUrl, string apiKey, HttpMessageHandler handler)
        {
            BaseUrl = (baseUrl ?? "").TrimEnd('/');
            ApiKey = apiKey ?? "";
            _client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            _client.Timeout = TimeSpan.FromMinutes(10);        // streaming; cancel is the way out
        }

        public abstract Task<ChatResult> StreamAsync(ChatRequest request, Action<string> onText, CancellationToken token);

        protected abstract void Authenticate(HttpRequestMessage message);

        protected static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        protected static Dictionary<string, object> Object(string json)
        {
            try { return Json.Deserialize<Dictionary<string, object>>(json); }
            catch { return null; }
        }

        protected static Dictionary<string, object> Child(Dictionary<string, object> node, string key) =>
            node != null && node.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

        protected static string Text(Dictionary<string, object> node, string key) =>
            node != null && node.TryGetValue(key, out var value) ? value as string : null;

        /// <summary>POSTs the body and walks the SSE events until the stream closes.
        /// Returns false from <paramref name="onEvent"/> to stop early.</summary>
        protected async Task PostStreamAsync(string path, Dictionary<string, object> body,
            Func<SseEvent, bool> onEvent, CancellationToken token)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
            {
                Content = new StringContent(Json.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
            };
            message.Headers.Accept.ParseAdd("text/event-stream");
            Authenticate(message);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException ex)
            {
                throw new ProviderException("Could not reach " + BaseUrl + ": " + Root(ex).Message, null, ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = response.Content == null ? "" :
                        await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    throw new ProviderException(DescribeError(status, errorBody), status);
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream))
                {
                    await SseReader.ReadAsync(reader, evt =>
                    {
                        token.ThrowIfCancellationRequested();
                        return onEvent(evt);
                    }, token).ConfigureAwait(false);
                }
            }
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        /// <summary>The server's own message when the body has one (both APIs nest it
        /// under "error"), else the status line.</summary>
        protected virtual string DescribeError(int status, string body)
        {
            var node = Object(body);
            var error = Child(node, "error");
            var detail = Text(error, "message") ?? Text(node, "message");
            var prefix = status == 401 || status == 403 ? "The API key was rejected"
                : status == 404 ? "The endpoint was not found"
                : status == 429 ? "The provider is rate limiting"
                : status >= 500 ? "The provider had a server error"
                : "The request was refused";
            return string.IsNullOrWhiteSpace(detail)
                ? $"{prefix} (HTTP {status})."
                : $"{prefix} (HTTP {status}): {detail.Trim()}";
        }
    }
}
