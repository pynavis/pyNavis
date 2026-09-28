using System;
using System.IO;
using PyNavis.Runtime.Ai;
using PyNavis.Runtime.Config;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The "ai" section of config.json: which provider, where, which model, how long
    /// an answer may be. The key itself never lives here (see SecretStoreTests).
    /// </summary>
    public class AiSettingsTests : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), "pynavis-ai-" + Guid.NewGuid() + ".json");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Fact]
        public void MissingSection_YieldsAnthropicDefaults()
        {
            File.WriteAllText(_path, "{}");

            var ai = PyNavisConfig.Load(_path).Ai;

            Assert.Equal(AiProvider.Anthropic, ai.Provider);
            Assert.Null(ai.BaseUrl);
            Assert.Null(ai.Model);
            Assert.Equal(AiSettings.DefaultMaxTokens, ai.MaxTokens);
            Assert.Equal("https://api.anthropic.com", ai.EffectiveBaseUrl);
            Assert.Equal("claude-opus-5", ai.EffectiveModel);
        }

        [Fact]
        public void Load_ReadsEveryKey()
        {
            File.WriteAllText(_path, "{\"ai\": {\"provider\": \"openai\", \"baseUrl\": \"http://localhost:11434/v1\","
                + " \"model\": \"llama3\", \"maxTokens\": 2048}}");

            var ai = PyNavisConfig.Load(_path).Ai;

            Assert.Equal(AiProvider.OpenAiCompatible, ai.Provider);
            Assert.Equal("http://localhost:11434/v1", ai.BaseUrl);
            Assert.Equal("llama3", ai.Model);
            Assert.Equal(2048, ai.MaxTokens);
            Assert.Equal("http://localhost:11434/v1", ai.EffectiveBaseUrl);
            Assert.Equal("llama3", ai.EffectiveModel);
        }

        [Fact]
        public void OpenAiCompatible_WithoutBaseUrlOrModel_UsesOpenAiDefaults()
        {
            File.WriteAllText(_path, "{\"ai\": {\"provider\": \"openai\"}}");

            var ai = PyNavisConfig.Load(_path).Ai;

            Assert.Equal("https://api.openai.com/v1", ai.EffectiveBaseUrl);
            Assert.Equal("gpt-4o", ai.EffectiveModel);
        }

        [Fact]
        public void UnknownProviderOrBadMaxTokens_FallBackToDefaults()
        {
            File.WriteAllText(_path, "{\"ai\": {\"provider\": \"gemini\", \"maxTokens\": -5}}");

            var ai = PyNavisConfig.Load(_path).Ai;

            Assert.Equal(AiProvider.Anthropic, ai.Provider);
            Assert.Equal(AiSettings.DefaultMaxTokens, ai.MaxTokens);
        }

        [Fact]
        public void SaveUserSettings_RoundTripsTheAiSection()
        {
            File.WriteAllText(_path, "{}");
            var settings = new PyNavisConfig.UserSettings();
            settings.Ai.Provider = AiProvider.OpenAiCompatible;
            settings.Ai.BaseUrl = "https://example.test/v1";
            settings.Ai.Model = "my-model";
            settings.Ai.MaxTokens = 4096;

            PyNavisConfig.SaveUserSettings(_path, settings);

            var ai = PyNavisConfig.Load(_path).Ai;
            Assert.Equal(AiProvider.OpenAiCompatible, ai.Provider);
            Assert.Equal("https://example.test/v1", ai.BaseUrl);
            Assert.Equal("my-model", ai.Model);
            Assert.Equal(4096, ai.MaxTokens);
        }

        [Fact]
        public void SavingTheDefaults_WritesNoAiSection_SoAnUntouchedFileStaysClean()
        {
            File.WriteAllText(_path, "{}");

            PyNavisConfig.SaveUserSettings(_path, new PyNavisConfig.UserSettings());

            Assert.DoesNotContain("\"ai\"", File.ReadAllText(_path));
        }

        [Fact]
        public void Saving_KeepsAnAiKeyItHasNeverHeardOf()
        {
            File.WriteAllText(_path, "{\"ai\": {\"future\": true}}");

            PyNavisConfig.SaveUserSettings(_path, new PyNavisConfig.UserSettings());

            Assert.Contains("future", File.ReadAllText(_path));
        }
    }
}
