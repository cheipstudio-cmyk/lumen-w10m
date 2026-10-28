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

    public sealed class ToolOptions
    {
        public bool Wikipedia { get; set; }
        public bool Creative { get; set; }
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
        public static readonly GeminiClient Shared = new GeminiClient();

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
            var images = new List<string>();
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
                        if (IsImageModel(id)) images.Add(id);
                        if (!IsChatModel(id)) continue;
                        result.Add(new ModelInfo { Id = id, DisplayName = o.GetNamedString("displayName", id) });
                    }
                    pageToken = root.GetNamedString("nextPageToken", "");
                    if (pageToken.Length == 0) pageToken = null;
                }
            } while (pageToken != null);

            // Remember the best image-capable model for the generate_image tool.
            var best = images
                .OrderByDescending(i => i.Contains("flash"))
                .ThenByDescending(i => i.EndsWith("-latest"))
                .ThenByDescending(i => !i.Contains("preview"))
                .ThenByDescending(i => i, StringComparer.Ordinal)
                .FirstOrDefault();
            AppSettings.ImageModel = best ?? "";

            return result
                .OrderByDescending(m => m.Id.EndsWith("-latest"))
                .ThenByDescending(m => m.Id.Contains("flash"))
                .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsImageModel(string id)
        {
            return id.StartsWith("gemini") && id.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0
                   && id.IndexOf("imagen", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsChatModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string[] skip = { "embedding", "aqa", "imagen", "tts", "image-generation", "-image", "native-audio", "live", "veo", "robotics", "computer-use" };
            foreach (var s in skip) if (id.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return id.StartsWith("gemini") || id.StartsWith("gemma") || id.StartsWith("learnlm");
        }

        // ---------------------------------------------------------------- chat

        private const int MaxToolRounds = 3;

        /// <summary>
        /// Streams an answer. The model may call tools (Wikipedia, images, files); we run them locally
        /// and continue the same answer, up to a few rounds.
        /// </summary>
        public async Task<StreamResult> StreamAsync(
            string apiKey, string baseUrl, string model, string systemPrompt, double temperature, ToolOptions tools,
            IList<ChatMessage> history, Action<string> onDelta, Action<string> onStatus, Action<Artifact> onArtifact, CancellationToken ct)
        {
            var contents = await BuildContentsAsync(history);
            var system = systemPrompt + "\n\n" + DateLine();
            if (tools.Wikipedia) system += "\n" + WikipediaClient.PromptHint;
            if (tools.Creative) system += "\n" + FileTools.PromptHint;
            var result = new StreamResult();

            for (int round = 0; round <= MaxToolRounds; round++)
            {
                // On the last round calling is switched off (tools stay declared) so the model answers with what it has.
                var body = BuildBody(contents, system, temperature, tools, round < MaxToolRounds);
                var turn = new TurnState();
                await StreamOnceAsync(apiKey, baseUrl, model, body, turn, onDelta, result, ct);

                if (turn.Calls.Count == 0) break;

                // Echo the model turn verbatim (Gemini 3 needs its thought signatures back), then the results.
                var modelTurn = new JsonObject();
                modelTurn["role"] = JsonValue.CreateStringValue("model");
                modelTurn["parts"] = turn.RawParts;
                contents.Add(modelTurn);

                var responses = new JsonArray();
                foreach (var call in turn.Calls)
                {
                    var name = call.GetNamedString("name", "");
                    var args = call.GetNamedObject("args", new JsonObject());
                    string output;
                    if (name == WikipediaClient.ToolName)
                    {
                        var query = args.GetNamedString("query", "");
                        onStatus?.Invoke(string.Format(S.LookingUp, query));
                        output = await WikipediaClient.SearchAsync(query, args.GetNamedString("lang", "en"), ct);
                    }
                    else if (name == FileTools.ImageTool)
                    {
                        onStatus?.Invoke(S.CreatingImage);
                        var r = await FileTools.GenerateImageAsync(this, apiKey, baseUrl, args, ct);
                        output = r.Item1;
                        if (r.Item2 != null) onArtifact?.Invoke(r.Item2);
                    }
                    else if (name == FileTools.FileTool)
                    {
                        onStatus?.Invoke(S.CreatingFile);
                        var r = await FileTools.CreateFileAsync(args);
                        output = r.Item1;
                        if (r.Item2 != null) onArtifact?.Invoke(r.Item2);
                    }
                    else
                    {
                        output = "Unknown function.";
                    }

                    var resp = new JsonObject();
                    resp["name"] = JsonValue.CreateStringValue(name);
                    var id = call.GetNamedString("id", "");
                    if (id.Length > 0) resp["id"] = JsonValue.CreateStringValue(id);
                    var payload = new JsonObject();
                    payload["result"] = JsonValue.CreateStringValue(output);
                    resp["response"] = payload;
                    var part = new JsonObject();
                    part["functionResponse"] = resp;
                    responses.Add(part);
                }
                var userTurn = new JsonObject();
                userTurn["role"] = JsonValue.CreateStringValue("user");
                userTurn["parts"] = responses;
                contents.Add(userTurn);
                onStatus?.Invoke(null);
            }
            return result;
        }

        /// <summary>One non-streamed call to an image-capable model. Returns (mimeType, base64) or null.</summary>
        public async Task<Tuple<string, string>> GenerateImageAsync(string apiKey, string baseUrl, string model, string prompt, CancellationToken ct)
        {
            var text = new JsonObject();
            text["text"] = JsonValue.CreateStringValue(prompt);
            var parts = new JsonArray();
            parts.Add(text);
            var content = new JsonObject();
            content["role"] = JsonValue.CreateStringValue("user");
            content["parts"] = parts;
            var contents = new JsonArray();
            contents.Add(content);

            var modalities = new JsonArray();
            modalities.Add(JsonValue.CreateStringValue("TEXT"));
            modalities.Add(JsonValue.CreateStringValue("IMAGE"));
            var gen = new JsonObject();
            gen["responseModalities"] = modalities;

            var body = new JsonObject();
            body["contents"] = contents;
            body["generationConfig"] = gen;

            var url = baseUrl + "/v1beta/models/" + Uri.EscapeDataString(model) + ":generateContent";
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Content = new HttpStringContent(body.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(90));
                using (var resp = await _http.SendRequestAsync(req, HttpCompletionOption.ResponseContentRead).AsTask(cts.Token))
                {
                    var raw = await resp.Content.ReadAsStringAsync().AsTask(cts.Token);
                    if (!resp.IsSuccessStatusCode) throw ToError((int)resp.StatusCode, raw);
                    JsonObject root;
                    if (!JsonObject.TryParse(raw, out root)) return null;
                    var cands = root.GetNamedArray("candidates", new JsonArray());
                    Tuple<string, string> last = null;
                    foreach (var c in cands)
                    {
                        if (c.ValueType != JsonValueType.Object) continue;
                        var cont = c.GetObject().GetNamedObject("content", null);
                        var ps = cont?.GetNamedArray("parts", null);
                        if (ps == null) continue;
                        foreach (var p in ps)
                        {
                            if (p.ValueType != JsonValueType.Object) continue;
                            var po = p.GetObject();
                            if (po.GetNamedBoolean("thought", false)) continue; // draft images while "thinking"
                            var inline = po.GetNamedObject("inlineData", null);
                            if (inline == null) continue;
                            var data = inline.GetNamedString("data", "");
                            if (data.Length > 0) last = Tuple.Create(inline.GetNamedString("mimeType", "image/png"), data);
                        }
                    }
                    return last;
                }
            }
        }

        private static string DateLine()
        {
            var now = DateTimeOffset.Now;
            return "Current date and time: " + now.ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) +
                   " (UTC" + (now.Offset < TimeSpan.Zero ? "-" : "+") + now.Offset.ToString("hh\\:mm") + "). " +
                   "Your training data may be older than this date: never claim something doesn't exist yet just because you don't know it.";
        }

        private sealed class TurnState
        {
            public readonly JsonArray RawParts = new JsonArray();
            public readonly List<JsonObject> Calls = new List<JsonObject>();
        }

        private async Task StreamOnceAsync(string apiKey, string baseUrl, string model, JsonObject body,
            TurnState turn, Action<string> onDelta, StreamResult result, CancellationToken ct)
        {
            var url = baseUrl + "/v1beta/models/" + Uri.EscapeDataString(model) + ":streamGenerateContent?alt=sse";
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Content = new HttpStringContent(body.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

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
                            HandleSseLine(line, onDelta, result, turn);
                        }
                    }
                    if (pending.Length > 0) HandleSseLine(pending.ToString().TrimEnd('\r'), onDelta, result, turn);
                }
            }
        }

        private static int IndexOf(StringBuilder sb, char c)
        {
            for (int i = 0; i < sb.Length; i++) if (sb[i] == c) return i;
            return -1;
        }

        private static void HandleSseLine(string line, Action<string> onDelta, StreamResult result, TurnState turn)
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
            if (feedback != null) result.BlockReason = feedback.GetNamedString("blockReason", "");

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
                turn.RawParts.Add(JsonObject.Parse(po.Stringify()));

                var call = po.GetNamedObject("functionCall", null);
                if (call != null) { turn.Calls.Add(call); continue; }
                if (po.GetNamedBoolean("thought", false)) continue; // hide thinking summaries
                var t = po.GetNamedString("text", "");
                if (t.Length > 0) sb.Append(t);
            }
            if (sb.Length > 0) onDelta(sb.ToString());
        }

        private static async Task<JsonArray> BuildContentsAsync(IList<ChatMessage> history)
        {
            var contents = new JsonArray();
            foreach (var m in history)
            {
                if (m.IsError) continue;
                var parts = new JsonArray();
                if (!string.IsNullOrEmpty(m.ImageFile) && m.IsUser)
                {
                    var b64 = await ImageHelper.ReadBase64Async("images", m.ImageFile);
                    if (b64 != null) parts.Add(InlinePart("image/jpeg", b64));
                }
                if (!string.IsNullOrEmpty(m.AudioFile))
                {
                    var b64 = await ImageHelper.ReadBase64Async("audio", m.AudioFile);
                    if (b64 != null) parts.Add(InlinePart("audio/wav", b64));
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
            return contents;
        }

        private static JsonObject BuildBody(JsonArray contents, string systemPrompt, double temperature, ToolOptions tools, bool allowCalls)
        {
            var body = new JsonObject();
            // Re-parse so each request owns its own copy (WinRT JSON values can't have two parents).
            body["contents"] = JsonArray.Parse(contents.Stringify());

            var sp = new JsonObject();
            sp["text"] = JsonValue.CreateStringValue(systemPrompt);
            var spParts = new JsonArray();
            spParts.Add(sp);
            var si = new JsonObject();
            si["parts"] = spParts;
            body["systemInstruction"] = si;

            var gen = new JsonObject();
            gen["temperature"] = JsonValue.CreateNumberValue(Math.Round(temperature, 2));
            body["generationConfig"] = gen;

            var decls = new JsonArray();
            if (tools.Wikipedia) decls.Add(WikipediaClient.Declaration());
            if (tools.Creative) foreach (var d in FileTools.Declarations()) decls.Add(d);
            if (decls.Count > 0)
            {
                var tool = new JsonObject();
                tool["functionDeclarations"] = decls;
                var arr = new JsonArray();
                arr.Add(tool);
                body["tools"] = arr;

                if (!allowCalls)
                {
                    var fcc = new JsonObject();
                    fcc["mode"] = JsonValue.CreateStringValue("NONE");
                    var tc = new JsonObject();
                    tc["functionCallingConfig"] = fcc;
                    body["toolConfig"] = tc;
                }
            }
            return body;
        }

        private static JsonObject InlinePart(string mime, string b64)
        {
            var inline = new JsonObject();
            inline["mimeType"] = JsonValue.CreateStringValue(mime);
            inline["data"] = JsonValue.CreateStringValue(b64);
            var part = new JsonObject();
            part["inlineData"] = inline;
            return part;
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

        /// <summary>Google's own wording, trimmed, so the user can tell e.g. a search quota from a model quota.</summary>
        private static string Detail(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("HTTP ")) return "";
            var t = raw.Replace("\n", " ").Trim();
            if (t.Length > 220) t = t.Substring(0, 220).TrimEnd() + "...";
            return "\n\nGoogle: " + t;
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
                        if (g.Message.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0) return S.ErrKey;
                        return S.ErrBadRequest + "\n" + g.Message;
                    case 401:
                    case 403: return S.ErrKey + "\n" + g.Message;
                    case 404: return S.ErrModel + Detail(g.Message);
                    case 429: return S.ErrQuota + Detail(g.Message);
                    case 500:
                    case 503: return S.ErrBusy + Detail(g.Message);
                    default: return "HTTP " + g.Status + ": " + g.Message;
                }
            }
            var hr = (uint)ex.HResult;
            // Certificate / TLS failures: old root store or missing cipher suites.
            if (hr == 0x80072F8F || hr == 0x80072F0D || hr == 0x80072F06 || hr == 0x80072F7D || hr == 0x80072F05)
                return S.ErrTls + string.Format(" (0x{0:X8})", hr);
            // No connection, DNS, timeout.
            if (hr == 0x80072EE7 || hr == 0x80072EFD || hr == 0x80072EE2 || hr == 0x80072EFE || hr == 0x80072F78)
                return S.ErrNetwork + string.Format(" (0x{0:X8})", hr);
            return S.ErrGeneric + "\n" + ex.Message + string.Format(" (0x{0:X8})", hr);
        }
    }
}
