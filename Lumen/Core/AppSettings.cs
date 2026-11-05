using System;
using System.Collections.Generic;
using Windows.Data.Json;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Lumen.Core
{
    /// <summary>App settings. API keys live in the Credential Locker, everything else in LocalSettings.</summary>
    public static class AppSettings
    {
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

        // ------------------------------------------------------------ provider

        /// <summary>Which AI service answers: gemini, anthropic, openai or custom (any OpenAI-compatible endpoint).</summary>
        public static string ProviderId
        {
            get { var p = Get("provider", "gemini"); return Providers.Find(p) != null ? p : "gemini"; }
            set { Put("provider", value ?? "gemini"); }
        }

        public static Provider Provider { get { return Providers.Find(ProviderId); } }

        // Gemini keeps its pre-1.4 storage keys so existing users keep their key, model and list.
        private static string K(string baseKey, string provider)
        {
            return provider == "gemini" ? baseKey : baseKey + "." + provider;
        }

        // ------------------------------------------------------------ keys (Credential Locker)

        private static readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

        private static string VaultFor(string provider)
        {
            return provider == "gemini" ? VaultResource : "Lumen.Key." + provider;
        }

        public static string KeyFor(string provider)
        {
            string cached;
            lock (_keys) if (_keys.TryGetValue(provider, out cached)) return cached;
            string key = "";
            try
            {
                var cred = new PasswordVault().Retrieve(VaultFor(provider), VaultUser);
                cred.RetrievePassword();
                key = cred.Password ?? "";
            }
            catch (Exception) { }
            lock (_keys) _keys[provider] = key;
            return key;
        }

        public static void SetKeyFor(string provider, string value)
        {
            var clean = (value ?? "").Trim();
            if (clean == KeyFor(provider)) return;
            var vault = new PasswordVault();
            try
            {
                foreach (var c in vault.FindAllByResource(VaultFor(provider))) vault.Remove(c);
            }
            catch (Exception) { }
            if (clean.Length > 0) vault.Add(new PasswordCredential(VaultFor(provider), VaultUser, clean));
            lock (_keys) _keys[provider] = clean;
        }

        public static bool HasKeyFor(string provider) { return KeyFor(provider).Length > 0; }

        /// <summary>Key of the active provider.</summary>
        public static string ApiKey
        {
            get { return KeyFor(ProviderId); }
            set { SetKeyFor(ProviderId, value); }
        }

        /// <summary>A custom endpoint may need no key at all (local servers); everything else does.</summary>
        public static bool HasApiKey { get { return ApiKey.Length > 0 || (ProviderId == "custom" && HasBaseUrlFor("custom")); } }

        // ------------------------------------------------------------ models

        public static string ModelFor(string provider)
        {
            var m = Get(K("model2", provider), "");
            if (!string.IsNullOrWhiteSpace(m)) return m;
            var def = Providers.Find(provider).DefaultModel;
            var list = ModelsFor(provider);
            foreach (var x in list) if (x.Id == def) return def;
            return list.Count > 0 ? list[0].Id : def;
        }

        public static void SetModelFor(string provider, string value) { Put(K("model2", provider), (value ?? "").Trim()); }

        public static string Model
        {
            get { return ModelFor(ProviderId); }
            set { SetModelFor(ProviderId, value); }
        }

        /// <summary>Models shown in the dropdown: the provider's defaults until the key is checked, then the live list.</summary>
        public static List<ModelInfo> ModelsFor(string provider)
        {
            var list = new List<ModelInfo>();
            JsonArray arr;
            if (JsonArray.TryParse(ReadModelsFile(provider) ?? Get(K("modelsCache", provider), ""), out arr))
            {
                foreach (var v in arr)
                {
                    if (v.ValueType != JsonValueType.Object) continue;
                    var o = v.GetObject();
                    var id = o.GetNamedString("id", "");
                    if (id.Length > 0) list.Add(new ModelInfo { Id = id, DisplayName = o.GetNamedString("name", id) });
                }
            }
            if (list.Count == 0) list.AddRange(Providers.Find(provider).Defaults());
            return list;
        }

        public static void SetModelsFor(string provider, List<ModelInfo> value)
        {
            var arr = new JsonArray();
            foreach (var m in value)
            {
                var o = new JsonObject();
                o["id"] = JsonValue.CreateStringValue(m.Id);
                o["name"] = JsonValue.CreateStringValue(m.DisplayName ?? m.Id);
                arr.Add(o);
            }
            // A file, not LocalSettings: one setting is capped at 8 KB and OpenRouter lists hundreds of models.
            try
            {
                System.IO.File.WriteAllText(ModelsPath(provider), arr.Stringify());
                lock (_models) _models[provider] = arr.Stringify();
            }
            catch (Exception) { }
        }

        private static readonly Dictionary<string, string> _models = new Dictionary<string, string>();

        private static string ModelsPath(string provider)
        {
            return System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "models." + provider + ".json");
        }

        private static string ReadModelsFile(string provider)
        {
            string cached;
            lock (_models) if (_models.TryGetValue(provider, out cached)) return cached;
            string text = null;
            try { if (System.IO.File.Exists(ModelsPath(provider))) text = System.IO.File.ReadAllText(ModelsPath(provider)); }
            catch (Exception) { }
            if (text != null) lock (_models) _models[provider] = text;
            return text;
        }

        public static List<ModelInfo> Models
        {
            get { return ModelsFor(ProviderId); }
            set { SetModelsFor(ProviderId, value); }
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

        public static string BaseUrlFor(string provider)
        {
            var d = Providers.Find(provider).DefaultBaseUrl;
            var u = Get(K("baseUrl", provider), d);
            return string.IsNullOrWhiteSpace(u) ? d : u.Trim().TrimEnd('/');
        }

        public static void SetBaseUrlFor(string provider, string value) { Put(K("baseUrl", provider), (value ?? "").Trim()); }

        public static bool HasBaseUrlFor(string provider) { return BaseUrlFor(provider).Length > 0; }

        public static string BaseUrl
        {
            get { return BaseUrlFor(ProviderId); }
            set { SetBaseUrlFor(ProviderId, value); }
        }

        /// <summary>Image-capable model used by the generate_image tool (auto-picked from the key's model list).</summary>
        public static string ImageModelFor(string provider) { return Get(K("imageModel", provider), ""); }

        public static void SetImageModelFor(string provider, string value) { Put(K("imageModel", provider), value ?? ""); }

        /// <summary>Let the assistant create images and files on request.</summary>
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
            get { return Get("accent2", 0); }
            set { Put("accent2", value); }
        }

        /// <summary>Windows Phone light theme (white background) instead of dark.</summary>
        public static bool LightTheme
        {
            get { return Get("lightTheme", false); }
            set { Put("lightTheme", value); }
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
