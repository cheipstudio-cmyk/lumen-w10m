using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Web.Http;

namespace Lumen.Core
{
    /// <summary>
    /// OpenAI Chat Completions, also used for any compatible server (OpenRouter, Groq, Mistral, DeepSeek, LM Studio...).
    /// </summary>
    public static class OpenAIClient
    {
        private const int MaxToolRounds = 3;

        private static HttpRequestMessage Request(HttpMethod method, string apiKey, string url)
        {
            var req = new HttpRequestMessage(method, new Uri(url));
            if (!string.IsNullOrEmpty(apiKey)) req.Headers.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", apiKey);
            return req;
        }

        private static string Root(string baseUrl)
        {
            var b = (baseUrl ?? "").Trim().TrimEnd('/');
            if (b.EndsWith("/chat/completions")) b = b.Substring(0, b.Length - "/chat/completions".Length);
            return b;
        }

        // ---------------------------------------------------------------- models

        public static async Task<List<ModelInfo>> ListModelsAsync(string provider, string apiKey, string baseUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new GeminiException(400, S.ErrNoEndpoint);
            var result = new List<ModelInfo>();
            var images = new List<string>();
            using (var resp = await Net.Http.SendRequestAsync(Request(HttpMethod.Get, apiKey, Root(baseUrl) + "/models"), HttpCompletionOption.ResponseContentRead).AsTask(ct))
            {
                var body = await resp.Content.ReadAsStringAsync().AsTask(ct);
                if (!resp.IsSuccessStatusCode) throw Net.Error((int)resp.StatusCode, body);
                JsonObject root;
                if (JsonObject.TryParse(body, out root))
                {
                    foreach (var v in root.GetNamedArray("data", new JsonArray()))
                    {
                        if (v.ValueType != JsonValueType.Object) continue;
                        var o = v.GetObject();
                        var id = o.GetNamedString("id", "");
                        if (id.Length == 0) continue;
                        if (provider == "openai")
                        {
                            if (id.StartsWith("gpt-image")) { images.Add(id); continue; }
                            if (!IsOpenAIChatModel(id)) continue;
                        }
                        result.Add(new ModelInfo { Id = id, DisplayName = o.GetNamedString("name", Pretty(id)) });
                    }
                }
            }

            if (provider == "openai")
            {
                // Newest image model, plain names before snapshots.
                AppSettings.SetImageModelFor("openai", images
                    .OrderBy(i => System.Text.RegularExpressions.Regex.IsMatch(i, @"\d{4}-\d{2}-\d{2}$"))
                    .ThenByDescending(i => i, StringComparer.Ordinal)
                    .FirstOrDefault() ?? "");
                // Cheapest current tier first (luna/mini/nano are the fast ones), then newest.
                return result
                    .OrderByDescending(m => m.Id.Contains("luna") || m.Id.EndsWith("-mini"))
                    .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                    .ToList();
            }
            return result.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsOpenAIChatModel(string id)
        {
            if (!(id.StartsWith("gpt-") || id.StartsWith("chatgpt-") || System.Text.RegularExpressions.Regex.IsMatch(id, @"^o\d"))) return false;
            string[] skip = { "audio", "realtime", "tts", "transcribe", "search", "embedding", "instruct", "codex", "cyber", "rosalind", "moderation", "dall-e", "whisper", "-pro" };
            foreach (var s in skip) if (id.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return !System.Text.RegularExpressions.Regex.IsMatch(id, @"\d{4}-\d{2}-\d{2}$"); // hide dated snapshots
        }

        private static string Pretty(string id)
        {
            if (!id.StartsWith("gpt-")) return id;
            var parts = id.Substring(4).Split('-');
            var sb = new StringBuilder("GPT-" + parts[0]);
            for (int i = 1; i < parts.Length; i++)
                sb.Append(' ').Append(parts[i].Length > 0 ? char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1) : "");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- images

        /// <summary>Images API. Returns (mimeType, base64) or null.</summary>
        public static async Task<Tuple<string, string>> GenerateImageAsync(string apiKey, string baseUrl, string model, string prompt, CancellationToken ct)
        {
            var body = new JsonObject();
            body["model"] = JsonValue.CreateStringValue(model);
            body["prompt"] = JsonValue.CreateStringValue(prompt);
            body["n"] = JsonValue.CreateNumberValue(1);
            body["size"] = JsonValue.CreateStringValue("1024x1024");
            var req = Request(HttpMethod.Post, apiKey, Root(baseUrl) + "/images/generations");
            req.Content = Net.Json(body);
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(150));
                using (var resp = await Net.Http.SendRequestAsync(req, HttpCompletionOption.ResponseContentRead).AsTask(cts.Token))
                {
                    var raw = await resp.Content.ReadAsStringAsync().AsTask(cts.Token);
                    if (!resp.IsSuccessStatusCode) throw Net.Error((int)resp.StatusCode, raw);
                    JsonObject root;
                    if (!JsonObject.TryParse(raw, out root)) return null;
                    foreach (var d in root.GetNamedArray("data", new JsonArray()))
                    {
                        if (d.ValueType != JsonValueType.Object) continue;
                        var b64 = d.GetObject().GetNamedString("b64_json", "");
                        if (b64.Length > 0) return Tuple.Create("image/" + root.GetNamedString("output_format", "png"), b64);
                    }
                    return null;
                }
            }
        }

