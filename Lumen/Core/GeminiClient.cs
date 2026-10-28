using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class GeminiException : Exception
    {
        public int Status { get; private set; }
        public GeminiException(int status, string message) : base(message) { Status = status; }
    }

    [Windows.UI.Xaml.Data.Bindable]
    public sealed class ModelInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public override string ToString() { return Id; }
    }

    public sealed class StreamResult
    {
        public string FinishReason { get; set; }
        public string BlockReason { get; set; }
    }

    /// <summary>
    /// Minimal Gemini REST client built only on WinRT APIs (no NuGet deps), tuned for low-end phones:
    /// one shared HttpClient, true incremental SSE reading, no full-response buffering.
    /// </summary>
    public sealed class GeminiClient : IDisposable
    {
        private readonly HttpClient _http;

        public GeminiClient()
        {
            var filter = new HttpBaseProtocolFilter { AllowUI = false };
            filter.CacheControl.ReadBehavior = HttpCacheReadBehavior.MostRecent;
            filter.CacheControl.WriteBehavior = HttpCacheWriteBehavior.NoCache;
            _http = new HttpClient(filter);
            _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Lumen/1.0 (Windows 10 Mobile)");
        }

        public void Dispose() { _http.Dispose(); }

        // ---------------------------------------------------------------- models

        public async Task<List<ModelInfo>> ListModelsAsync(string apiKey, string baseUrl, CancellationToken ct)
        {
            var result = new List<ModelInfo>();
            string pageToken = null;
            do
            {
                var url = baseUrl + "/v1beta/models?pageSize=1000" + (pageToken != null ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Add("x-goog-api-key", apiKey);
                using (var resp = await _http.SendRequestAsync(req, HttpCompletionOption.ResponseContentRead).AsTask(ct))
                {
                    var body = await resp.Content.ReadAsStringAsync().AsTask(ct);
                    if (!resp.IsSuccessStatusCode) throw ToError((int)resp.StatusCode, body);

                    JsonObject root;
                    if (!JsonObject.TryParse(body, out root)) break;
                    var models = root.GetNamedArray("models", new JsonArray());
                    foreach (var v in models)
                    {
                        if (v.ValueType != JsonValueType.Object) continue;
                        var o = v.GetObject();
                        var methods = o.GetNamedArray("supportedGenerationMethods", new JsonArray());
                        if (!methods.Any(m => m.ValueType == JsonValueType.String && m.GetString() == "generateContent")) continue;
                        var id = o.GetNamedString("name", "").Replace("models/", "");
                        if (!IsChatModel(id)) continue;
                        result.Add(new ModelInfo { Id = id, DisplayName = o.GetNamedString("displayName", id) });
                    }
                    pageToken = root.GetNamedString("nextPageToken", "");
                    if (pageToken.Length == 0) pageToken = null;
                }
            } while (pageToken != null);

            return result
                .OrderByDescending(m => m.Id.EndsWith("-latest"))
                .ThenByDescending(m => m.Id.Contains("flash"))
                .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsChatModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string[] skip = { "embedding", "aqa", "imagen", "tts", "image-generation", "-image", "native-audio", "live", "veo", "robotics", "computer-use" };
            foreach (var s in skip) if (id.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return id.StartsWith("gemini") || id.StartsWith("gemma") || id.StartsWith("learnlm");
        }

        // ---------------------------------------------------------------- chat

        public async Task<StreamResult> StreamAsync(
            string apiKey, string baseUrl, string model, string systemPrompt, double temperature, bool webSearch,
            IList<ChatMessage> history, Action<string> onDelta, CancellationToken ct)
        {
            var body = await BuildBodyAsync(systemPrompt, temperature, webSearch, history);
            var url = baseUrl + "/v1beta/models/" + Uri.EscapeDataString(model) + ":streamGenerateContent?alt=sse";

            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Content = new HttpStringContent(body.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

            var result = new StreamResult();
            using (var resp = await _http.SendRequestAsync(req, HttpCompletionOption.ResponseHeadersRead).AsTask(ct))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync().AsTask(ct);
                    throw ToError((int)resp.StatusCode, err);
                }

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

                        // Process complete lines only; keep the tail for the next read.
                        int nl;
                        while ((nl = IndexOf(pending, '\n')) >= 0)
                        {
                            var line = pending.ToString(0, nl).TrimEnd('\r');
                            pending.Remove(0, nl + 1);
                            HandleSseLine(line, onDelta, result);
                        }
                    }
                    if (pending.Length > 0) HandleSseLine(pending.ToString().TrimEnd('\r'), onDelta, result);
                }
            }
            return result;
        }

        private static int IndexOf(StringBuilder sb, char c)
        {
            for (int i = 0; i < sb.Length; i++) if (sb[i] == c) return i;
            return -1;
        }

        private static void HandleSseLine(string line, Action<string> onDelta, StreamResult result)
        {
            if (!line.StartsWith("data:")) return;
            var json = line.Substring(5).Trim();
            if (json.Length == 0 || json == "[DONE]") return;

            JsonObject chunk;
            if (!JsonObject.TryParse(json, out chunk)) return;

            if (chunk.ContainsKey("error"))
            {
                var e = chunk.GetNamedObject("error");
                throw new GeminiException((int)e.GetNamedNumber("code", 500), e.GetNamedString("message", "Stream error"));
            }

            var feedback = chunk.GetNamedObject("promptFeedback", null);
            if (feedback != null) result.BlockReason = feedback.GetNamedString("blockReason", result.BlockReason);

            var candidates = chunk.GetNamedArray("candidates", null);
            if (candidates == null || candidates.Count == 0) return;
            var cand = candidates[0].GetObject();
            var finish = cand.GetNamedString("finishReason", "");
            if (finish.Length > 0) result.FinishReason = finish;

            var content = cand.GetNamedObject("content", null);
            if (content == null) return;
            var parts = content.GetNamedArray("parts", null);
            if (parts == null) return;

            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (p.ValueType != JsonValueType.Object) continue;
                var po = p.GetObject();
                if (po.GetNamedBoolean("thought", false)) continue; // hide thinking summaries
                var t = po.GetNamedString("text", "");
                if (t.Length > 0) sb.Append(t);
            }
            if (sb.Length > 0) onDelta(sb.ToString());
        }

        private static async Task<JsonObject> BuildBodyAsync(string systemPrompt, double temperature, bool webSearch, IList<ChatMessage> history)
        {
            var contents = new JsonArray();
            foreach (var m in history)
            {
                if (m.IsError) continue;
                var parts = new JsonArray();
                if (!string.IsNullOrEmpty(m.ImageFile))
                {
                    var b64 = await ImageHelper.ReadBase64Async(m.ImageFile);
                    if (b64 != null)
                    {
                        var inline = new JsonObject();
                        inline["mimeType"] = JsonValue.CreateStringValue("image/jpeg");
                        inline["data"] = JsonValue.CreateStringValue(b64);
                        var part = new JsonObject();
                        part["inlineData"] = inline;
                        parts.Add(part);
                    }
                }
                if (!string.IsNullOrWhiteSpace(m.Text))
                {
                    var tp = new JsonObject();
                    tp["text"] = JsonValue.CreateStringValue(m.Text);
                    parts.Add(tp);
                }
                if (parts.Count == 0) continue;

                var c = new JsonObject();
                c["role"] = JsonValue.CreateStringValue(m.IsUser ? "user" : "model");
                c["parts"] = parts;
                contents.Add(c);
            }

            var body = new JsonObject();
            body["contents"] = contents;

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                var sp = new JsonObject();
                sp["text"] = JsonValue.CreateStringValue(systemPrompt);
                var spParts = new JsonArray();
                spParts.Add(sp);
                var si = new JsonObject();
                si["parts"] = spParts;
                body["systemInstruction"] = si;
            }

            var gen = new JsonObject();
            gen["temperature"] = JsonValue.CreateNumberValue(Math.Round(temperature, 2));
            body["generationConfig"] = gen;

            if (webSearch)
            {
                var tool = new JsonObject();
                tool["google_search"] = new JsonObject();
                var tools = new JsonArray();
                tools.Add(tool);
                body["tools"] = tools;
            }
            return body;
        }

        private static GeminiException ToError(int status, string body)
        {
            string message = "";
            JsonObject root;
            if (!string.IsNullOrEmpty(body) && JsonObject.TryParse(body, out root))
            {
                var e = root.GetNamedObject("error", null);
                if (e != null) message = e.GetNamedString("message", "");
            }
            return new GeminiException(status, string.IsNullOrEmpty(message) ? ("HTTP " + status) : message);
        }

        /// <summary>Human-friendly message for any failure, localized.</summary>
        public static string Describe(Exception ex)
        {
            var g = ex as GeminiException;
            if (g != null)
            {
                switch (g.Status)
                {
                    case 400:
                        if (g.Message.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0) return Loc.Get("ErrKey");
                        return Loc.Get("ErrBadRequest") + "\n" + g.Message;
                    case 401:
                    case 403: return Loc.Get("ErrKey") + "\n" + g.Message;
                    case 404: return Loc.Get("ErrModel");
                    case 429: return Loc.Get("ErrQuota");
                    case 500:
                    case 503: return Loc.Get("ErrBusy");
                    default: return "HTTP " + g.Status + ": " + g.Message;
                }
            }
            var hr = (uint)ex.HResult;
            // Certificate / TLS failures: old root store or missing cipher suites.
            if (hr == 0x80072F8F || hr == 0x80072F0D || hr == 0x80072F06 || hr == 0x80072F7D || hr == 0x80072F05)
                return Loc.Get("ErrTls") + string.Format(" (0x{0:X8})", hr);
            // No connection, DNS, timeout.
            if (hr == 0x80072EE7 || hr == 0x80072EFD || hr == 0x80072EE2 || hr == 0x80072EFE || hr == 0x80072F78)
                return Loc.Get("ErrNetwork") + string.Format(" (0x{0:X8})", hr);
            return Loc.Get("ErrGeneric") + "\n" + ex.Message + string.Format(" (0x{0:X8})", hr);
        }
    }
}
