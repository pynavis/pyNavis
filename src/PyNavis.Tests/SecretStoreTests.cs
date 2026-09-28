using System;
using System.IO;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// secrets.json beside config.json: API keys encrypted for the current Windows user,
    /// so a shared or screenshotted config.json never carries one.
    /// </summary>
    public class SecretStoreTests : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), "pynavis-secrets-" + Guid.NewGuid() + ".json");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Fact]
        public void MissingFile_HasNothing()
        {
            var store = new SecretStore(_path);

            Assert.Null(store.Get("ai.key"));
            Assert.False(store.Has("ai.key"));
        }

        [Fact]
        public void Set_ThenGet_RoundTrips_AndTheFileHoldsNoPlaintext()
        {
            var store = new SecretStore(_path);

            store.Set("ai.key", "sk-ant-very-secret-12345");

            Assert.Equal("sk-ant-very-secret-12345", new SecretStore(_path).Get("ai.key"));
            Assert.DoesNotContain("very-secret", File.ReadAllText(_path));
        }

        [Fact]
        public void Set_WithNullOrEmpty_RemovesTheEntry()
        {
            var store = new SecretStore(_path);
            store.Set("ai.key", "abc");
            store.Set("other", "keep");

            store.Set("ai.key", "");

            Assert.Null(store.Get("ai.key"));
            Assert.Equal("keep", store.Get("other"));
        }

        [Fact]
        public void AnEntryThatCannotBeDecrypted_ReadsAsAbsent_InsteadOfThrowing()
        {
            File.WriteAllText(_path, "{\"ai.key\": \"bm90LWRwYXBp\"}");   // base64 of "not-dpapi"

            Assert.Null(new SecretStore(_path).Get("ai.key"));
        }

        [Fact]
        public void AFileThatIsNotJson_ReadsAsEmpty_AndIsReplacedOnSet()
        {
            File.WriteAllText(_path, "{ broken");
            var store = new SecretStore(_path);

            Assert.Null(store.Get("ai.key"));
            store.Set("ai.key", "abc");
            Assert.Equal("abc", new SecretStore(_path).Get("ai.key"));
        }
    }
}
