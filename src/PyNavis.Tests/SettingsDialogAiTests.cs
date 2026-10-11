using System;
using System.IO;
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
        public void Remove_MarksTheSavedKeyForRemoval_AndClearsTheBox()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings(), hasKey: true,
                    keySavedFor: "https://api.anthropic.com");
                SettingsDialog.SetAiKeyForTest(window, "sk-typed");

                SettingsDialog.PressRemoveKeyForTest(window);

                Assert.True(SettingsDialog.KeyRemoveRequestedForTest(window));
                Assert.Null(SettingsDialog.EnteredKeyForTest(window));
            });
        }

        [Fact]
        public void TheKeyHint_NamesTheEndpointTheKeyIsSavedFor()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(new PyNavisConfig.UserSettings(), hasKey: true,
                    keySavedFor: "https://api.anthropic.com");

                Assert.Contains("https://api.anthropic.com", SettingsDialog.AiSectionTextForTest(window));
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

        private static PyNavisConfig.UserSettings OpenAiAt(string baseUrl)
        {
            var settings = new PyNavisConfig.UserSettings();
            settings.Ai.Provider = AiProvider.OpenAiCompatible;
            settings.Ai.BaseUrl = baseUrl;
            return settings;
        }

        private static void InTempFolder(Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try { body(dir); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // The key used to be written first: when config.json then refused the save (not
        // valid JSON, locked), the key was already bound to the new provider while the
        // config still named the old one. A failed save must leave the key where it was.
        [Fact]
        public void AConfigSaveThatFails_LeavesTheSavedKeyWhereItWas()
        {
            InTempFolder(dir =>
            {
                var path = Path.Combine(dir, "config.json");
                File.WriteAllText(path, "{ not json");
                var secrets = new SecretStore(Path.Combine(dir, "secrets.json"));
                ApiKeys.Save(secrets, "old-key", new AiSettings { Provider = AiProvider.Anthropic });

                Assert.ThrowsAny<Exception>(() => SettingsDialog.SaveEdited(path, new PyNavisConfig.UserSettings(),
                    OpenAiAt("https://api.openai.com/v1"), secrets, "new-key", removeKey: false));

                Assert.Equal("old-key", secrets.Get(SecretStore.AiKeyName));
                Assert.Equal("https://api.anthropic.com", ApiKeys.SavedFor(secrets));
                Assert.Equal("{ not json", File.ReadAllText(path));
            });
        }

        // Saved in the other order, a key that cannot be written (secrets.json locked,
        // its folder unusable) must still be reported, saying the rest was saved.
        [Fact]
        public void AKeyThatCannotBeSaved_IsReported_AfterTheConfigWasSaved()
        {
            InTempFolder(dir =>
            {
                var path = Path.Combine(dir, "config.json");
                var blocker = Path.Combine(dir, "not-a-folder");
                File.WriteAllText(blocker, "");
                var secrets = new SecretStore(Path.Combine(blocker, "secrets.json"));

                var error = Assert.ThrowsAny<Exception>(() => SettingsDialog.SaveEdited(path,
                    new PyNavisConfig.UserSettings(), OpenAiAt("https://api.openai.com/v1"), secrets,
                    "new-key", removeKey: false));

                Assert.Contains("API key", error.Message);
                Assert.Equal(AiProvider.OpenAiCompatible, PyNavisConfig.Load(path).Ai.Provider);
            });
        }

        [Fact]
        public void ASave_WritesTheConfigAndTheKeyForTheNewEndpoint()
        {
            InTempFolder(dir =>
            {
                var path = Path.Combine(dir, "config.json");
                var secrets = new SecretStore(Path.Combine(dir, "secrets.json"));

                var outcome = SettingsDialog.SaveEdited(path, new PyNavisConfig.UserSettings(),
                    OpenAiAt("https://api.openai.com/v1"), secrets, "new-key", removeKey: false);

                Assert.Equal(SettingsDialog.Outcome.NeedsReload, outcome);
                Assert.Equal(AiProvider.OpenAiCompatible, PyNavisConfig.Load(path).Ai.Provider);
                Assert.Equal("new-key", secrets.Get(SecretStore.AiKeyName));
                Assert.Equal("https://api.openai.com", ApiKeys.SavedFor(secrets));
            });
        }
    }
}
