using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// API keys, in %APPDATA%\pyNavis\secrets.json, each value encrypted with DPAPI for
    /// the current Windows user. config.json gets shared, screenshotted and pasted into
    /// bug reports; this file is useless to anyone but the account that wrote it.
    ///
    /// Reads never throw: an unreadable file or an entry the machine cannot decrypt
    /// (copied from another user, edited by hand) reads as "no key", which the UI
    /// turns into "enter your key".
    /// </summary>
    public sealed class SecretStore
    {
        public const string AiKeyName = "ai.key";

        private readonly string _path;

        public SecretStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>The store beside the user's config.json.</summary>
        public static SecretStore Default => new SecretStore(Path.Combine(
            Path.GetDirectoryName(RuntimeHost.UserConfigPath), "secrets.json"));

        public string FilePath => _path;

        public bool Has(string name) => !string.IsNullOrEmpty(Get(name));

        public string Get(string name)
        {
            var map = Read();
            if (!map.TryGetValue(name, out var raw) || !(raw is string encoded) || encoded.Length == 0)
                return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encoded), Entropy,
                    DataProtectionScope.CurrentUser);
                var text = Encoding.UTF8.GetString(bytes);
                return text.Length == 0 ? null : text;
            }
            catch (Exception ex)
            {
                Log.Error($"Secret '{name}' could not be decrypted; treating it as absent.", ex);
                return null;
            }
        }

        /// <summary>Null or empty removes the entry. IO errors propagate.</summary>
        public void Set(string name, string value)
        {
            var map = Read();
            if (string.IsNullOrEmpty(value))
            {
                map.Remove(name);
            }
            else
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy,
                    DataProtectionScope.CurrentUser);
                map[name] = Convert.ToBase64String(bytes);
            }
            Write(map);
        }

        // Fixed extra entropy: not a secret, just keeps a stray DPAPI blob from
        // another program from decoding as ours.
        private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("pyNavis.secrets.v1");

        private Dictionary<string, object> Read()
        {
            try
            {
                if (!File.Exists(_path)) return new Dictionary<string, object>();
                var text = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text)
                    ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                Log.Error($"secrets.json at '{_path}' is unreadable; treating it as empty.", ex);
                return new Dictionary<string, object>();
            }
        }

        private void Write(Dictionary<string, object> map)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonPretty.Write(map));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}
