using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace Lumen.Core
{
    /// <summary>One AI service Lumen can talk to.</summary>
    [Windows.UI.Xaml.Data.Bindable]
    public sealed class Provider
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>Company shown in error details ("Google: ...").</summary>
        public string Vendor { get; set; }
        public string DefaultBaseUrl { get; set; }
        public string DefaultModel { get; set; }
        public string KeyPlaceholder { get; set; }
        public string KeyUrl { get; set; }
        public string Initial { get; set; }
        /// <summary>Understands voice messages natively.</summary>
        public bool Audio { get; set; }
        /// <summary>Can draw images with the same key.</summary>
        public bool Images { get; set; }
        public string[] DefaultModels { get; set; }

        public List<ModelInfo> Defaults()
        {
            var list = new List<ModelInfo>();
            if (DefaultModels == null) return list;
            for (int i = 0; i + 1 < DefaultModels.Length; i += 2)
                list.Add(new ModelInfo { Id = DefaultModels[i], DisplayName = DefaultModels[i + 1] });
            return list;
        }

        public override string ToString() { return Name; }
    }

    public static class Providers
    {
        public static readonly List<Provider> All = new List<Provider>
        {
            new Provider
            {
                Id = "gemini", Name = "Google Gemini", Vendor = "Google", Initial = "G",
                DefaultBaseUrl = "https://generativelanguage.googleapis.com",
                DefaultModel = "gemini-flash-lite-latest", KeyPlaceholder = "AIza...",
                KeyUrl = "https://aistudio.google.com/apikey", Audio = true, Images = true,
                DefaultModels = new[] { "gemini-flash-lite-latest", "Gemini Flash-Lite (latest)", "gemini-flash-latest", "Gemini Flash (latest)" }
            },
            new Provider
            {
                Id = "anthropic", Name = "Anthropic Claude", Vendor = "Anthropic", Initial = "C",
                DefaultBaseUrl = "https://api.anthropic.com",
                DefaultModel = "claude-haiku-5-5", KeyPlaceholder = "sk-ant-...",
                KeyUrl = "https://console.anthropic.com/settings/keys",
                DefaultModels = new[] { "claude-haiku-5-5", "Claude Haiku 5.5", "claude-sonnet-5-5", "Claude Sonnet 5.5", "claude-opus-5-5", "Claude Opus 5.5" }
            },
            new Provider
            {
                Id = "openai", Name = "OpenAI ChatGPT", Vendor = "OpenAI", Initial = "O",
                DefaultBaseUrl = "https://api.openai.com/v1",
                DefaultModel = "gpt-6-luna", KeyPlaceholder = "sk-...",
                KeyUrl = "https://platform.openai.com/api-keys", Images = true,
                DefaultModels = new[] { "gpt-6-luna", "GPT-6 Luna", "gpt-6-sol", "GPT-6 Sol", "gpt-6.1-terra", "GPT-6.1 Terra" }
            },
            new Provider
            {
                Id = "custom", Name = "OpenAI compatible", Vendor = "Server", Initial = "+",
                DefaultBaseUrl = "", DefaultModel = "", KeyPlaceholder = "API key (if needed)",
                KeyUrl = "https://openrouter.ai/keys"
            },
        };

        public static Provider Find(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }
    }

    /// <summary>One shared HttpClient for every provider, plus a streaming line reader for SSE.</summary>
    public static class Net
    {
        private static HttpClient _http;

        public static HttpClient Http
        {
            get
            {
                if (_http == null)
                {
                    var filter = new HttpBaseProtocolFilter { AllowUI = false };
                    filter.CacheControl.ReadBehavior = HttpCacheReadBehavior.MostRecent;
                    filter.CacheControl.WriteBehavior = HttpCacheWriteBehavior.NoCache;
                    _http = new HttpClient(filter);
                    _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Lumen/1.4 (Windows 10 Mobile)");
                }
                return _http;
            }
        }

        public static HttpStringContent Json(JsonObject body)
        {
            return new HttpStringContent(body.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
        }

        /// <summary>Reads the response body line by line as it arrives (no buffering of the whole answer).</summary>
        public static async Task ReadLinesAsync(HttpResponseMessage resp, Action<string> onLine, CancellationToken ct)
        {
            using (var input = await resp.Content.ReadAsInputStreamAsync().AsTask(ct))
            {
                var decoder = Encoding.UTF8.GetDecoder();
                var buffer = new Windows.Storage.Streams.Buffer(8192);
                var bytes = new byte[8192];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(8192)];
                var pending = new StringBuilder();

                while (true)
                {
                    var read = await input.ReadAsync(buffer, buffer.Capacity, InputStreamOptions.Partial).AsTask(ct);
                    if (read.Length == 0) break;
                    read.CopyTo(0u, bytes, 0, (int)read.Length);
                    int n = decoder.GetChars(bytes, 0, (int)read.Length, chars, 0);
                    pending.Append(chars, 0, n);

                    int nl;
                    while ((nl = IndexOf(pending, '\n')) >= 0)
                    {
                        var line = pending.ToString(0, nl).TrimEnd('\r');
                        pending.Remove(0, nl + 1);
                        onLine(line);
                    }
                }
                if (pending.Length > 0) onLine(pending.ToString().TrimEnd('\r'));
            }
        }

        private static int IndexOf(StringBuilder sb, char c)
        {
            for (int i = 0; i < sb.Length; i++) if (sb[i] == c) return i;
            return -1;
        }

        /// <summary>{"error":{"message":...}} is shared by Google, OpenAI and Anthropic.</summary>
        public static GeminiException Error(int status, string body)
        {
            string message = "";
            JsonObject root;
            if (!string.IsNullOrEmpty(body) && JsonObject.TryParse(body, out root))
            {
                var e = root.GetNamedValue("error", null);
                if (e != null && e.ValueType == JsonValueType.Object) message = e.GetObject().GetNamedString("message", "");
                else if (e != null && e.ValueType == JsonValueType.String) message = e.GetString();
            }
            return new GeminiException(status, string.IsNullOrEmpty(message) ? ("HTTP " + status) : message);
        }

        /// <summary>Gemini declarations use OBJECT/STRING; JSON Schema (OpenAI, Anthropic) wants lowercase.</summary>
        public static JsonObject LowerSchema(JsonObject schema)
        {
            var copy = new JsonObject();
            foreach (var kv in schema)
            {
                var v = kv.Value;
                if (kv.Key == "type" && v.ValueType == JsonValueType.String) copy[kv.Key] = JsonValue.CreateStringValue(v.GetString().ToLowerInvariant());
                else if (v.ValueType == JsonValueType.Object) copy[kv.Key] = LowerSchema(v.GetObject());
                else copy[kv.Key] = JsonValue.Parse(v.Stringify());
            }
            return copy;
        }

        public static JsonObject Clone(JsonObject o) { return JsonObject.Parse(o.Stringify()); }
    }

    /// <summary>Runs the local tools (Wikipedia, images, files) for any provider.</summary>
    public static class ToolRunner
    {
        public static List<JsonObject> Declarations(ToolOptions tools)
        {
            var list = new List<JsonObject>();
            if (tools.Wikipedia) list.Add(WikipediaClient.Declaration());
            if (tools.Creative) list.AddRange(FileTools.Declarations(FileTools.CanDrawImages));
            return list;
        }

        public static string SystemPrompt(string systemPrompt, ToolOptions tools)
        {
            var system = systemPrompt + "\n\n" + DateLine();
            if (tools.Wikipedia) system += "\n" + WikipediaClient.PromptHint;
            if (tools.Creative) system += "\n" + FileTools.Hint(FileTools.CanDrawImages);
            return system;
        }

        private static string DateLine()
        {
            var now = DateTimeOffset.Now;
            return "Current date and time: " + now.ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) +
                   " (UTC" + (now.Offset < TimeSpan.Zero ? "-" : "+") + now.Offset.ToString("hh\\:mm") + "). " +
                   "Your training data may be older than this date: never claim something doesn't exist yet just because you don't know it.";
        }

        public static async Task<string> RunAsync(string name, JsonObject args, Action<string> onStatus, Action<Artifact> onArtifact, CancellationToken ct)
        {
            if (args == null) args = new JsonObject();
            if (name == WikipediaClient.ToolName)
            {
                var query = args.GetNamedString("query", "");
                onStatus?.Invoke(string.Format(S.LookingUp, query));
                return await WikipediaClient.SearchAsync(query, args.GetNamedString("lang", "en"), ct);
            }
            if (name == FileTools.ImageTool)
            {
                onStatus?.Invoke(S.CreatingImage);
                var r = await FileTools.GenerateImageAsync(args, ct);
                if (r.Item2 != null) onArtifact?.Invoke(r.Item2);
                return r.Item1;
            }
            if (name == FileTools.FileTool)
            {
                onStatus?.Invoke(S.CreatingFile);
                var r = await FileTools.CreateFileAsync(args);
                if (r.Item2 != null) onArtifact?.Invoke(r.Item2);
                return r.Item1;
            }
            return "Unknown function.";
        }

        public static JsonObject ParseArgs(string json)
        {
            JsonObject o;
            return !string.IsNullOrWhiteSpace(json) && JsonObject.TryParse(json, out o) ? o : new JsonObject();
        }
    }

    /// <summary>Single entry point: picks the client for the active provider.</summary>
    public static class Ai
    {
        public static Task<List<ModelInfo>> ListModelsAsync(string provider, string apiKey, string baseUrl, CancellationToken ct)
        {
            switch (provider)
            {
                case "anthropic": return AnthropicClient.ListModelsAsync(apiKey, baseUrl, ct);
                case "openai":
                case "custom": return OpenAIClient.ListModelsAsync(provider, apiKey, baseUrl, ct);
                default: return GeminiClient.Shared.ListModelsAsync(apiKey, baseUrl, ct);
            }
        }

        public static Task<StreamResult> StreamAsync(string provider, string apiKey, string baseUrl, string model, string systemPrompt,
            double temperature, ToolOptions tools, IList<ChatMessage> history,
            Action<string> onDelta, Action<string> onStatus, Action<Artifact> onArtifact, CancellationToken ct)
        {
            switch (provider)
            {
                case "anthropic": return AnthropicClient.StreamAsync(apiKey, baseUrl, model, systemPrompt, temperature, tools, history, onDelta, onStatus, onArtifact, ct);
                case "openai":
                case "custom": return OpenAIClient.StreamAsync(provider, apiKey, baseUrl, model, systemPrompt, temperature, tools, history, onDelta, onStatus, onArtifact, ct);
                default: return GeminiClient.Shared.StreamAsync(apiKey, baseUrl, model, systemPrompt, temperature, tools, history, onDelta, onStatus, onArtifact, ct);
            }
        }
    }
}
