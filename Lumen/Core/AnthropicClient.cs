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
    /// <summary>Claude via the Anthropic Messages API: streaming, images, tool use.</summary>
    public static class AnthropicClient
    {
        private const string Version = "2023-06-01";
        private const int MaxToolRounds = 3;

        private static HttpRequestMessage Request(HttpMethod method, string apiKey, string url)
        {
            var req = new HttpRequestMessage(method, new Uri(url));
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", Version);
            return req;
        }

        private static string Root(string baseUrl)
        {
            var b = (baseUrl ?? "").TrimEnd('/');
            return b.EndsWith("/v1") ? b : b + "/v1";
        }

        // ---------------------------------------------------------------- models

        public static async Task<List<ModelInfo>> ListModelsAsync(string apiKey, string baseUrl, CancellationToken ct)
        {
            var result = new List<ModelInfo>();
            string after = null;
            for (int page = 0; page < 10; page++)
            {
                var url = Root(baseUrl) + "/models?limit=100" + (after != null ? "&after_id=" + Uri.EscapeDataString(after) : "");
                using (var resp = await Net.Http.SendRequestAsync(Request(HttpMethod.Get, apiKey, url), HttpCompletionOption.ResponseContentRead).AsTask(ct))
                {
                    var body = await resp.Content.ReadAsStringAsync().AsTask(ct);
                    if (!resp.IsSuccessStatusCode) throw Net.Error((int)resp.StatusCode, body);
                    JsonObject root;
                    if (!JsonObject.TryParse(body, out root)) break;
                    foreach (var v in root.GetNamedArray("data", new JsonArray()))
                    {
                        if (v.ValueType != JsonValueType.Object) continue;
                        var o = v.GetObject();
                        var id = o.GetNamedString("id", "");
                        if (id.Length == 0) continue;
                        result.Add(new ModelInfo { Id = id, DisplayName = o.GetNamedString("display_name", id) });
                    }
                    if (!root.GetNamedBoolean("has_more", false)) break;
                    after = root.GetNamedString("last_id", "");
                    if (after.Length == 0) break;
                }
            }
            // The API lists newest first; keep that, but put the fast, cheap Haiku on top as the default pick.
            return result.OrderByDescending(m => m.Id.Contains("haiku")).ToList();
        }

        // ---------------------------------------------------------------- chat

        public static async Task<StreamResult> StreamAsync(string apiKey, string baseUrl, string model, string systemPrompt,
            double temperature, ToolOptions tools, IList<ChatMessage> history,
            Action<string> onDelta, Action<string> onStatus, Action<Artifact> onArtifact, CancellationToken ct)
        {
            var messages = await BuildMessagesAsync(history);
            var system = ToolRunner.SystemPrompt(systemPrompt, tools);
            var decls = ToolRunner.Declarations(tools);
            // Text from a new tool round starts on its own paragraph.
            bool sep = false, any = false;
            var raw = onDelta;
            onDelta = t => { if (sep && any) raw("\n\n"); sep = false; any = true; raw(t); };
            var result = new StreamResult();
            bool sendTemperature = true, sendTools = decls.Count > 0;
            int maxTokens = 8192;

            for (int round = 0; round <= MaxToolRounds; round++)
            {
                var turn = new Turn();
                for (int attempt = 0; ; attempt++)
                {
                    var body = BuildBody(model, maxTokens, messages, system, temperature, sendTemperature, sendTools ? decls : null, round < MaxToolRounds);
                    try
                    {
                        turn = new Turn();
                        await StreamOnceAsync(apiKey, baseUrl, body, turn, onDelta, result, ct);
                        break;
                    }
                    catch (GeminiException g) when (g.Status == 400 && attempt < 3 && !turn.Started)
                    {
                        // Some models refuse a parameter: drop it and retry once each.
                        if (maxTokens > 4096 && g.Message.IndexOf("max_tokens", StringComparison.OrdinalIgnoreCase) >= 0) maxTokens = 4096;
                        else if (sendTemperature && g.Message.IndexOf("temperature", StringComparison.OrdinalIgnoreCase) >= 0) sendTemperature = false;
                        else if (sendTools && g.Message.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0) sendTools = false;
                        else throw;
                    }
                }

                if (turn.Calls.Count == 0 || round == MaxToolRounds) break;
                sep = true;

                // Assistant turn with its text and tool_use blocks, then the tool results.
                var content = new JsonArray();
                if (turn.Text.ToString().Trim().Length > 0) content.Add(TextBlock(turn.Text.ToString()));
                foreach (var c in turn.Calls)
                {
                    var b = new JsonObject();
                    b["type"] = JsonValue.CreateStringValue("tool_use");
                    b["id"] = JsonValue.CreateStringValue(c.Id);
                    b["name"] = JsonValue.CreateStringValue(c.Name);
                    b["input"] = ToolRunner.ParseArgs(c.Json.ToString());
                    content.Add(b);
                }
                messages.Add(Message("assistant", content));

                var results = new JsonArray();
                foreach (var c in turn.Calls)
                {
                    var output = await ToolRunner.RunAsync(c.Name, ToolRunner.ParseArgs(c.Json.ToString()), onStatus, onArtifact, ct);
                    var r = new JsonObject();
                    r["type"] = JsonValue.CreateStringValue("tool_result");
                    r["tool_use_id"] = JsonValue.CreateStringValue(c.Id);
                    r["content"] = JsonValue.CreateStringValue(output ?? "");
                    results.Add(r);
                }
                messages.Add(Message("user", results));
                onStatus?.Invoke(null);
            }
            return result;
        }

        private sealed class Call
        {
            public string Id;
            public string Name;
            public readonly StringBuilder Json = new StringBuilder();
        }

        private sealed class Turn
        {
            public bool Started;
            public readonly StringBuilder Text = new StringBuilder();
            public readonly List<Call> Calls = new List<Call>();
            public readonly Dictionary<int, Call> ByIndex = new Dictionary<int, Call>();
        }

        private static async Task StreamOnceAsync(string apiKey, string baseUrl, JsonObject body, Turn turn,
            Action<string> onDelta, StreamResult result, CancellationToken ct)
        {
            var req = Request(HttpMethod.Post, apiKey, Root(baseUrl) + "/messages");
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
            JsonObject e;
            if (!JsonObject.TryParse(line.Substring(5).Trim(), out e)) return;
            turn.Started = true;
            switch (e.GetNamedString("type", ""))
            {
                case "content_block_start":
                {
                    var block = e.GetNamedObject("content_block", new JsonObject());
                    if (block.GetNamedString("type", "") == "tool_use")
                    {
                        var call = new Call { Id = block.GetNamedString("id", ""), Name = block.GetNamedString("name", "") };
                        turn.Calls.Add(call);
                        turn.ByIndex[(int)e.GetNamedNumber("index", 0)] = call;
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var d = e.GetNamedObject("delta", new JsonObject());
                    var kind = d.GetNamedString("type", "");
                    if (kind == "text_delta")
                    {
                        var t = d.GetNamedString("text", "");
                        if (t.Length > 0) { turn.Text.Append(t); onDelta(t); }
                    }
                    else if (kind == "input_json_delta")
                    {
                        Call call;
                        if (turn.ByIndex.TryGetValue((int)e.GetNamedNumber("index", 0), out call)) call.Json.Append(d.GetNamedString("partial_json", ""));
                    }
                    break;
                }
                case "message_delta":
                {
                    var reason = e.GetNamedObject("delta", new JsonObject()).GetNamedString("stop_reason", "");
                    if (reason == "max_tokens") result.FinishReason = "MAX_TOKENS";
                    else if (reason == "refusal") result.BlockReason = "SAFETY";
                    else if (reason.Length > 0) result.FinishReason = "STOP";
                    break;
                }
                case "error":
                {
                    var err = e.GetNamedObject("error", new JsonObject());
                    var type = err.GetNamedString("type", "");
                    var status = type == "overloaded_error" ? 503 : type == "rate_limit_error" ? 429 : 500;
                    throw new GeminiException(status, err.GetNamedString("message", "Stream error"));
                }
            }
        }

        private static JsonObject BuildBody(string model, int maxTokens, JsonArray messages, string system, double temperature, bool sendTemperature,
            List<JsonObject> decls, bool allowCalls)
        {
            var body = new JsonObject();
            body["model"] = JsonValue.CreateStringValue(model);
            body["max_tokens"] = JsonValue.CreateNumberValue(maxTokens);
            body["stream"] = JsonValue.CreateBooleanValue(true);
            body["system"] = JsonValue.CreateStringValue(system);
            if (sendTemperature) body["temperature"] = JsonValue.CreateNumberValue(Math.Round(Math.Min(1.0, temperature), 2));
            body["messages"] = JsonArray.Parse(messages.Stringify());

            if (decls != null && decls.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var d in decls)
                {
                    var t = new JsonObject();
                    t["name"] = JsonValue.CreateStringValue(d.GetNamedString("name", ""));
                    t["description"] = JsonValue.CreateStringValue(d.GetNamedString("description", ""));
                    t["input_schema"] = Net.LowerSchema(d.GetNamedObject("parameters", new JsonObject()));
                    arr.Add(t);
                }
                body["tools"] = arr;
                if (!allowCalls)
                {
                    var choice = new JsonObject();
                    choice["type"] = JsonValue.CreateStringValue("none");
                    body["tool_choice"] = choice;
                }
            }
            return body;
        }

        private static JsonObject TextBlock(string text)
        {
            var b = new JsonObject();
            b["type"] = JsonValue.CreateStringValue("text");
            b["text"] = JsonValue.CreateStringValue(text);
            return b;
        }

        private static JsonObject Message(string role, JsonArray content)
        {
            var m = new JsonObject();
            m["role"] = JsonValue.CreateStringValue(role);
            m["content"] = content;
            return m;
        }

        /// <summary>Claude wants strictly alternating turns starting with the user: merge neighbours with the same role.</summary>
        private static async Task<JsonArray> BuildMessagesAsync(IList<ChatMessage> history)
        {
            var turns = new List<KeyValuePair<string, List<JsonObject>>>();
            foreach (var m in history)
            {
                if (m.IsError) continue;
                var role = m.IsUser ? "user" : "assistant";
                var blocks = new List<JsonObject>();
                if (m.IsUser && !string.IsNullOrEmpty(m.ImageFile))
                {
                    var b64 = await ImageHelper.ReadBase64Async("images", m.ImageFile);
                    if (b64 != null)
                    {
                        var src = new JsonObject();
                        src["type"] = JsonValue.CreateStringValue("base64");
                        src["media_type"] = JsonValue.CreateStringValue("image/jpeg");
                        src["data"] = JsonValue.CreateStringValue(b64);
                        var img = new JsonObject();
                        img["type"] = JsonValue.CreateStringValue("image");
                        img["source"] = src;
                        blocks.Add(img);
                    }
                }
                var text = MessageText(m);
                if (text.Length > 0) blocks.Add(TextBlock(text));
                if (blocks.Count == 0) continue;
                if (turns.Count == 0 && role == "assistant") continue; // must start with the user
                if (turns.Count > 0 && turns[turns.Count - 1].Key == role) turns[turns.Count - 1].Value.AddRange(blocks);
                else turns.Add(new KeyValuePair<string, List<JsonObject>>(role, blocks));
            }

            var messages = new JsonArray();
            foreach (var t in turns)
            {
                var content = new JsonArray();
                foreach (var b in t.Value) content.Add(b);
                messages.Add(Message(t.Key, content));
            }
            return messages;
        }

        /// <summary>Text sent for a message; voice notes travel as their transcript.</summary>
        internal static string MessageText(ChatMessage m)
        {
            var t = (m.Text ?? "").Trim();
            if (t.Length == 0 && !string.IsNullOrEmpty(m.AudioFile)) t = "(voice message without transcript)";
            if (!m.IsUser && !string.IsNullOrEmpty(m.ImageFile) && t.Length == 0) t = "(image shown to the user)";
            return t;
        }
    }
}
