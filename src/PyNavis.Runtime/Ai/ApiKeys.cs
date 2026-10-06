using System;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// The saved API key belongs to the endpoint it was saved for (its origin: scheme,
    /// host and port, kept beside it in secrets.json). It is sent only back to that
    /// origin, and only over https or to a server on this computer, so switching provider
    /// or pointing the panel at a local model server never hands the key to another host
    /// or puts it on the network in clear text.
    /// </summary>
    public static class ApiKeys
    {
        public const string EndpointName = "ai.key.endpoint";

        /// <summary>"scheme://host[:port]" in lower case, or null for anything that is
        /// not an absolute http or https URL.</summary>
        public static string Origin(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
            return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        }

        /// <summary>True for https, and for plain http only when the server is this computer.</summary>
        public static bool MaySendKeyTo(string url)
        {
            if (Origin(url) == null) return false;
            var uri = new Uri(url.Trim());
            return uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback;
        }

        /// <summary>The origin the saved key belongs to, or null.</summary>
        public static string SavedFor(SecretStore store) => store.Get(EndpointName);

        /// <summary>The key to send to the settings' endpoint, or null when there is none
        /// for it.</summary>
        public static string Resolve(SecretStore store, AiSettings settings)
        {
            var url = settings.EffectiveBaseUrl;
            if (!MaySendKeyTo(url)) return null;
            var key = store.Get(SecretStore.AiKeyName);
            if (key == null) return null;
            return string.Equals(SavedFor(store), Origin(url), StringComparison.Ordinal) ? key : null;
        }

        /// <summary>The key for the saved settings, adopting a 1.2.0 key first. What the
        /// running panel uses; the Settings window's unsaved values never adopt.</summary>
        public static string ForSaved(SecretStore store, AiSettings saved)
        {
            AdoptLegacy(store, saved);
            return Resolve(store, saved);
        }

        public static void Save(SecretStore store, string key, AiSettings settings)
        {
            store.Set(SecretStore.AiKeyName, key);
            store.Set(EndpointName, Origin(settings.EffectiveBaseUrl));
        }

        /// <summary>pyNavis 1.2.0 saved the key with no endpoint. Bind such a key to the
        /// saved settings' endpoint, but only one a key may travel to.</summary>
        public static void AdoptLegacy(SecretStore store, AiSettings saved)
        {
            if (!store.Has(SecretStore.AiKeyName) || SavedFor(store) != null) return;
            if (!MaySendKeyTo(saved.EffectiveBaseUrl)) return;
            store.Set(EndpointName, Origin(saved.EffectiveBaseUrl));
        }

        /// <summary>What Save in the Settings window does with the key box: a new key is
        /// saved for the edited endpoint, Remove deletes the key, nothing entered keeps the
        /// key where it was.</summary>
        public static void Apply(SecretStore store, string enteredKey, bool removeRequested, AiSettings edited)
        {
            if (enteredKey != null) Save(store, enteredKey, edited);
            else if (removeRequested)
            {
                store.Set(SecretStore.AiKeyName, null);
                store.Set(EndpointName, null);
            }
        }

        /// <summary>The key for the Test button, against the unsaved settings in the window:
        /// a typed key goes where keys may go (anywhere else throws, with the reason as the
        /// message), the saved key only to its own endpoint.</summary>
        public static string ForConnectionTest(SecretStore store, string enteredKey, AiSettings typed)
        {
            if (enteredKey == null) return Resolve(store, typed);
            if (!MaySendKeyTo(typed.EffectiveBaseUrl))
                throw new InvalidOperationException(
                    "Keys are only sent over https, or to a server on this computer. "
                    + "Clear the key box to test this server without one.");
            return enteredKey;
        }
    }
}
