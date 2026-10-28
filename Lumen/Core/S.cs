namespace Lumen.Core
{
    /// <summary>All user-facing runtime strings (English only).</summary>
    public static class S
    {
        public const string AppVersion = "1.2";

        public const string GreetMorning = "Good morning";
        public const string GreetAfternoon = "Good afternoon";
        public const string GreetEvening = "Good evening";

        public const string Suggest1 = "Explain quantum computing like I'm 10";
        public const string Suggest2 = "Write a funny birthday message for a friend";
        public const string Suggest3 = "Quick dinner idea with eggs and potatoes";
        public const string Suggest4 = "Plan a perfect day trip, ask me where I live";

        public const string NewChatTitle = "New chat";
        public const string ImageOnly = "Photo";
        public const string VoiceOnly = "Voice message";
        public const string Copied = "Copied";
        public const string Stopped = "Stopped.";
        public const string Truncated = "Answer cut off: maximum length reached.";
        public const string ImageError = "Couldn't load the photo";
        public const string CameraError = "Camera not available, opening the gallery";
        public const string MicDenied = "Microphone access is off. Turn it on in Settings > Privacy > Microphone.";
        public const string MicError = "Couldn't start the microphone";
        public const string TooShort = "Hold on a bit longer, that was too short";
        public const string CreatingImage = "Creating the image";
        public const string CreatingFile = "Writing the file";
        public const string LookingUp = "Looking up \"{0}\" on Wikipedia";

        public const string Verifying = "Checking your key...";
        public const string ModelsLoaded = "Key works. {0} models available.";
        public const string KeyMissing = "Paste your API key first.";

        public const string DefaultSystemPrompt =
            "You are Lumen, a helpful assistant running on a small phone screen. " +
            "Answer clearly and concisely, use short paragraphs and lists, avoid wide tables. " +
            "Reply in the user's language.";

        public const string ErrKey = "Invalid or missing API key. Check it in Settings.";
        public const string ErrBadRequest = "The request was rejected.";
        public const string ErrModel = "This model isn't available for your key. Pick another one in Settings.";
        public const string ErrQuota = "Free quota reached. Wait a minute, or switch to Flash-Lite in Settings.";
        public const string ErrBusy = "Google's servers are busy. Try again in a moment.";
        public const string ErrTls = "Secure connection failed. The phone's certificates may be too old: set a proxy URL in Settings > Advanced.";
        public const string ErrNetwork = "No connection. Check mobile data or Wi-Fi.";
        public const string ErrGeneric = "Something went wrong.";
        public const string ErrBlocked = "The answer was blocked by Google's safety filters. Try rephrasing.";
        public const string ErrEmpty = "Empty answer. Try again.";

        public const string DeleteAllTitle = "Delete all chats?";
        public const string DeleteAllBody = "Conversations, photos and voice messages will be removed from this phone. This can't be undone.";
        public const string Delete = "Delete";
        public const string Cancel = "Cancel";
    }
}
