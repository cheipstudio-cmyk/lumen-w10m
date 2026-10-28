using Windows.ApplicationModel.Resources;

namespace Lumen.Core
{
    /// <summary>User-facing runtime strings, from Strings/*/Resources.resw (generated).</summary>
    public static class S
    {
        public const string AppVersion = "1.3";

        /// <summary>Model-facing, kept in English on purpose: the model replies in the user's language anyway.</summary>
        public const string DefaultSystemPrompt =
            "You are Lumen, a helpful assistant running on a small phone screen. " +
            "Answer clearly and concisely, use short paragraphs and lists, avoid wide tables. " +
            "Reply in the user's language.";

        private static ResourceLoader _loader;

        /// <summary>Call after changing the app language.</summary>
        public static void Reset() { _loader = null; }

        public static string Get(string key)
        {
            try
            {
                if (_loader == null) _loader = ResourceLoader.GetForViewIndependentUse();
                var s = _loader.GetString(key);
                return string.IsNullOrEmpty(s) ? key : s;
            }
            catch { return key; }
        }

        public static string LabelSend { get { return Get("LabelSend"); } }
        public static string LabelVoice { get { return Get("LabelVoice"); } }
        public static string LabelStop { get { return Get("LabelStop"); } }
        public static string PhoneAccent { get { return Get("PhoneAccent"); } }
        public static string GreetMorning { get { return Get("GreetMorning"); } }
        public static string GreetAfternoon { get { return Get("GreetAfternoon"); } }
        public static string GreetEvening { get { return Get("GreetEvening"); } }
        public static string Suggest1 { get { return Get("Suggest1"); } }
        public static string Suggest2 { get { return Get("Suggest2"); } }
        public static string Suggest3 { get { return Get("Suggest3"); } }
        public static string Suggest4 { get { return Get("Suggest4"); } }
        public static string NewChatTitle { get { return Get("NewChatTitle"); } }
        public static string ImageOnly { get { return Get("ImageOnly"); } }
        public static string VoiceOnly { get { return Get("VoiceOnly"); } }
        public static string Copied { get { return Get("Copied"); } }
        public static string Stopped { get { return Get("Stopped"); } }
        public static string Truncated { get { return Get("Truncated"); } }
        public static string ImageError { get { return Get("ImageError"); } }
        public static string CameraError { get { return Get("CameraError"); } }
        public static string MicDenied { get { return Get("MicDenied"); } }
        public static string MicError { get { return Get("MicError"); } }
        public static string TooShort { get { return Get("TooShort"); } }
        public static string CreatingImage { get { return Get("CreatingImage"); } }
        public static string CreatingFile { get { return Get("CreatingFile"); } }
        public static string LookingUp { get { return Get("LookingUp"); } }
        public static string Verifying { get { return Get("Verifying"); } }
        public static string ModelsLoaded { get { return Get("ModelsLoaded"); } }
        public static string KeyMissing { get { return Get("KeyMissing"); } }
        public static string KeyAlsoImage { get { return Get("KeyAlsoImage"); } }
        public static string ErrKey { get { return Get("ErrKey"); } }
        public static string ErrBadRequest { get { return Get("ErrBadRequest"); } }
        public static string ErrModel { get { return Get("ErrModel"); } }
        public static string ErrQuota { get { return Get("ErrQuota"); } }
        public static string ErrBusy { get { return Get("ErrBusy"); } }
        public static string ErrTls { get { return Get("ErrTls"); } }
        public static string ErrNetwork { get { return Get("ErrNetwork"); } }
        public static string ErrGeneric { get { return Get("ErrGeneric"); } }
        public static string ErrBlocked { get { return Get("ErrBlocked"); } }
        public static string ErrEmpty { get { return Get("ErrEmpty"); } }
        public static string DeleteAllTitle { get { return Get("DeleteAllTitle"); } }
        public static string DeleteAllBody { get { return Get("DeleteAllBody"); } }
        public static string Delete { get { return Get("Delete"); } }
        public static string Cancel { get { return Get("Cancel"); } }
        public static string Copy { get { return Get("Copy"); } }
        public static string EditResend { get { return Get("EditResend"); } }
        public static string SelectText { get { return Get("SelectText"); } }
        public static string ReadAloud { get { return Get("ReadAloud"); } }
        public static string Share { get { return Get("Share"); } }
        public static string Regenerate { get { return Get("Regenerate"); } }
        public static string PinToTop { get { return Get("PinToTop"); } }
        public static string Unpin { get { return Get("Unpin"); } }
        public static string Rename { get { return Get("Rename"); } }
        public static string RenameChat { get { return Get("RenameChat"); } }
        public static string Done { get { return Get("Done"); } }
        public static string ImageReady { get { return Get("ImageReady"); } }
        public static string FileReady { get { return Get("FileReady"); } }
        public static string ReadingAloud { get { return Get("ReadingAloud"); } }
        public static string NoVoice { get { return Get("NoVoice"); } }
        public static string NowUsing { get { return Get("NowUsing"); } }
        public static string Pinned { get { return Get("Pinned"); } }
        public static string Unpinned { get { return Get("Unpinned"); } }
        public static string ChatDeleted { get { return Get("ChatDeleted"); } }
        public static string Saved { get { return Get("Saved"); } }
        public static string SavedFile { get { return Get("SavedFile"); } }
        public static string NoAppForFile { get { return Get("NoAppForFile"); } }
        public static string NothingFound { get { return Get("NothingFound"); } }
        public static string NoChatsYet { get { return Get("NoChatsYet"); } }
        public static string You { get { return Get("You"); } }
        public static string FilePreview { get { return Get("FilePreview"); } }
        public static string ImageCreated { get { return Get("ImageCreated"); } }
        public static string Start { get { return Get("Start"); } }
        public static string Next { get { return Get("Next"); } }
        public static string KeyConnected { get { return Get("KeyConnected"); } }
        public static string NoKeyYet { get { return Get("NoKeyYet"); } }
        public static string ImageModelNone { get { return Get("ImageModelNone"); } }
        public static string ImageModelUses { get { return Get("ImageModelUses"); } }
        public static string StorageUsed { get { return Get("StorageUsed"); } }
        public static string SystemDefault { get { return Get("SystemDefault"); } }
        public static string Small { get { return Get("Small"); } }
        public static string Normal { get { return Get("Normal"); } }
        public static string Large { get { return Get("Large"); } }
        public static string AboutTitle { get { return Get("AboutTitle"); } }
        public static string AskImagePrefix { get { return Get("AskImagePrefix"); } }
        public static string AskFilePrefix { get { return Get("AskFilePrefix"); } }
        public static string Retry { get { return Get("Retry"); } }
    }
}