        // ---------------------------------------------------------------- chat

        public static async Task<StreamResult> StreamAsync(string provider, string apiKey, string baseUrl, string model, string systemPrompt,
            double temperature, ToolOptions tools, IList<ChatMessage> history,
            Action<string> onDelta, Action<string> onStatus, Action<Artifact> onArtifact, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new GeminiException(400, S.ErrNoEndpoint);
            var messages = await BuildMessagesAsync(ToolRunner.SystemPrompt(systemPrompt, tools), history);
            var decls = ToolRunner.Declarations(tools);
            // Text from a new tool round starts on its own paragraph.
            bool sep = false, any = false;
            var raw = onDelta;
            onDelta = t => { if (sep && any) raw("\n\n"); sep = false; any = true; raw(t); };
            var result = new StreamResult();
            bool sendTemperature = true, sendTools = decls.Count > 0;

            for (int round = 0; round <= MaxToolRounds; round++)
            {
                var turn = new Turn();
                for (int attempt = 0; ; attempt++)
                {
                    var body = BuildBody(model, messages, temperature, sendTemperature, sendTools ? decls : null, round < MaxToolRounds);
                    try
                    {
                        turn = new Turn();
                        await StreamOnceAsync(apiKey, baseUrl, body, turn, onDelta, result, ct);
                        break;
                    }
                    catch (GeminiException g) when ((g.Status == 400 || g.Status == 422) && attempt < 2 && !turn.Started)
                    {
                        // Reasoning models reject temperature; some servers or models reject tools.
                        if (sendTemperature && g.Message.IndexOf("temperature", StringComparison.OrdinalIgnoreCase) >= 0) sendTemperature = false;
                        else if (sendTools && (g.Message.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               g.Message.IndexOf("function", StringComparison.OrdinalIgnoreCase) >= 0)) sendTools = false;
                        else throw;
                    }
                }

                var calls = turn.Calls.Values.Where(c => c.Name.Length > 0).ToList();
                if (calls.Count == 0 || round == MaxToolRounds) break;
                sep = true;

                var toolCalls = new JsonArray();
                foreach (var c in calls)
                {
                    var fn = new JsonObject();
                    fn["name"] = JsonValue.CreateStringValue(c.Name);
                    fn["arguments"] = JsonValue.CreateStringValue(c.Args.Length > 0 ? c.Args.ToString() : "{}");
                    var tc = new JsonObject();
                    tc["id"] = JsonValue.CreateStringValue(c.Id);
                    tc["type"] = JsonValue.CreateStringValue("function");
                    tc["function"] = fn;
                    toolCalls.Add(tc);
                }
                var assistant = new JsonObject();
                assistant["role"] = JsonValue.CreateStringValue("assistant");
                assistant["content"] = turn.Text.Length > 0 ? (IJsonValue)JsonValue.CreateStringValue(turn.Text.ToString()) : JsonValue.CreateNullValue();
                assistant["tool_calls"] = toolCalls;
                messages.Add(assistant);

                foreach (var c in calls)
                {
                    var output = await ToolRunner.RunAsync(c.Name, ToolRunner.ParseArgs(c.Args.ToString()), onStatus, onArtifact, ct);
                    var tool = new JsonObject();
                    tool["role"] = JsonValue.CreateStringValue("tool");
                    tool["tool_call_id"] = JsonValue.CreateStringValue(c.Id);
                    tool["content"] = JsonValue.CreateStringValue(output ?? "");
                    messages.Add(tool);
                }
                onStatus?.Invoke(null);
            }
            return result;
        }

