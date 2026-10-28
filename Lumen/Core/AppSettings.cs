using System;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Lumen.Core
{
    /// <summary>App settings. The API key lives in the Credential Locker, everything else in LocalSettings.</summary>
    public static class AppSettings
    {
        public const string DefaultModel = "gemini-flash-latest";
        public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com";
        private const string VaultResource = "Lumen.GeminiApiKey";
        private const string VaultUser = "default";

        private static ApplicationDataContainer Store { get { return ApplicationData.Current.LocalSettings; } }

        private static T Get<T>(string key, T fallback)
        {
            object v;
            if (Store.Values.TryGetValue(key, out v) && v is T) return (T)v;
            return fallback;
        }

        private static void Put(string key, object value)
        {
            Store.Values[key] = value;
        }

        private static string _cachedKey;

        public static string ApiKey
        {
            get
            {
                if (_cachedKey != null) return _cachedKey;
                try
                {
                    var vault = new PasswordVault();
                    var cred = vault.Retrieve(VaultResource, VaultUser);
                    cred.RetrievePassword();
                    _cachedKey = cred.Password ?? "";
                }
                catch (Exception)
                {
                    _cachedKey = "";
                }
                return _cachedKey;
            }
            set
            {
                var clean = (value ?? "").Trim();
                var vault = new PasswordVault();
                try
                {
                    foreach (var c in vault.FindAllByResource(VaultResource)) vault.Remove(c);
                }
                catch (Exception) { }
                if (clean.Length > 0) vault.Add(new PasswordCredential(VaultResource, VaultUser, clean));
                _cachedKey = clean;
            }
        }

        public static bool HasApiKey { get { return ApiKey.Length > 0; } }

        public static string Model
        {
            get { var m = Get("model", DefaultModel); return string.IsNullOrWhiteSpace(m) ? DefaultModel : m; }
            set { Put("model", (value ?? "").Trim()); }
        }

        public static double Temperature
        {
            get { return Get("temperature", 1.0); }
            set { Put("temperature", Math.Max(0, Math.Min(2, value))); }
        }

        public static string SystemPrompt
        {
            get { return Get("systemPrompt", Loc.Get("DefaultSystemPrompt")); }
            set { Put("systemPrompt", value ?? ""); }
        }

        public static bool WebSearch
        {
            get { return Get("webSearch", false); }
            set { Put("webSearch", value); }
        }

        public static string BaseUrl
        {
            get { var u = Get("baseUrl", DefaultBaseUrl); return string.IsNullOrWhiteSpace(u) ? DefaultBaseUrl : u.Trim().TrimEnd('/'); }
            set { Put("baseUrl", (value ?? "").Trim()); }
        }

        public static string LastConversationId
        {
            get { return Get<string>("lastConversation", null); }
            set { Put("lastConversation", value); }
        }
    }
}
