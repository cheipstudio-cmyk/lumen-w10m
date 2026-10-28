using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;

namespace Lumen.Core
{
    /// <summary>"Read aloud" with the voices installed on the phone (Settings > Time &amp; language > Speech).</summary>
    public static class Speech
    {
        public static VoiceInformation[] Voices
        {
            get
            {
                try { return SpeechSynthesizer.AllVoices.ToArray(); }
                catch (Exception) { return new VoiceInformation[0]; }
            }
        }

        public static async Task<MediaSource> SynthesizeAsync(string markdown)
        {
            using (var synth = new SpeechSynthesizer())
            {
                var id = AppSettings.VoiceId;
                if (!string.IsNullOrEmpty(id))
                {
                    var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.Id == id);
                    if (v != null) synth.Voice = v;
                }
                var stream = await synth.SynthesizeTextToStreamAsync(Plain(markdown));
                return MediaSource.CreateFromStream(stream, stream.ContentType);
            }
        }

        /// <summary>Strip markdown so the voice doesn't read asterisks and hashes.</summary>
        private static string Plain(string md)
        {
            var sb = new StringBuilder();
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("```")) continue;
                line = line.TrimStart('#', '>', ' ');
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ")) line = line.Substring(2);
                line = line.Replace("**", "").Replace("__", "").Replace("`", "").Replace("*", "");
                if (line.Length > 0) sb.Append(line).Append(".\n");
            }
            var s = sb.ToString();
            return s.Length > 4000 ? s.Substring(0, 4000) : s;
        }
    }
}
