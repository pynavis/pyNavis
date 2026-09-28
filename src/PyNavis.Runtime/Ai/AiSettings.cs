using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Ai
{
    /// <summary>Which chat API the assistant talks to.</summary>
    public enum AiProvider
    {
        /// <summary>The Anthropic Messages API.</summary>
        Anthropic,
        /// <summary>Any endpoint speaking the OpenAI chat-completions shape: OpenAI,
        /// Azure OpenAI, Ollama, most local servers. The base URL picks which.</summary>
        OpenAiCompatible,
    }

    /// <summary>
    /// The "ai" section of config.json. Everything a person picks about the assistant
    /// except the key, which SecretStore keeps encrypted in its own file.
    /// </summary>
    public sealed class AiSettings
    {
        public const int DefaultMaxTokens = 8192;
        public const string AnthropicBaseUrl = "https://api.anthropic.com";
        public const string AnthropicDefaultModel = "claude-opus-5";
        public const string OpenAiBaseUrl = "https://api.openai.com/v1";
        public const string OpenAiDefaultModel = "gpt-4o";

        /// <summary>Models offered in the Settings combo per provider. Any other name
        /// can still be typed.</summary>
        public static readonly string[] AnthropicModels =
            { "claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5" };
        public static readonly string[] OpenAiModels =
            { "gpt-4o", "gpt-4o-mini", "gpt-4.1", "o3" };

        public AiProvider Provider = AiProvider.Anthropic;
        /// <summary>Null or empty: the provider's public endpoint.</summary>
        public string BaseUrl;
        /// <summary>Null or empty: the provider's default model.</summary>
        public string Model;
        public int MaxTokens = DefaultMaxTokens;

        public string EffectiveBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.Trim().TrimEnd('/')
            : Provider == AiProvider.Anthropic ? AnthropicBaseUrl : OpenAiBaseUrl;

        public string EffectiveModel => !string.IsNullOrWhiteSpace(Model)
            ? Model.Trim()
            : Provider == AiProvider.Anthropic ? AnthropicDefaultModel : OpenAiDefaultModel;

        public AiSettings Clone() => new AiSettings
        {
            Provider = Provider, BaseUrl = BaseUrl, Model = Model, MaxTokens = MaxTokens,
        };

        // ---- config.json shape --------------------------------------------------

        public static string ProviderKey(AiProvider provider) =>
            provider == AiProvider.OpenAiCompatible ? "openai" : "anthropic";

        public static AiProvider ParseProvider(string text) =>
            string.Equals(text, "openai", StringComparison.OrdinalIgnoreCase)
                ? AiProvider.OpenAiCompatible : AiProvider.Anthropic;

        /// <summary>Reads the parsed "ai" dictionary; anything missing or odd keeps
        /// its default, never throws.</summary>
        public static AiSettings FromSection(Dictionary<string, object> section)
        {
            var ai = new AiSettings();
            if (section == null) return ai;
            if (section.TryGetValue("provider", out var p) && p is string provider)
                ai.Provider = ParseProvider(provider);
            if (section.TryGetValue("baseUrl", out var u) && u is string url && !string.IsNullOrWhiteSpace(url))
                ai.BaseUrl = url.Trim();
            if (section.TryGetValue("model", out var m) && m is string model && !string.IsNullOrWhiteSpace(model))
                ai.Model = model.Trim();
            if (section.TryGetValue("maxTokens", out var t))
            {
                int parsed;
                if (int.TryParse(Convert.ToString(t), out parsed) && parsed > 0) ai.MaxTokens = parsed;
            }
            return ai;
        }

        /// <summary>Writes the non-default keys into <paramref name="section"/> and
        /// removes the default ones, leaving keys it does not know alone.</summary>
        public void WriteSection(Dictionary<string, object> section)
        {
            if (Provider == AiProvider.Anthropic) section.Remove("provider");
            else section["provider"] = ProviderKey(Provider);
            SetOrRemove(section, "baseUrl", BaseUrl);
            SetOrRemove(section, "model", Model);
            if (MaxTokens <= 0 || MaxTokens == DefaultMaxTokens) section.Remove("maxTokens");
            else section["maxTokens"] = MaxTokens;
        }

        private static void SetOrRemove(Dictionary<string, object> section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) section.Remove(key);
            else section[key] = value.Trim();
        }
    }
}
