using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Lumen.Core
{
    /// <summary>
    /// Records 16 kHz mono PCM WAV: universally supported by Gemini, no encoder needed on the phone,
    /// ~32 KB per second so a 1 minute note stays around 2 MB.
    /// </summary>
    public sealed class AudioRecorder
    {
        public const int MaxSeconds = 120;

        private MediaCapture _capture;
        private StorageFile _file;
        private readonly Stopwatch _watch = new Stopwatch();

        public bool IsRecording { get; private set; }
        public TimeSpan Elapsed { get { return _watch.Elapsed; } }

        public static async Task<StorageFolder> AudioFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("audio", CreationCollisionOption.OpenIfExists);
        }

        /// <summary>Throws UnauthorizedAccessException when the microphone is blocked.</summary>
        public async Task StartAsync()
        {
            if (IsRecording) return;
            _capture = new MediaCapture();
            try
            {
                await _capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio
                });

                var folder = await AudioFolderAsync();
                _file = await folder.CreateFileAsync(Guid.NewGuid().ToString("N") + ".wav", CreationCollisionOption.ReplaceExisting);

                var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Low);
                profile.Audio = AudioEncodingProperties.CreatePcm(16000, 1, 16);
                await _capture.StartRecordToStorageFileAsync(profile, _file);
            }
            catch
            {
                // Release the mic so the next attempt doesn't fail with "device in use".
                Cleanup();
                if (_file != null)
                {
                    try { await _file.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                    _file = null;
                }
                throw;
            }
            _watch.Restart();
            IsRecording = true;
        }

        /// <summary>Stops and returns (file name, seconds), or null when cancelled / too short.</summary>
        public async Task<Tuple<string, int>> StopAsync(bool keep)
        {
            if (!IsRecording) return null;
            IsRecording = false;
            _watch.Stop();
            try { await _capture.StopRecordAsync(); } catch (Exception) { }
            Cleanup();

            var seconds = (int)Math.Round(_watch.Elapsed.TotalSeconds);
            if (!keep || _watch.Elapsed.TotalSeconds < 0.8)
            {
                try { await _file.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch (Exception) { }
                return null;
            }
            return Tuple.Create(_file.Name, Math.Max(1, seconds));
        }

        private void Cleanup()
        {
            try { _capture?.Dispose(); } catch (Exception) { }
            _capture = null;
        }

        public static async Task DeleteAsync(IEnumerable<string> fileNames)
        {
            var folder = await AudioFolderAsync();
            foreach (var n in fileNames)
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
    }
}
