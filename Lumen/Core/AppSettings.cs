using System;
using System.Collections.Generic;
using Windows.Data.Json;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Lumen.Core
{
    /// <summary>App settings. The API key lives in the Credential Locker, everything else in LocalSettings.</summary>
    public static class AppSettings
    {
        public const string DefaultModel = "gemini-flash-lite-latest";
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

        // ------------------------------------------------------------ key

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
                if (clean == _cachedKey) return;
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

        // ------------------------------------------------------------ model

        public static string Model
        {
            get { var m = Get("model2", DefaultModel); return string.IsNullOrWhiteSpace(m) ? DefaultModel : m; }
            set { Put("model2", (value ?? "").Trim()); }
        }

        /// <summary>Models shown in the dropdown. Seeded with the two stable aliases, replaced by the live list once the key is verified.</summary>
        public static List<ModelInfo> Models
        {
            get
            {
                var list = new List<ModelInfo>();
                JsonArray arr;
                if (JsonArray.TryParse(Get("modelsCache", ""), out arr))
                {
                    foreach (var v in arr)
                    {
                        if (v.ValueType != JsonValueType.Object) continue;
                        var o = v.GetObject();
                        var id = o.GetNamedString("id", "");
                        if (id.Length > 0) list.Add(new ModelInfo { Id = id, DisplayName = o.GetNamedString("name", id) });
                    }
                }
                if (list.Count == 0)
                {
                    list.Add(new ModelInfo { Id = "gemini-flash-lite-latest", DisplayName = "Gemini Flash-Lite (latest)" });
                    list.Add(new ModelInfo { Id = "gemini-flash-latest", DisplayName = "Gemini Flash (latest)" });
                }
                return list;
            }
            set
            {
                var arr = new JsonArray();
                foreach (var m in value)
                {
                    var o = new JsonObject();
                    o["id"] = JsonValue.CreateStringValue(m.Id);
                    o["name"] = JsonValue.CreateStringValue(m.DisplayName ?? m.Id);
                    arr.Add(o);
                }
                Put("modelsCache", arr.Stringify());
            }
        }

        public static string ModelDisplayName
        {
            get
            {
                var id = Model;
                foreach (var m in Models) if (m.Id == id) return m.DisplayName;
                return id;
            }
        }

        // ------------------------------------------------------------ assistant

        public static double Temperature
        {
            get { return Get("temperature", 1.0); }
            set { Put("temperature", Math.Max(0, Math.Min(2, value))); }
        }

        /// <summary>Null/empty means "use the built-in prompt".</summary>
        public static string CustomPrompt
        {
            get { return Get("customPrompt", ""); }
            set { Put("customPrompt", value ?? ""); }
        }

        public static string SystemPrompt
        {
            get { var c = CustomPrompt; return string.IsNullOrWhiteSpace(c) ? S.DefaultSystemPrompt : c; }
        }

        /// <summary>Let the model look things up on Wikipedia (free, replaces Google Search grounding).</summary>
        public static bool Wikipedia
        {
            get { return Get("wikipedia", true); }
            set { Put("wikipedia", value); }
        }

        public static string BaseUrl
        {
            get { var u = Get("baseUrl", DefaultBaseUrl); return string.IsNullOrWhiteSpace(u) ? DefaultBaseUrl : u.Trim().TrimEnd('/'); }
            set { Put("baseUrl", (value ?? "").Trim()); }
        }

        /// <summary>Image-capable model used by the generate_image tool (auto-picked from the key's model list).</summary>
        public static string ImageModel
        {
            get { return Get("imageModel", ""); }
            set { Put("imageModel", value ?? ""); }
        }

        /// <summary>Let Gemini create images and files on request.</summary>
        public static bool Creative
        {
            get { return Get("creative", true); }
            set { Put("creative", value); }
        }

        /// <summary>Voice id for "Read aloud", empty = system default.</summary>
        public static string VoiceId
        {
            get { return Get("voice", ""); }
            set { Put("voice", value ?? ""); }
        }

        // ------------------------------------------------------------ appearance

        public static int AccentIndex
        {
            get { return Get("accent", 0); }
            set { Put("accent", value); }
        }

        public static bool PureBlack
        {
            get { return Get("pureBlack", false); }
            set { Put("pureBlack", value); }
        }

        /// <summary>0 small, 1 normal, 2 large.</summary>
        public static int TextSize
        {
            get { return Get("textSize", 1); }
            set { Put("textSize", value); }
        }

        public static bool Animations
        {
            get { return Get("animations", true); }
            set { Put("animations", value); }
        }

        // ------------------------------------------------------------ state

        public static bool Onboarded
        {
            get { return Get("onboarded", false); }
            set { Put("onboarded", value); }
        }
    }
}
