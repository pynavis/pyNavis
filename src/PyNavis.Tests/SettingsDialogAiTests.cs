using System;
using System.Threading;
using PyNavis.Runtime.Ai;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The AI assistant section of the Settings window: provider, endpoint,
    /// model, key and answer length, read back exactly as entered.</summary>
    public class SettingsDialogAiTests
    {
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

        [Fact]
        public void TheAiSection_RoundTripsThroughTheWindow()
        {
            OnSta(() =>
            {
                var given = new PyNavisConfig.UserSettings();
                given.Ai.Provider = AiProvider.OpenAiCompatible;
                given.Ai.BaseUrl = "http://localhost:11434/v1";
                given.Ai.Model = "llama3";
                given.Ai.MaxTokens = 2048;

                var window = SettingsDialog.Build(given, hasKey: false);
                var back = SettingsDialog.CollectForTest(window);

                Assert.Equal(AiProvider.OpenAiCompatible, back.Ai.Provider);
                Assert.Equal("http://localhost:11434/v1", back.Ai.BaseUrl);
                Assert.Equal("llama3", back.Ai.Model);
                Assert.Equal(2048, back.Ai.MaxTokens);
            });
        }

        [Fact]
        public void Defaults_ReadBackAsDefaults_WithNoBaseUrlOrModelForced()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings(), hasKey: false);
                var back = SettingsDialog.CollectForTest(window);

                Assert.Equal(AiProvider.Anthropic, back.Ai.Provider);
                Assert.Null(back.Ai.BaseUrl);
                Assert.Null(back.Ai.Model);
                Assert.Equal(AiSettings.DefaultMaxTokens, back.Ai.MaxTokens);
            });
        }

        [Fact]
        public void TheKey_IsNullUntilTyped_ThenExactlyWhatWasTyped()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings(), hasKey: true);
                Assert.Null(SettingsDialog.EnteredKeyForTest(window));

                SettingsDialog.SetAiKeyForTest(window, "  sk-test-123 ");

                Assert.Equal("sk-test-123", SettingsDialog.EnteredKeyForTest(window));
            });
        }

        [Fact]
        public void ANonNumericMaxTokens_FallsBackToTheDefault()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings(), hasKey: false);
                SettingsDialog.SetAiMaxTokensForTest(window, "lots");

                Assert.Equal(AiSettings.DefaultMaxTokens, SettingsDialog.CollectForTest(window).Ai.MaxTokens);
            });
        }

        [Fact]
        public void TheSection_SaysAnyAssistantWillDo_SoNobodyThinksThisIsTheOnlyWay()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings(), hasKey: false);

                Assert.Contains("any assistant", SettingsDialog.AiSectionTextForTest(window), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("beta", SettingsDialog.AiSectionTextForTest(window), StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
