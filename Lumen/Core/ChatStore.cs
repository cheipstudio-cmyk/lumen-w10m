using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace Lumen.Core
{
    /// <summary>
    /// Plain JSON persistence in LocalFolder: index.json for the drawer, one file per conversation.
    /// Only the open conversation is ever kept in memory.
    /// </summary>
    public static class ChatStore
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        /// <summary>Bumped when all data is wiped, so cached pages know to reset.</summary>
        public static int Generation { get; private set; }

        private static async Task<StorageFolder> ChatsFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("chats", CreationCollisionOption.OpenIfExists);
        }

        public static async Task<List<ConversationInfo>> LoadIndexAsync()
        {
            var list = new List<ConversationInfo>();
            try
            {
                var folder = await ChatsFolderAsync();
                var item = await folder.TryGetItemAsync("index.json") as StorageFile;
                if (item == null) return list;
                var text = await FileIO.ReadTextAsync(item);
                JsonArray arr;
                if (!JsonArray.TryParse(text, out arr)) return list;
                foreach (var v in arr)
                {
                    if (v.ValueType != JsonValueType.Object) continue;
                    var o = v.GetObject();
                    list.Add(new ConversationInfo
                    {
                        Id = o.GetNamedString("id", ""),
                        Title = o.GetNamedString("title", ""),
                        Updated = FromUnix(o.GetNamedNumber("updated", 0)),
                        Preview = o.GetNamedString("preview", ""),
                        Pinned = o.GetNamedBoolean("pinned", false)
                    });
                }
            }
            catch (Exception) { }
            return list.Where(c => c.Id.Length > 0).OrderByDescending(c => c.Pinned).ThenByDescending(c => c.Updated).ToList();
        }

        public static async Task SaveIndexAsync(IEnumerable<ConversationInfo> items)
        {
            var arr = new JsonArray();
            foreach (var c in items)
            {
                var o = new JsonObject();
                o["id"] = JsonValue.CreateStringValue(c.Id);
                o["title"] = JsonValue.CreateStringValue(c.Title ?? "");
                o["updated"] = JsonValue.CreateNumberValue(c.Updated.ToUnixTimeMilliseconds());
                o["preview"] = JsonValue.CreateStringValue(c.Preview ?? "");
                o["pinned"] = JsonValue.CreateBooleanValue(c.Pinned);
                arr.Add(o);
            }
            await WriteAsync("index.json", arr.Stringify());
        }

        public static async Task<Conversation> LoadAsync(ConversationInfo info)
        {
            var conv = new Conversation { Info = info };
            try
            {
                var folder = await ChatsFolderAsync();
                var item = await folder.TryGetItemAsync(info.Id + ".json") as StorageFile;
                if (item == null) return conv;
                var text = await FileIO.ReadTextAsync(item);
                JsonArray arr;
                if (!JsonArray.TryParse(text, out arr)) return conv;
                foreach (var v in arr)
                {
                    if (v.ValueType != JsonValueType.Object) continue;
                    var o = v.GetObject();
                    var img = o.GetNamedString("image", "");
                    var audio = o.GetNamedString("audio", "");
                    conv.Messages.Add(new ChatMessage
                    {
                        Id = o.GetNamedString("id", Guid.NewGuid().ToString("N")),
                        Role = o.GetNamedString("role", ChatMessage.RoleUser),
                        Text = o.GetNamedString("text", ""),
                        ImageFile = img.Length > 0 ? img : null,
                        AudioFile = audio.Length > 0 ? audio : null,
                        AudioSeconds = (int)o.GetNamedNumber("audioSec", 0),
                        DocFile = Nullable(o.GetNamedString("doc", "")),
                        DocSize = (long)o.GetNamedNumber("docSize", 0),
                        Time = FromUnix(o.GetNamedNumber("time", 0))
                    });
                }
            }
            catch (Exception) { }
            return conv;
        }

        public static async Task SaveAsync(Conversation conv)
        {
            var arr = new JsonArray();
            foreach (var m in conv.Messages)
            {
                if (m.IsError || m.IsStreaming) continue;
                var o = new JsonObject();
                o["id"] = JsonValue.CreateStringValue(m.Id);
                o["role"] = JsonValue.CreateStringValue(m.Role);
                o["text"] = JsonValue.CreateStringValue(m.Text ?? "");
                if (!string.IsNullOrEmpty(m.ImageFile)) o["image"] = JsonValue.CreateStringValue(m.ImageFile);
                if (!string.IsNullOrEmpty(m.DocFile))
                {
                    o["doc"] = JsonValue.CreateStringValue(m.DocFile);
                    o["docSize"] = JsonValue.CreateNumberValue(m.DocSize);
                }
                if (!string.IsNullOrEmpty(m.AudioFile))
                {
                    o["audio"] = JsonValue.CreateStringValue(m.AudioFile);
                    o["audioSec"] = JsonValue.CreateNumberValue(m.AudioSeconds);
                }
                o["time"] = JsonValue.CreateNumberValue(m.Time.ToUnixTimeMilliseconds());
                arr.Add(o);
            }
            await WriteAsync(conv.Info.Id + ".json", arr.Stringify());
        }

        public static async Task DeleteAsync(ConversationInfo info)
        {
            var conv = await LoadAsync(info);
            await ImageHelper.DeleteAsync(conv.Messages.Select(m => m.ImageFile));
            await AudioRecorder.DeleteAsync(conv.Messages.Select(m => m.AudioFile));
            await FileTools.DeleteAsync(conv.Messages.Select(m => m.DocFile));
            try
            {
                var folder = await ChatsFolderAsync();
                var item = await folder.TryGetItemAsync(info.Id + ".json");
                if (item != null) await item.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch (Exception) { }
        }

        public static async Task DeleteAllAsync()
        {
            await Gate.WaitAsync();
            try
            {
                var local = ApplicationData.Current.LocalFolder;
                foreach (var name in new[] { "chats", "images", "audio", "files" })
                {
                    var f = await local.TryGetItemAsync(name);
                    if (f != null) await f.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
            }
            catch (Exception) { }
            finally { Generation++; Gate.Release(); }
        }

        private static async Task WriteAsync(string name, string content)
        {
            await Gate.WaitAsync();
            try
            {
                var folder = await ChatsFolderAsync();
                // Write to a temp file then swap, so a crash never leaves a half-written chat.
                var tmp = await folder.CreateFileAsync(name + ".tmp", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(tmp, content);
                await tmp.RenameAsync(name, NameCollisionOption.ReplaceExisting);
            }
            catch (Exception) { }
            finally { Gate.Release(); }
        }

        private static string Nullable(string s) { return string.IsNullOrEmpty(s) ? null : s; }

        private static DateTimeOffset FromUnix(double ms)
        {
            if (ms <= 0) return DateTimeOffset.Now;
            return DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
        }
    }
}
