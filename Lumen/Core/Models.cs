using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace Lumen.Core
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    [Windows.UI.Xaml.Data.Bindable]
    public sealed class ChatMessage : Observable
    {
        public const string RoleUser = "user";
        public const string RoleModel = "model";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Role { get; set; } = RoleUser;
        public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

        /// <summary>Image file name inside LocalFolder\images (a user photo, or an image the model generated), or null.</summary>
        private string _imageFile;
        public string ImageFile
        {
            get { return _imageFile; }
            set
            {
                if (Set(ref _imageFile, value))
                {
                    _thumb = null;
                    Raise(nameof(ImageVisibility));
                    Raise(nameof(Thumb));
                }
            }
        }

        /// <summary>WAV file name inside LocalFolder\audio, or null.</summary>
        public string AudioFile { get; set; }
        public int AudioSeconds { get; set; }

        /// <summary>File created by the model (create_file tool), inside LocalFolder\files.</summary>
        private string _docFile;
        public string DocFile
        {
            get { return _docFile; }
            set { if (Set(ref _docFile, value)) { Raise(nameof(DocVisibility)); Raise(nameof(DocName)); } }
        }
        public string DocName { get { return string.IsNullOrEmpty(_docFile) ? "" : _docFile.Substring(_docFile.IndexOf('_') + 1); } }
        private long _docSize;
        public long DocSize { get { return _docSize; } set { if (Set(ref _docSize, value)) Raise(nameof(DocSizeLabel)); } }
        public string DocSizeLabel { get { return DocSize < 1024 ? DocSize + " B" : (DocSize / 1024.0).ToString("0.#") + " KB"; } }
        public Visibility DocVisibility { get { return string.IsNullOrEmpty(_docFile) ? Visibility.Collapsed : Visibility.Visible; } }

        public bool IsUser { get { return Role == RoleUser; } }

        private string _text = "";
        public string Text
        {
            get { return _text; }
            set
            {
                if (Set(ref _text, value ?? ""))
                {
                    Raise(nameof(ThinkingVisibility));
                    Raise(nameof(TextVisibility));
                    Raise(nameof(StreamingVisibility));
                }
            }
        }

        private bool _isStreaming;
        public bool IsStreaming
        {
            get { return _isStreaming; }
            set
            {
                if (Set(ref _isStreaming, value))
                {
                    Raise(nameof(StreamingVisibility));
                    Raise(nameof(RenderedVisibility));
                    Raise(nameof(ThinkingVisibility));
                    Raise(nameof(RenderedText));
                }
            }
        }

        private bool _isError;
        public bool IsError
        {
            get { return _isError; }
            set
            {
                if (Set(ref _isError, value))
                {
                    Raise(nameof(ErrorVisibility));
                    Raise(nameof(RenderedVisibility));
                    Raise(nameof(RenderedText));
                }
            }
        }

        private string _status;
        /// <summary>Transient line shown while a tool runs, e.g. a Wikipedia lookup.</summary>
        public string Status
        {
            get { return _status; }
            set { if (Set(ref _status, value)) Raise(nameof(StatusVisibility)); }
        }
        public Visibility StatusVisibility { get { return string.IsNullOrEmpty(_status) ? Visibility.Collapsed : Visibility.Visible; } }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get { return _isPlaying; }
            set { if (Set(ref _isPlaying, value)) Raise(nameof(PlayGlyph)); }
        }

        /// <summary>Text pushed to the markdown renderer: only once streaming is over, so long answers are parsed once.</summary>
        public string RenderedText { get { return (_isStreaming || _isError) ? null : _text; } }

        public Visibility StreamingVisibility { get { return _isStreaming && _text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility RenderedVisibility { get { return (_isStreaming || _isError) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility ThinkingVisibility { get { return _isStreaming && _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ErrorVisibility { get { return _isError ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ImageVisibility { get { return string.IsNullOrEmpty(ImageFile) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility AudioVisibility { get { return string.IsNullOrEmpty(AudioFile) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility TextVisibility { get { return string.IsNullOrEmpty(_text) ? Visibility.Collapsed : Visibility.Visible; } }

        public string AudioLabel { get { return string.Format("{0}:{1:00}", AudioSeconds / 60, AudioSeconds % 60); } }
        public string PlayGlyph { get { return _isPlaying ? Icons.Pause : Icons.Play; } }

        private BitmapImage _thumb;
        /// <summary>Lazy, downscaled thumbnail. Created on first binding (UI thread).</summary>
        public BitmapImage Thumb
        {
            get
            {
                if (_thumb == null && !string.IsNullOrEmpty(ImageFile))
                {
                    _thumb = new BitmapImage { DecodePixelWidth = IsUser ? 440 : 600 };
                    _thumb.UriSource = new Uri("ms-appdata:///local/images/" + ImageFile);
                }
                return _thumb;
            }
        }
    }

    [Windows.UI.Xaml.Data.Bindable]
    public sealed class ConversationInfo : Observable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _title = "";
        public string Title { get { return _title; } set { if (Set(ref _title, value ?? "")) Raise(nameof(Initial)); } }

        private DateTimeOffset _updated = DateTimeOffset.Now;
        public DateTimeOffset Updated
        {
            get { return _updated; }
            set { if (Set(ref _updated, value)) Raise(nameof(UpdatedLabel)); }
        }

        private string _preview = "";
        public string Preview { get { return _preview; } set { Set(ref _preview, value ?? ""); } }

        private bool _pinned;
        public bool Pinned
        {
            get { return _pinned; }
            set { if (Set(ref _pinned, value)) { Raise(nameof(PinVisibility)); Raise(nameof(PinLabel)); } }
        }
        public string PinLabel { get { return _pinned ? "Unpin" : "Pin to top"; } }
        public Visibility PinVisibility { get { return _pinned ? Visibility.Visible : Visibility.Collapsed; } }

        /// <summary>First letter for the avatar.</summary>
        public string Initial
        {
            get
            {
                foreach (var ch in _title) if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
                return "L";
            }
        }

        public string UpdatedLabel
        {
            get
            {
                var local = _updated.ToLocalTime();
                return local.Date == DateTimeOffset.Now.Date ? local.ToString("HH:mm") : local.ToString("MMM d");
            }
        }
    }

    public sealed class Conversation
    {
        public ConversationInfo Info { get; set; } = new ConversationInfo();
        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
    }
}
