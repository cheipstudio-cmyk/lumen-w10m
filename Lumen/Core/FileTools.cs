using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace Lumen.Core
{
    /// <summary>Something the model produced for the user: a generated image or a file.</summary>
    public sealed class Artifact
    {
        public bool IsImage { get; set; }
        public string FileName { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// "Creative" tools exposed to Gemini: generate_image (routed to an image-capable model)
    /// and create_file (text files the user can save, open or share). Works the same on free and paid keys:
    /// when the key has no quota for image models, the model gets a clear reason and tells the user.
    /// </summary>
    public static class FileTools
    {
        public const string ImageTool = "generate_image";
        public const string FileTool = "create_file";
        private const int MaxFileChars = 200000;

        public const string PromptHint =
            "When the user asks for a picture, drawing, logo, photo or any image, call generate_image with a detailed English prompt. " +
            "When the user asks for a document, code file, table, list or anything they will keep, call create_file with a sensible file name and the full content " +
            "(markdown .md, .txt, .csv, .html, .json or source code). After a tool succeeds, reply with one short sentence; the result is already shown to the user.";

        // ------------------------------------------------------------ declarations

        public static IEnumerable<JsonObject> Declarations()
        {
            yield return Decl(ImageTool, "Create an image from a text description and show it to the user.",
                Prop("prompt", "Detailed description of the image to create, in English."), new[] { "prompt" });

            var fileProps = Prop("filename", "File name with extension, e.g. 'shopping-list.md' or 'script.py'.");
            var content = new JsonObject();
            content["type"] = JsonValue.CreateStringValue("STRING");
            content["description"] = JsonValue.CreateStringValue("The complete file content.");
            fileProps["content"] = content;
            yield return Decl(FileTool, "Create a text file (document, code, table, list) and give it to the user to save or share.",
                fileProps, new[] { "filename", "content" });
        }

        private static JsonObject Prop(string name, string desc)
        {
            var p = new JsonObject();
            p["type"] = JsonValue.CreateStringValue("STRING");
            p["description"] = JsonValue.CreateStringValue(desc);
            var props = new JsonObject();
            props[name] = p;
            return props;
        }

        private static JsonObject Decl(string name, string desc, JsonObject props, string[] required)
        {
            var req = new JsonArray();
            foreach (var r in required) req.Add(JsonValue.CreateStringValue(r));
            var parameters = new JsonObject();
            parameters["type"] = JsonValue.CreateStringValue("OBJECT");
            parameters["properties"] = props;
            parameters["required"] = req;
            var d = new JsonObject();
            d["name"] = JsonValue.CreateStringValue(name);
            d["description"] = JsonValue.CreateStringValue(desc);
            d["parameters"] = parameters;
            return d;
        }

        // ------------------------------------------------------------ create_file

        public static async Task<Tuple<string, Artifact>> CreateFileAsync(JsonObject args)
        {
            var name = Sanitize(args.GetNamedString("filename", "lumen.txt"));
            var content = args.GetNamedString("content", "");
            if (content.Length > MaxFileChars) content = content.Substring(0, MaxFileChars);

            var folder = await FolderAsync();
            var stored = Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + name;
            var file = await folder.CreateFileAsync(stored, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, content, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            var size = Encoding.UTF8.GetByteCount(content);

            var art = new Artifact { IsImage = false, FileName = stored, Size = size };
            return Tuple.Create("File '" + name + "' created (" + size + " bytes) and shown to the user with Save, Open and Share buttons.", art);
        }

        private static string Sanitize(string name)
        {
            name = (name ?? "").Trim();
            var bad = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in name) sb.Append(bad.Contains(ch) || ch == '_' && sb.Length == 0 ? '-' : ch);
            var s = sb.ToString().Trim('.', ' ');
            if (s.Length == 0) s = "lumen.txt";
            if (s.Length > 80) s = s.Substring(s.Length - 80);
            if (s.IndexOf('.') < 0) s += ".txt";
            return s;
        }

        // ------------------------------------------------------------ generate_image

        public static async Task<Tuple<string, Artifact>> GenerateImageAsync(GeminiClient client, string apiKey, string baseUrl, JsonObject args, CancellationToken ct)
        {
            var prompt = args.GetNamedString("prompt", "").Trim();
            if (prompt.Length == 0) return Tuple.Create<string, Artifact>("Empty prompt.", null);

            var model = AppSettings.ImageModel;
            if (string.IsNullOrEmpty(model))
            {
                // Discover once: which image-capable models does this key see?
                try { await client.ListModelsAsync(apiKey, baseUrl, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { }
                model = AppSettings.ImageModel;
            }
            if (string.IsNullOrEmpty(model))
            {
                return Tuple.Create<string, Artifact>(
                    "Image generation failed: this API key has no image-capable Gemini model. Tell the user in one sentence that creating images needs a Gemini API key with access to an image model (usually a paid plan).", null);
            }

            try
            {
                var img = await client.GenerateImageAsync(apiKey, baseUrl, model, prompt, ct);
                if (img == null)
                {
                    return Tuple.Create<string, Artifact>("The image model answered without an image. Tell the user briefly and suggest rephrasing.", null);
                }
                var ext = img.Item1.Contains("jpeg") || img.Item1.Contains("jpg") ? ".jpg" : ".png";
                var folder = await ImageHelper.ImagesFolderAsync();
                var name = "gen_" + Guid.NewGuid().ToString("N") + ext;
                var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                var buf = CryptographicBuffer.DecodeFromBase64String(img.Item2);
                await FileIO.WriteBufferAsync(file, buf);
                return Tuple.Create("Image created with " + model + " and shown to the user under your message.",
                    new Artifact { IsImage = true, FileName = name, Size = buf.Length });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (GeminiException g)
            {
                var why = g.Status == 429 ? "no quota for image generation on this key (free plans usually have none)"
                        : g.Status == 403 || g.Status == 404 ? "this key can't use the image model " + model
                        : "HTTP " + g.Status + ": " + g.Message;
                return Tuple.Create<string, Artifact>("Image generation failed: " + why + ". Tell the user in one or two sentences, without technical details.", null);
            }
            catch (Exception ex)
            {
                return Tuple.Create<string, Artifact>("Image generation failed (" + ex.Message + "). Tell the user briefly.", null);
            }
        }

        // ------------------------------------------------------------ user actions

        public static async Task<StorageFolder> FolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("files", CreationCollisionOption.OpenIfExists);
        }

        public static async Task<StorageFile> GetDocAsync(string stored)
        {
            var folder = await FolderAsync();
            return await folder.GetFileAsync(stored);
        }

        public static async Task<StorageFile> GetImageAsync(string name)
        {
            var folder = await ImageHelper.ImagesFolderAsync();
            return await folder.GetFileAsync(name);
        }

        /// <summary>Copies a file to a location the user picks. Returns false if they cancel.</summary>
        public static async Task<bool> SaveAsAsync(StorageFile source, string suggestedName)
        {
            var ext = System.IO.Path.GetExtension(suggestedName);
            if (string.IsNullOrEmpty(ext)) ext = ".txt";
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = source.FileType == ".png" || source.FileType == ".jpg" ? PickerLocationId.PicturesLibrary : PickerLocationId.DocumentsLibrary,
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(suggestedName)
            };
            picker.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant() + " file", new List<string> { ext });
            var target = await picker.PickSaveFileAsync();
            if (target == null) return false;
            await source.CopyAndReplaceAsync(target);
            return true;
        }

        public static async Task OpenAsync(StorageFile file)
        {
            if (!await Launcher.LaunchFileAsync(file, new LauncherOptions { DisplayApplicationPicker = false }))
                throw new InvalidOperationException("No app handles this file type");
        }

        public static async Task DeleteAsync(IEnumerable<string> names)
        {
            var folder = await FolderAsync();
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                try
                {
                    var f = await folder.TryGetItemAsync(n);
                    if (f != null) await f.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch (Exception) { }
            }
        }

        /// <summary>Total bytes Lumen keeps on the phone (chats, photos, voice notes, files).</summary>
        public static async Task<ulong> UsageAsync()
        {
            ulong total = 0;
            var local = ApplicationData.Current.LocalFolder;
            foreach (var name in new[] { "chats", "images", "audio", "files" })
            {
                try
                {
                    var f = await local.TryGetItemAsync(name) as StorageFolder;
                    if (f == null) continue;
                    foreach (var file in await f.GetFilesAsync())
                    {
                        var props = await file.GetBasicPropertiesAsync();
                        total += props.Size;
                    }
                }
                catch (Exception) { }
            }
            return total;
        }
    }
}