        private sealed class Call
        {
            public string Id = "";
            public string Name = "";
            public readonly StringBuilder Args = new StringBuilder();
        }

        private sealed class Turn
        {
            public bool Started;
            public readonly StringBuilder Text = new StringBuilder();
            public readonly SortedDictionary<int, Call> Calls = new SortedDictionary<int, Call>();
        }

        private static async Task StreamOnceAsync(string apiKey, string baseUrl, JsonObject body, Turn turn,
            Action<string> onDelta, StreamResult result, CancellationToken ct)
        {
            var req = Request(HttpMethod.Post, apiKey, Root(baseUrl) + "/chat/completions");
            req.Content = Net.Json(body);
            using (var resp = await Net.Http.SendRequestAsync(req, HttpCompletionOption.ResponseHeadersRead).AsTask(ct))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync().AsTask(ct);
                    throw Net.Error((int)resp.StatusCode, err);
                }
                await Net.ReadLinesAsync(resp, line => HandleLine(line, turn, onDelta, result), ct);
            }
        }

        private static void HandleLine(string line, Turn turn, Action<string> onDelta, StreamResult result)
        {
            if (!line.StartsWith("data:")) return;
            var json = line.Substring(5).Trim();
            if (json.Length == 0 || json == "[DONE]") return;
            JsonObject chunk;
            if (!JsonObject.TryParse(json, out chunk)) return;
            turn.Started = true;

            var error = chunk.GetNamedValue("error", null);
            if (error != null && error.ValueType == JsonValueType.Object)
            {
                var eo = error.GetObject();
                var code = eo.GetNamedValue("code", null);
                var msg = eo.GetNamedValue("message", null);
                throw new GeminiException(code != null && code.ValueType == JsonValueType.Number ? (int)code.GetNumber() : 500,
                    msg != null && msg.ValueType == JsonValueType.String ? msg.GetString() : "Stream error");
            }

            var choices = chunk.GetNamedArray("choices", null);
            if (choices == null || choices.Count == 0 || choices[0].ValueType != JsonValueType.Object) return;
            var choice = choices[0].GetObject();

            var finish = choice.GetNamedValue("finish_reason", null);
            if (finish != null && finish.ValueType == JsonValueType.String)
            {
                var f = finish.GetString();
                result.FinishReason = f == "length" ? "MAX_TOKENS" : "STOP";
                if (f == "content_filter") result.BlockReason = "SAFETY";
            }

            var delta = choice.GetNamedObject("delta", null);
            if (delta == null) return;
            var content = delta.GetNamedValue("content", null);
            if (content != null && content.ValueType == JsonValueType.String)
            {
                var t = content.GetString();
                if (t.Length > 0) { turn.Text.Append(t); onDelta(t); }
            }

            var calls = delta.GetNamedValue("tool_calls", null);
            if (calls == null || calls.ValueType != JsonValueType.Array) return;
            foreach (var v in calls.GetArray())
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var o = v.GetObject();
                var index = (int)o.GetNamedNumber("index", turn.Calls.Count);
                Call call;
                if (!turn.Calls.TryGetValue(index, out call)) { call = new Call(); turn.Calls[index] = call; }
                var id = o.GetNamedValue("id", null);
                if (id != null && id.ValueType == JsonValueType.String && id.GetString().Length > 0) call.Id = id.GetString();
                var fn = o.GetNamedObject("function", null);
                if (fn == null) continue;
                var name = fn.GetNamedValue("name", null);
                if (name != null && name.ValueType == JsonValueType.String && name.GetString().Length > 0) call.Name = name.GetString();
                var args = fn.GetNamedValue("arguments", null);
                if (args != null && args.ValueType == JsonValueType.String) call.Args.Append(args.GetString());
            }
            foreach (var c in turn.Calls) if (c.Value.Id.Length == 0) c.Value.Id = "call_" + c.Key;
        }

        private static JsonObject BuildBody(string model, JsonArray messages, double temperature, bool sendTemperature,
            List<JsonObject> decls, bool allowCalls)
        {
            var body = new JsonObject();
            body["model"] = JsonValue.CreateStringValue(model);
            body["stream"] = JsonValue.CreateBooleanValue(true);
            if (sendTemperature) body["temperature"] = JsonValue.CreateNumberValue(Math.Round(temperature, 2));
            body["messages"] = JsonArray.Parse(messages.Stringify());
            if (decls != null && decls.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var d in decls)
                {
                    var fn = new JsonObject();
                    fn["name"] = JsonValue.CreateStringValue(d.GetNamedString("name", ""));
                    fn["description"] = JsonValue.CreateStringValue(d.GetNamedString("description", ""));
                    fn["parameters"] = Net.LowerSchema(d.GetNamedObject("parameters", new JsonObject()));
                    var t = new JsonObject();
                    t["type"] = JsonValue.CreateStringValue("function");
                    t["function"] = fn;
                    arr.Add(t);
                }
                body["tools"] = arr;
                if (!allowCalls) body["tool_choice"] = JsonValue.CreateStringValue("none");
            }
            return body;
        }

        private static async Task<JsonArray> BuildMessagesAsync(string system, IList<ChatMessage> history)
        {
            var messages = new JsonArray();
            var sys = new JsonObject();
            sys["role"] = JsonValue.CreateStringValue("system");
            sys["content"] = JsonValue.CreateStringValue(system);
            messages.Add(sys);

            foreach (var m in history)
            {
                if (m.IsError) continue;
                var text = AnthropicClient.MessageText(m);
                var msg = new JsonObject();
                msg["role"] = JsonValue.CreateStringValue(m.IsUser ? "user" : "assistant");

                string b64 = null;
                if (m.IsUser && !string.IsNullOrEmpty(m.ImageFile)) b64 = await ImageHelper.ReadBase64Async("images", m.ImageFile);
                if (b64 != null)
                {
                    var parts = new JsonArray();
                    var url = new JsonObject();
                    url["url"] = JsonValue.CreateStringValue("data:image/jpeg;base64," + b64);
                    var img = new JsonObject();
                    img["type"] = JsonValue.CreateStringValue("image_url");
                    img["image_url"] = url;
                    parts.Add(img);
                    if (text.Length > 0)
                    {
                        var tp = new JsonObject();
                        tp["type"] = JsonValue.CreateStringValue("text");
                        tp["text"] = JsonValue.CreateStringValue(text);
                        parts.Add(tp);
                    }
                    msg["content"] = parts;
                }
                else
                {
                    if (text.Length == 0) continue;
                    msg["content"] = JsonValue.CreateStringValue(text);
                }
                messages.Add(msg);
            }
            return messages;
        }
    }
}
