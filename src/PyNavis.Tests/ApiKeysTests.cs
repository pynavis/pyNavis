using System;
using System.IO;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The saved API key belongs to the endpoint it was saved for. Switching provider or
    /// pointing the panel at a local server must not hand that key to a different host,
    /// and a key never travels over plain http to another machine.
    /// </summary>
    public class ApiKeysTests : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), "pynavis-apikeys-" + Guid.NewGuid() + ".json");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private static AiSettings Anthropic() => new AiSettings { Provider = AiProvider.Anthropic };

        private static AiSettings OpenAi(string baseUrl = null) =>
            new AiSettings { Provider = AiProvider.OpenAiCompatible, BaseUrl = baseUrl };

        [Theory]
        [InlineData("https://api.anthropic.com", "https://api.anthropic.com")]
        [InlineData("https://API.OpenAI.com/v1/", "https://api.openai.com")]
        [InlineData("http://localhost:11434/v1", "http://localhost:11434")]
        [InlineData("https://example.com:8443/openai/v1", "https://example.com:8443")]
        public void Origin_IsSchemeHostAndPort(string url, string origin)
        {
            Assert.Equal(origin, ApiKeys.Origin(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a url")]
        [InlineData("ftp://example.com")]
        [InlineData("file:///C:/keys")]
        public void Origin_OfAnythingButHttp_IsNull(string url)
        {
            Assert.Null(ApiKeys.Origin(url));
        }

        [Theory]
        [InlineData("https://api.anthropic.com", true)]
        [InlineData("http://localhost:11434/v1", true)]
        [InlineData("http://127.0.0.1:1234/v1", true)]
        [InlineData("http://[::1]:1234/v1", true)]
        [InlineData("http://192.168.1.20:11434/v1", false)]
        [InlineData("http://gpu-box/v1", false)]
        [InlineData("garbage", false)]
        public void AKey_TravelsOnlyOverHttps_OrToThisComputer(string url, bool allowed)
        {
            Assert.Equal(allowed, ApiKeys.MaySendKeyTo(url));
        }

        [Fact]
        public void TheSavedKey_GoesToTheEndpointItWasSavedFor()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            Assert.Equal("sk-ant-test", ApiKeys.Resolve(store, Anthropic()));
        }

        [Fact]
        public void SwitchingProvider_DoesNotSendTheOldKeyToTheNewHost()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            Assert.Null(ApiKeys.Resolve(store, OpenAi()));
        }

        [Fact]
        public void PointingAtALanServer_DoesNotSendTheKey()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            Assert.Null(ApiKeys.Resolve(store, OpenAi("http://192.168.1.20:11434/v1")));
        }

        [Fact]
        public void AKeySavedForAPlainHttpLanServer_IsStillNeverSent()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "lan-key", OpenAi("http://192.168.1.20:11434/v1"));

            Assert.Null(ApiKeys.Resolve(store, OpenAi("http://192.168.1.20:11434/v1")));
        }

        [Fact]
        public void AKeyFrom120_WithNoEndpoint_IsAdoptedByTheSavedHttpsEndpoint()
        {
            var store = new SecretStore(_path);
            store.Set(SecretStore.AiKeyName, "sk-ant-legacy");

            ApiKeys.AdoptLegacy(store, Anthropic());

            Assert.Equal("sk-ant-legacy", ApiKeys.Resolve(store, Anthropic()));
            Assert.Null(ApiKeys.Resolve(store, OpenAi()));
        }

        [Fact]
        public void AKeyFrom120_IsNotAdoptedByAPlainHttpLanEndpoint()
        {
            var store = new SecretStore(_path);
            store.Set(SecretStore.AiKeyName, "sk-ant-legacy");

            ApiKeys.AdoptLegacy(store, OpenAi("http://192.168.1.20:11434/v1"));

            Assert.Null(store.Get(ApiKeys.EndpointName));
            Assert.Null(ApiKeys.Resolve(store, OpenAi("http://192.168.1.20:11434/v1")));
        }

        [Fact]
        public void AdoptLegacy_LeavesAnAlreadyBoundKeyAlone()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            ApiKeys.AdoptLegacy(store, OpenAi());

            Assert.Equal("https://api.anthropic.com", ApiKeys.SavedFor(store));
        }

        [Fact]
        public void ForSaved_AdoptsA120Key_ThenResolvesIt()
        {
            var store = new SecretStore(_path);
            store.Set(SecretStore.AiKeyName, "sk-ant-legacy");

            Assert.Equal("sk-ant-legacy", ApiKeys.ForSaved(store, Anthropic()));
            Assert.Equal("https://api.anthropic.com", ApiKeys.SavedFor(store));
        }

        [Fact]
        public void Apply_WithANewKey_SavesItForTheEditedEndpoint()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-old", Anthropic());

            ApiKeys.Apply(store, "sk-openai-new", removeRequested: false, OpenAi());

            Assert.Equal("sk-openai-new", ApiKeys.Resolve(store, OpenAi()));
            Assert.Equal("https://api.openai.com", ApiKeys.SavedFor(store));
        }

        [Fact]
        public void Apply_WithRemove_DeletesTheKeyAndItsEndpoint()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            ApiKeys.Apply(store, null, removeRequested: true, Anthropic());

            Assert.False(store.Has(SecretStore.AiKeyName));
            Assert.Null(ApiKeys.SavedFor(store));
        }

        [Fact]
        public void Apply_WithNothingEntered_KeepsTheKeyBoundWhereItWas()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            ApiKeys.Apply(store, null, removeRequested: false, OpenAi());

            Assert.Equal("https://api.anthropic.com", ApiKeys.SavedFor(store));
            Assert.Null(ApiKeys.Resolve(store, OpenAi()));
        }

        [Fact]
        public void ForConnectionTest_SendsATypedKeyOnlyWhereKeysMayGo()
        {
            var store = new SecretStore(_path);

            Assert.Equal("typed", ApiKeys.ForConnectionTest(store, "typed", Anthropic()));
            Assert.Throws<InvalidOperationException>(() =>
                ApiKeys.ForConnectionTest(store, "typed", OpenAi("http://192.168.1.20:11434/v1")));
        }

        [Fact]
        public void ForConnectionTest_SendsTheSavedKeyOnlyToItsOwnEndpoint()
        {
            var store = new SecretStore(_path);
            ApiKeys.Save(store, "sk-ant-test", Anthropic());

            Assert.Equal("sk-ant-test", ApiKeys.ForConnectionTest(store, null, Anthropic()));
            Assert.Null(ApiKeys.ForConnectionTest(store, null, OpenAi("https://evil.example/v1")));
        }
    }
}
