using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace Lumen.Core
{
    /// <summary>
    /// Free web knowledge for the model: Wikipedia's public API, exposed to Gemini as a function.
    /// No key, no quota on our side. Used because Google Search grounding is not in the free tier.
    /// </summary>
    public static class WikipediaClient
    {
        public const string ToolName = "wikipedia_search";

        public const string PromptHint =
            "You can call wikipedia_search to look things up on Wikipedia. Use it for products, people, places, companies, " +
            "events or anything that may have changed after your training data, and whenever you are not sure. " +
            "Use lang matching the user's language (for example 'it' for Italian), the tool falls back to English by itself. " +
            "When you use it, mention the Wikipedia page you relied on. Don't use it for chit-chat, coding, maths or creative writing.";

        private static readonly Regex LangRx = new Regex("^[a-z]{2,3}$");
        private static HttpClient _http;

        private static HttpClient Http
        {
            get
            {
                if (_http == null)
                {
                    var filter = new HttpBaseProtocolFilter { AllowUI = false };
                    _http = new HttpClient(filter);
                    // Wikimedia asks every API client for a descriptive User-Agent.
                    _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Lumen/" + S.AppVersion + " (Windows 10 Mobile; https://github.com/cheipstudio-cmyk/lumen-w10m)");
                }
                return _http;
            }
        }

        public static JsonObject Declaration()
        {
            var query = new JsonObject();
            query["type"] = JsonValue.CreateStringValue("STRING");
            query["description"] = JsonValue.CreateStringValue("What to look up, as a short search query (e.g. 'Moto G57').");

            var lang = new JsonObject();
            lang["type"] = JsonValue.CreateStringValue("STRING");
            lang["description"] = JsonValue.CreateStringValue("Two-letter Wikipedia language code, e.g. 'it' or 'en'.");

            var props = new JsonObject();
            props["query"] = query;
            props["lang"] = lang;

            var required = new JsonArray();
            required.Add(JsonValue.CreateStringValue("query"));

            var parameters = new JsonObject();
            parameters["type"] = JsonValue.CreateStringValue("OBJECT");
            parameters["properties"] = props;
            parameters["required"] = required;

            var decl = new JsonObject();
            decl["name"] = JsonValue.CreateStringValue(ToolName);
            decl["description"] = JsonValue.CreateStringValue("Search Wikipedia and return the beginning of the best matching articles, with their URLs.");
            decl["parameters"] = parameters;
            return decl;
        }

        /// <summary>Never throws (except on cancellation): failures become a short text the model can read.</summary>
        public static async Task<string> SearchAsync(string query, string lang, CancellationToken ct)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0) return "Empty query.";
            lang = (lang ?? "en").Trim().ToLowerInvariant();
            if (!LangRx.IsMatch(lang)) lang = "en";

            try
            {
                var text = await SearchLangAsync(query, lang, ct);
                if (text == null && lang != "en") text = await SearchLangAsync(query, "en", ct);
                return text ?? ("No Wikipedia results for \"" + query + "\".");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return "Wikipedia lookup failed (" + ex.Message + "). Answer from your own knowledge and say it may be outdated.";
            }
        }

        private static async Task<string> SearchLangAsync(string query, string lang, CancellationToken ct)
        {
            var api = "https://" + lang + ".wikipedia.org/w/api.php";

            var searchUrl = api + "?action=query&list=search&srlimit=3&format=json&formatversion=2&utf8=1&srsearch=" + Uri.EscapeDataString(query);
            var root = await GetJsonAsync(searchUrl, ct);
            var hits = root?.GetNamedObject("query", new JsonObject()).GetNamedArray("search", new JsonArray());
            if (hits == null || hits.Count == 0) return null;

            var titles = hits.Where(h => h.ValueType == JsonValueType.Object)
                             .Select(h => h.GetObject().GetNamedString("title", ""))
                             .Where(t => t.Length > 0).Take(3).ToList();
            if (titles.Count == 0) return null;

            var extractUrl = api + "?action=query&prop=extracts&exintro=1&explaintext=1&exchars=1800&exlimit=3&format=json&formatversion=2&redirects=1&titles=" +
                             Uri.EscapeDataString(string.Join("|", titles));
            var ex = await GetJsonAsync(extractUrl, ct);
            var pages = ex?.GetNamedObject("query", new JsonObject()).GetNamedArray("pages", new JsonArray());
            if (pages == null) return null;

            var byTitle = new Dictionary<string, string>();
            foreach (var p in pages)
            {
                if (p.ValueType != JsonValueType.Object) continue;
                var o = p.GetObject();
                byTitle[o.GetNamedString("title", "")] = o.GetNamedString("extract", "");
            }

            var sb = new StringBuilder();
            sb.Append("Wikipedia (").Append(lang).Append(") results for \"").Append(query).Append("\":\n");
            foreach (var t in titles)
            {
                string extract;
                if (!byTitle.TryGetValue(t, out extract) || string.IsNullOrWhiteSpace(extract)) continue;
                sb.Append("\n## ").Append(t).Append('\n');
                sb.Append("URL: https://").Append(lang).Append(".wikipedia.org/wiki/").Append(Uri.EscapeDataString(t.Replace(' ', '_'))).Append('\n');
                sb.Append(extract.Trim()).Append('\n');
            }
            return sb.ToString();
        }

        private static async Task<JsonObject> GetJsonAsync(string url, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(12));
                var body = await Http.GetStringAsync(new Uri(url)).AsTask(cts.Token);
                JsonObject root;
                return JsonObject.TryParse(body, out root) ? root : null;
            }
        }
    }
}
