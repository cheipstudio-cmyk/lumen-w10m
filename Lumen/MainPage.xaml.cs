using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumen.Core;
using Lumen.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Lumen
{
    public sealed partial class MainPage : Page, IBackAware
    {
        private static GeminiClient Client { get { return GeminiClient.Shared; } }

        private readonly ObservableCollection<ConversationInfo> _conversations = new ObservableCollection<ConversationInfo>();
        private Conversation _conv;
        private bool _indexLoaded;
        private int _generation;
        private bool _busy;
        private CancellationTokenSource _cts;
        private string _pendingImage;
        private ShareOperation _share;
        private string _shareText;
        private IStorageItem _shareFile;
        private ScrollViewer _scroller;
        private DispatcherTimer _toastTimer;
        private Storyboard _toastHide;

        // voice in / voice out
        private readonly AudioRecorder _recorder = new AudioRecorder();
        private DispatcherTimer _recordTimer;
        private Storyboard _recordPulse;
        private MediaPlayer _player;
        private ChatMessage _playing;

        public MainPage()
        {
            InitializeComponent();
            AppBar.RequestedTheme = ThemeManager.Theme;
            ConversationsList.ItemsSource = _conversations;
            _conversations.CollectionChanged += (s, e) => UpdateDrawerEmpty();
            // handledEventsToo: the multi-line TextBox would otherwise swallow Enter.
            InputBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(InputBox_KeyDown), true);

            Suggest1.Content = SuggestionContent(Icons.Lightbulb, S.Suggest1); Suggest1.Tag = S.Suggest1;
            Suggest2.Content = SuggestionContent(Icons.Edit, S.Suggest2); Suggest2.Tag = S.Suggest2;
            Suggest3.Content = SuggestionContent(Icons.ImageSparkle, S.Suggest3); Suggest3.Tag = S.Suggest3;
            Suggest4.Content = SuggestionContent(Icons.Globe, S.Suggest4); Suggest4.Tag = S.Suggest4;

            // Subscribed only while on screen: pages rebuilt by a language/theme switch must not answer share requests.
            Loaded += (s, a) => DataTransferManager.GetForCurrentView().DataRequested += OnShareRequested;
            Unloaded += (s, a) => DataTransferManager.GetForCurrentView().DataRequested -= OnShareRequested;
            SuggestionsPanel.SizeChanged += (s, a) =>
            {
                // Square live tiles.
                var side = Math.Max(110, Math.Min(190, a.NewSize.Width / 2 - 8));
                foreach (var child in SuggestionsPanel.Children) ((FrameworkElement)child).Height = side;
            };
            StartNewConversation();
        }

        /// <summary>True while any MainPage is streaming an answer (language/theme switches wait for it).</summary>
        public static bool IsBusy { get; private set; }

        /// <summary>Live tile content: icon top-left, text bottom-left, like the Start screen.</summary>
        private static UIElement SuggestionContent(Uri icon, string text)
        {
            var g = new Grid();
            g.Children.Add(new BitmapIcon
            {
                UriSource = icon,
                Width = 30,
                Height = 30,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            });
            g.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxLines = 4,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return g;
        }

        // ================================================================ navigation

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyPreferences();

            if (_generation != ChatStore.Generation && !_busy)
            {
                _generation = ChatStore.Generation;
                _conversations.Clear();
                StartNewConversation();
            }

            if (!_indexLoaded)
            {
                _indexLoaded = true;
                var items = await ChatStore.LoadIndexAsync();
                foreach (var c in items) _conversations.Add(c);
                UpdateDrawerEmpty();
            }

            var share = e.Parameter as ShareOperation;
            if (share != null && _share == null)
            {
                _share = share;
                await ReceiveShareAsync(share);
            }
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            StopPlayback();
            if (_recorder.IsRecording) await FinishRecordingAsync(false);
        }

        public bool HandleBack()
        {
            if (MainPivot.SelectedIndex != 0) { MainPivot.SelectedIndex = 0; return true; }
            if (_recorder.IsRecording) { var t = FinishRecordingAsync(false); return true; }
            if (_share != null)
            {
                try { _share.ReportCompleted(); } catch (Exception) { }
                return true;
            }
            return false;
        }

        /// <summary>Re-read settings after coming back from Settings / onboarding.</summary>
        private void ApplyPreferences()
        {
            ModelLabel.Text = AppSettings.ModelDisplayName;
            NoKeyCard.Visibility = AppSettings.HasApiKey ? Visibility.Collapsed : Visibility.Visible;
            SuggestionsPanel.Visibility = AppSettings.HasApiKey ? Visibility.Visible : Visibility.Collapsed;
            GreetingText.Text = Greeting();
            MessagesList.FontSize = ThemeManager.MessageFontSize;
            MessagesList.ItemContainerTransitions = Fx.Enabled
                ? new TransitionCollection { new AddDeleteThemeTransition() }
                : new TransitionCollection();
            UpdateEmptyState();
            UpdateSendState();
        }

        private static string Greeting()
        {
            var h = DateTime.Now.Hour;
            if (h >= 5 && h < 12) return S.GreetMorning;
            if (h >= 12 && h < 18) return S.GreetAfternoon;
            return S.GreetEvening;
        }

        // ================================================================ conversations

        private void StartNewConversation()
        {
            _conv = new Conversation();
            _conv.Info.Title = S.NewChatTitle;
            MessagesList.ItemsSource = _conv.Messages;
            ConversationsList.SelectedItem = null;
            TitleText.Text = "Lumen";
            UpdateEmptyState();
        }

        private async Task OpenConversationAsync(ConversationInfo info)
        {
            if (_busy) return;
            if (_conv != null && _conv.Info.Id == info.Id) return;
            StopPlayback();
            _conv = await ChatStore.LoadAsync(info);
            MessagesList.ItemsSource = _conv.Messages;
            TitleText.Text = info.Title;
            UpdateEmptyState();
            ScrollToEnd(false);
        }

        private void UpdateEmptyState()
        {
            var empty = _conv.Messages.Count == 0;
            var wasVisible = EmptyState.Visibility == Visibility.Visible;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

            if (empty && !wasVisible)
            {
                // Greeting slides in, then the live tiles cascade.
                Fx.FadeInUp(GreetingText, 80, 20, 500);
                Fx.FadeInUp(EmptySubtitle, 160, 20, 500);
                for (int i = 0; i < SuggestionsPanel.Children.Count; i++) Fx.FadeInUp(SuggestionsPanel.Children[i], 260 + i * 80, 24, 480);
            }
        }

        private void UpdateDrawerEmpty()
        {
            var list = ConversationsList.ItemsSource as System.Collections.ICollection;
            var none = list == null || list.Count == 0;
            NoChats.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            NoChatsText.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? S.NoChatsYet : S.NothingFound;
        }

        private async Task PersistAsync()
        {
            var info = _conv.Info;
            info.Updated = DateTimeOffset.Now;
            var last = _conv.Messages.LastOrDefault(m => !m.IsError);
            if (last != null) info.Preview = PreviewOf(last);

            var idx = _conversations.IndexOf(info);
            var target = info.Pinned ? 0 : _conversations.Count(c => c.Pinned);
            if (idx < 0) _conversations.Insert(Math.Min(target, _conversations.Count), info);
            else if (idx != target && target < _conversations.Count) _conversations.Move(idx, target);
            ConversationsList.SelectedItem = info;
            TitleText.Text = info.Title;

            await ChatStore.SaveAsync(_conv);
            await ChatStore.SaveIndexAsync(_conversations);
            TileHelper.Update(info.Title, info.Preview);
        }

        private static string PreviewOf(ChatMessage m)
        {
            if (!string.IsNullOrEmpty(m.DocFile)) return string.Format(S.FilePreview, m.DocName);
            if (!string.IsNullOrEmpty(m.ImageFile) && !m.IsUser) return S.ImageCreated;
            var t = (m.Text ?? "").Replace("\n", " ").Replace("**", "").Replace("#", "").Trim();
            if (t.Length == 0) return m.AudioFile != null ? S.VoiceOnly : S.ImageOnly;
            return (m.IsUser ? S.You : "") + (t.Length > 80 ? t.Substring(0, 80) : t);
        }

        private static string MakeTitle(string text, bool hasAudio)
        {
            var t = (text ?? "").Replace('\n', ' ').Trim();
            if (t.Length == 0) return hasAudio ? S.VoiceOnly : S.ImageOnly;
            return t.Length > 42 ? t.Substring(0, 42).TrimEnd() + "…" : t;
        }

        // ================================================================ sending

        private async Task SendAsync(string text, string audioFile = null, int audioSeconds = 0)
        {
            if (_busy) return;
            text = (text ?? "").Trim();
            if (text.Length == 0 && _pendingImage == null && audioFile == null) return;

            if (!AppSettings.HasApiKey)
            {
                Frame.Navigate(typeof(SettingsPage));
                return;
            }

            if (_conv.Messages.Count == 0)
            {
                _conv.Info.Title = MakeTitle(text, audioFile != null);
                TitleText.Text = _conv.Info.Title;
            }

            _conv.Messages.Add(new ChatMessage
            {
                Role = ChatMessage.RoleUser,
                Text = text,
                ImageFile = _pendingImage,
                AudioFile = audioFile,
                AudioSeconds = audioSeconds
            });
            InputBox.Text = "";
            ClearAttachment();
            UpdateEmptyState();
            await RunModelAsync();
        }

        private async Task RunModelAsync()
        {
            var history = _conv.Messages.Where(m => !m.IsError).ToList();
            var reply = new ChatMessage { Role = ChatMessage.RoleModel, IsStreaming = true };
            _conv.Messages.Add(reply);
            ScrollToEnd(true);
            SetBusy(true);

            var cts = new CancellationTokenSource();
            _cts = cts;
            var gate = new object();
            var buffer = new StringBuilder();
            var dirty = false;
            string status = null;
            var statusDirty = false;
            var artifacts = new List<Artifact>();

            // Push text to the UI at ~15 fps instead of on every chunk: smooth on a Lumia 735.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
            Action pump = () =>
            {
                string snapshot = null, st = null;
                bool stChanged;
                Artifact[] arts;
                lock (gate)
                {
                    if (dirty) { snapshot = buffer.ToString(); dirty = false; }
                    stChanged = statusDirty; st = status; statusDirty = false;
                    arts = artifacts.ToArray(); artifacts.Clear();
                }
                if (stChanged) reply.Status = st;
                foreach (var a in arts)
                {
                    if (a.IsImage) reply.ImageFile = a.FileName;
                    else { reply.DocSize = a.Size; reply.DocFile = a.FileName; }
                }
                if (snapshot != null || arts.Length > 0)
                {
                    var stick = IsNearBottom();
                    if (snapshot != null) reply.Text = snapshot;
                    if (stick) ScrollToEnd(false);
                }
            };
            timer.Tick += (s, e) => pump();
            timer.Start();

            string apiKey = AppSettings.ApiKey, baseUrl = AppSettings.BaseUrl, model = AppSettings.Model, system = AppSettings.SystemPrompt;
            double temp = AppSettings.Temperature;
            var tools = new ToolOptions { Wikipedia = AppSettings.Wikipedia, Creative = AppSettings.Creative };

            try
            {
                var result = await Task.Run(() => Client.StreamAsync(apiKey, baseUrl, model, system, temp, tools, history,
                    delta => { lock (gate) { buffer.Append(delta); dirty = true; } },
                    st => { lock (gate) { status = st; statusDirty = true; } },
                    art => { lock (gate) { artifacts.Add(art); } },
                    cts.Token));

                pump();
                string final;
                lock (gate) final = buffer.ToString();
                var hasArtifact = !string.IsNullOrEmpty(reply.ImageFile) || !string.IsNullOrEmpty(reply.DocFile);
                if (final.Trim().Length == 0 && !hasArtifact)
                {
                    reply.IsError = true;
                    reply.Text = !string.IsNullOrEmpty(result.BlockReason) || result.FinishReason == "SAFETY" || result.FinishReason == "PROHIBITED_CONTENT"
                        ? S.ErrBlocked
                        : S.ErrEmpty;
                }
                else
                {
                    if (result.FinishReason == "MAX_TOKENS") final += "\n\n_" + S.Truncated + "_";
                    reply.Text = final;
                    if (!string.IsNullOrEmpty(reply.ImageFile)) ShowToast(Icons.ImageSparkle, S.ImageReady);
                    else if (!string.IsNullOrEmpty(reply.DocFile)) ShowToast(Icons.Document, string.Format(S.FileReady, reply.DocName));
                }
            }
            catch (Exception ex)
            {
                pump();
                string partial;
                lock (gate) partial = buffer.ToString();
                var cancelled = cts.IsCancellationRequested;

                if (partial.Trim().Length > 0)
                {
                    reply.Text = partial + "\n\n_" + (cancelled ? S.Stopped : GeminiClient.Describe(ex)) + "_";
                }
                else if (cancelled)
                {
                    _conv.Messages.Remove(reply);
                }
                else
                {
                    reply.IsError = true;
                    reply.Text = GeminiClient.Describe(ex);
                }
            }
            finally
            {
                timer.Stop();
                reply.Status = null;
                reply.IsStreaming = false;
                _cts = null;
                cts.Dispose();
                SetBusy(false);
            }

            if (IsNearBottom()) ScrollToEnd(true);
            await PersistAsync();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            IsBusy = busy;
            ModelButton.IsEnabled = !busy;
            UpdateBarState();
            BusyBar.IsIndeterminate = busy;
            BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            UpdateSendState();
        }

        /// <summary>One button, three jobs: stop (answering), send (recording or something typed), mic (empty).</summary>
        private void UpdateSendState()
        {
            Uri glyph;
            if (_busy) glyph = Icons.Stop;
            else if (_recorder.IsRecording) glyph = Icons.Send;
            else if (InputBox.Text.Trim().Length > 0 || _pendingImage != null) glyph = Icons.Send;
            else glyph = Icons.Mic;

            if (SendGlyph.UriSource != glyph)
            {
                SendGlyph.UriSource = glyph;
                SendButton.Label = _busy ? S.LabelStop : glyph == Icons.Mic ? S.LabelVoice : S.LabelSend;
            }
        }

        // ================================================================ voice

        private async Task StartRecordingAsync()
        {
            StopPlayback();
            try
            {
                await _recorder.StartAsync();
            }
            catch (UnauthorizedAccessException)
            {
                ShowToast(Icons.Mic, S.MicDenied, true);
                return;
            }
            catch (Exception)
            {
                ShowToast(Icons.Mic, S.MicError, true);
                return;
            }

            InputBox.Visibility = Visibility.Collapsed;
            RecordingBar.Visibility = Visibility.Visible;
            RecordTime.Text = "0:00";
            Fx.FadeInUp(RecordingBar, 0, 10, 220);
            _recordPulse = Fx.Pulse(RecordDot);

            if (_recordTimer == null)
            {
                _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _recordTimer.Tick += async (s, e) =>
                {
                    var el = _recorder.Elapsed;
                    RecordTime.Text = string.Format("{0}:{1:00}", (int)el.TotalMinutes, el.Seconds);
                    if (el.TotalSeconds >= AudioRecorder.MaxSeconds) await FinishRecordingAsync(true);
                };
            }
            _recordTimer.Start();
            UpdateSendState();
        }

        private async Task FinishRecordingAsync(bool send)
        {
            _recordTimer?.Stop();
            _recordPulse?.Stop();
            RecordingBar.Visibility = Visibility.Collapsed;
            InputBox.Visibility = Visibility.Visible;

            var result = await _recorder.StopAsync(send);
            UpdateSendState();
            if (!send) return;
            if (result == null) { ShowToast(Icons.Mic, S.TooShort); return; }
            await SendAsync(InputBox.Text, result.Item1, result.Item2);
        }

        private MediaPlayer Player
        {
            get
            {
                if (_player == null)
                {
                    _player = new MediaPlayer { AutoPlay = false };
                    _player.MediaEnded += async (s, a) =>
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, StopPlayback);
                }
                return _player;
            }
        }

        private void StopPlayback()
        {
            if (_player != null) _player.Pause();
            if (_playing != null) _playing.IsPlaying = false;
            _playing = null;
        }

        private async void PlayAudio_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.AudioFile)) return;
            if (_playing == msg) { StopPlayback(); return; }
            StopPlayback();
            try
            {
                var folder = await AudioRecorder.AudioFolderAsync();
                var file = await folder.GetFileAsync(msg.AudioFile);
                Player.Source = MediaSource.CreateFromStorageFile(file);
                Player.Play();
                _playing = msg;
                msg.IsPlaying = true;
            }
            catch (Exception)
            {
                ShowToast(Icons.Warning, S.ErrGeneric, true);
            }
        }

        private async void ReadAloud_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrWhiteSpace(msg.Text)) return;
            StopPlayback();
            try
            {
                Player.Source = await Speech.SynthesizeAsync(msg.Text);
                Player.Play();
                ShowToast(Icons.Speak, S.ReadingAloud);
            }
            catch (Exception)
            {
                ShowToast(Icons.Speak, S.NoVoice, true);
            }
        }

        private async void CancelRecording_Click(object sender, RoutedEventArgs e)
        {
            await FinishRecordingAsync(false);
        }

        // ================================================================ scrolling

        private ScrollViewer Scroller
        {
            get
            {
                if (_scroller == null) _scroller = FindChild<ScrollViewer>(MessagesList);
                return _scroller;
            }
        }

        private bool IsNearBottom()
        {
            var sv = Scroller;
            return sv == null || sv.VerticalOffset >= sv.ScrollableHeight - 160;
        }

        private void ScrollToEnd(bool animate)
        {
            var sv = Scroller;
            if (sv == null)
            {
                if (_conv.Messages.Count > 0) MessagesList.ScrollIntoView(_conv.Messages[_conv.Messages.Count - 1]);
                return;
            }
            // Let layout catch up first instead of forcing a synchronous UpdateLayout on every tick.
            var instant = !animate || !Fx.Enabled;
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => sv.ChangeView(null, sv.ScrollableHeight, null, instant));
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var hit = child as T ?? FindChild<T>(child);
                if (hit != null) return hit;
            }
            return null;
        }

        // ================================================================ attachments & share

        private async Task AttachAsync(IStorageFile file)
        {
            if (file == null) return;
            try
            {
                var name = await ImageHelper.ImportAsync(file);
                SetPendingImage(name);
            }
            catch (Exception)
            {
                ShowToast(Icons.Image, S.ImageError, true);
            }
        }

        private void SetPendingImage(string name)
        {
            _pendingImage = name;
            var bmp = new BitmapImage { DecodePixelWidth = 152 };
            bmp.UriSource = new Uri("ms-appdata:///local/images/" + name);
            AttachmentImage.Source = bmp;
            AttachmentPreview.Visibility = Visibility.Visible;
            Fx.Pop(AttachmentPreview, 0.6, 360);
            UpdateSendState();
        }

        private void ClearAttachment()
        {
            _pendingImage = null;
            AttachmentImage.Source = null;
            AttachmentPreview.Visibility = Visibility.Collapsed;
            UpdateSendState();
        }

        private async Task ReceiveShareAsync(ShareOperation share)
        {
            var data = share.Data;
            var text = new StringBuilder();
            try
            {
                if (data.Contains(StandardDataFormats.Text)) text.Append(await data.GetTextAsync());
                if (data.Contains(StandardDataFormats.WebLink))
                {
                    var link = await data.GetWebLinkAsync();
                    if (text.Length > 0) text.Append("\n");
                    text.Append(link.ToString());
                }
                if (data.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await data.GetStorageItemsAsync();
                    var file = items.OfType<StorageFile>().FirstOrDefault();
                    if (file != null) await AttachAsync(file);
                }
            }
            catch (Exception) { }
            InputBox.Text = text.ToString();
            InputBox.SelectionStart = InputBox.Text.Length;
            UpdateSendState();
            InputBox.Focus(FocusState.Programmatic);
        }

        private void OnShareRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            args.Request.Data.Properties.Title = "Lumen";
            if (_shareFile != null)
            {
                args.Request.Data.SetStorageItems(new[] { _shareFile });
                _shareFile = null;
            }
            else if (!string.IsNullOrEmpty(_shareText))
            {
                args.Request.Data.SetText(_shareText);
                _shareText = null;
            }
        }

        // ================================================================ notifications

        /// <summary>Animated in-app notification: slides down with a little overshoot, then slides back up.</summary>
        private void ShowToast(Uri glyph, string text, bool warning = false)
        {
            // A hide animation still running (or holding its end values) must not swallow this toast.
            _toastHide?.Stop();
            _toastHide = null;
            Toast.Opacity = 1;
            ToastMove.Y = 0;
            ToastIcon.UriSource = glyph;
            ToastText.Text = text;
            Toast.Visibility = Visibility.Visible;

            if (Fx.Enabled)
            {
                var sb = new Storyboard();
                var slide = new DoubleAnimation { From = -90, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(520)), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } };
                Storyboard.SetTarget(slide, ToastMove); Storyboard.SetTargetProperty(slide, "Y");
                var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(220)) };
                Storyboard.SetTarget(fade, Toast); Storyboard.SetTargetProperty(fade, "Opacity");
                sb.Children.Add(slide); sb.Children.Add(fade);
                sb.Begin();
            }

            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer();
                _toastTimer.Tick += (s, e) => HideToast();
            }
            _toastTimer.Interval = TimeSpan.FromMilliseconds(warning ? 4200 : 2400);
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer?.Stop();
            if (!Fx.Enabled) { Toast.Visibility = Visibility.Collapsed; return; }
            var sb = new Storyboard();
            var slide = new DoubleAnimation { To = -90, Duration = new Duration(TimeSpan.FromMilliseconds(300)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(slide, ToastMove); Storyboard.SetTargetProperty(slide, "Y");
            var fade = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
            Storyboard.SetTarget(fade, Toast); Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(slide); sb.Children.Add(fade);
            _toastHide = sb;
            sb.Completed += (s, e) => { if (sb == _toastHide) { Toast.Visibility = Visibility.Collapsed; _toastHide = null; } };
            sb.Begin();
        }

        private void Toast_Tapped(object sender, TappedRoutedEventArgs e)
        {
            StopPlayback();
            HideToast();
        }

        // ================================================================ composer events

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                var c = _cts;
                if (c != null) c.Cancel();
                return;
            }
            if (_recorder.IsRecording) { await FinishRecordingAsync(true); return; }
            if (InputBox.Text.Trim().Length == 0 && _pendingImage == null) { await StartRecordingAsync(); return; }
            await SendAsync(InputBox.Text);
        }

        private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (shift) return; // let the TextBox insert the line break
            e.Handled = true;
            // The TextBox may already have inserted the newline: strip it before sending.
            await SendAsync(InputBox.Text.TrimEnd('\r', '\n'));
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendState();
        }

        private async void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            await SendAsync(b.Tag as string);
        }

        private async void Camera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = await ImageHelper.CaptureAsync();
                await AttachAsync(file);
            }
            catch (Exception)
            {
                // No camera API on this device: fall back to the gallery picker.
                ShowToast(Icons.Camera, S.CameraError);
                Gallery_Click(sender, e);
            }
        }

        private async void Gallery_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = await ImageHelper.PickAsync();
                await AttachAsync(file);
            }
            catch (Exception)
            {
                ShowToast(Icons.Image, S.ImageError, true);
            }
        }

        private void AskImage_Click(object sender, RoutedEventArgs e)
        {
            Prefill(S.AskImagePrefix);
        }

        private void AskFile_Click(object sender, RoutedEventArgs e)
        {
            Prefill(S.AskFilePrefix);
        }

        private void Prefill(string text)
        {
            InputBox.Text = text;
            InputBox.SelectionStart = text.Length;
            InputBox.Focus(FocusState.Programmatic);
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            ClearAttachment();
        }

        // ================================================================ header & drawer

        private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Composer == null || SendButton == null) return;
            var chat = MainPivot.SelectedIndex == 0;
            Composer.Visibility = chat ? Visibility.Visible : Visibility.Collapsed;
            UpdateBarState();
        }

        /// <summary>Send/attach only make sense on the chat pivot, but Stop must stay reachable everywhere.</summary>
        private void UpdateBarState()
        {
            var chat = MainPivot.SelectedIndex == 0;
            SendButton.IsEnabled = chat || _busy;
            AttachButton.IsEnabled = chat && !_busy;
        }

        private void Tour_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            Frame.Navigate(typeof(OnboardingPage));
        }

        private void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            // Quick model switch without going to Settings.
            var flyout = ThemeManager.Themed(new MenuFlyout());
            var current = AppSettings.Model;
            foreach (var m in AppSettings.Models)
            {
                var item = new ToggleMenuFlyoutItem { Text = m.DisplayName, IsChecked = m.Id == current, Tag = m.Id };
                item.Click += (s, a) =>
                {
                    AppSettings.Model = (string)((FrameworkElement)s).Tag;
                    ModelLabel.Text = AppSettings.ModelDisplayName;
                    ShowToast(Icons.Model, string.Format(S.NowUsing, AppSettings.ModelDisplayName));
                };
                flyout.Items.Add(item);
            }
            flyout.ShowAt(ModelButton);
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            MainPivot.SelectedIndex = 0;
            if (_busy) return;
            StopPlayback();
            StartNewConversation();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private async void Conversation_ItemClick(object sender, ItemClickEventArgs e)
        {
            MainPivot.SelectedIndex = 0;
            var info = e.ClickedItem as ConversationInfo;
            if (info != null) await OpenConversationAsync(info);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0) ConversationsList.ItemsSource = _conversations;
            else
            {
                ConversationsList.ItemsSource = _conversations
                    .Where(c => c.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Preview.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }
            UpdateDrawerEmpty();
        }

        private async void PinConversation_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement)?.DataContext as ConversationInfo;
            if (info == null) return;
            info.Pinned = !info.Pinned;
            var idx = _conversations.IndexOf(info);
            var target = info.Pinned ? 0 : Math.Max(0, _conversations.Count(c => c.Pinned));
            if (idx >= 0 && idx != target && target < _conversations.Count) _conversations.Move(idx, target);
            await ChatStore.SaveIndexAsync(_conversations);
            ShowToast(Icons.Pin, info.Pinned ? S.Pinned : S.Unpinned);
        }

        private async void RenameConversation_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement)?.DataContext as ConversationInfo;
            if (info == null) return;
            var box = new TextBox { Text = info.Title, SelectionStart = 0, SelectionLength = info.Title.Length };
            var dialog = new ContentDialog
            {
                RequestedTheme = ThemeManager.Theme,
                Title = S.RenameChat,
                Content = box,
                PrimaryButtonText = S.Rename,
                SecondaryButtonText = S.Cancel
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var name = box.Text.Trim();
            if (name.Length == 0) return;
            info.Title = name;
            if (_conv.Info == info) TitleText.Text = name;
            await ChatStore.SaveIndexAsync(_conversations);
        }

        private async void DeleteConversation_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement)?.DataContext as ConversationInfo;
            if (info == null) return;
            if (_busy && _conv.Info == info) return;
            _conversations.Remove(info);
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox_TextChanged(SearchBox, null);
            await ChatStore.DeleteAsync(info);
            await ChatStore.SaveIndexAsync(_conversations);
            if (_conv.Info.Id == info.Id) StartNewConversation();
            ShowToast(Icons.Delete, S.ChatDeleted);
        }

        // ================================================================ context menus (built on demand: none per list item)

        private static MenuFlyoutItem MenuItem(string text, Uri icon, object context, RoutedEventHandler click, bool danger = false)
        {
            var item = new MenuFlyoutItem { Text = text, DataContext = context };
            var bi = new BitmapIcon { UriSource = icon, Width = 20, Height = 20 };
            if (danger)
            {
                var red = (Brush)Application.Current.Resources["DangerBrush"];
                item.Foreground = red;
                bi.Foreground = red;
            }
            item.Icon = bi;
            item.Click += click;
            return item;
        }

        private static void ShowMenu(MenuFlyout menu, FrameworkElement target, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            try { menu.ShowAt(target, e.GetPosition(target)); }
            catch (Exception) { menu.ShowAt(target); }
        }

        private void UserMessage_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var el = (FrameworkElement)sender;
            var msg = el.DataContext as ChatMessage;
            if (msg == null) return;
            var menu = ThemeManager.Themed(new MenuFlyout());
            if (!string.IsNullOrEmpty(msg.Text)) menu.Items.Add(MenuItem(S.Copy, Icons.Copy, msg, CopyMessage_Click));
            menu.Items.Add(MenuItem(S.EditResend, Icons.Edit, msg, EditMessage_Click));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(S.Delete, Icons.Delete, msg, DeleteMessage_Click, true));
            ShowMenu(menu, el, e);
        }

        private void ModelMessage_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var el = (FrameworkElement)sender;
            var msg = el.DataContext as ChatMessage;
            if (msg == null || msg.IsStreaming) return;
            var menu = ThemeManager.Themed(new MenuFlyout());
            if (!string.IsNullOrEmpty(msg.Text))
            {
                menu.Items.Add(MenuItem(S.Copy, Icons.Copy, msg, CopyMessage_Click));
                menu.Items.Add(MenuItem(S.SelectText, Icons.SelectText, msg, SelectText_Click));
                menu.Items.Add(MenuItem(S.ReadAloud, Icons.Speak, msg, ReadAloud_Click));
                menu.Items.Add(MenuItem(S.Share, Icons.Share, msg, ShareMessage_Click));
            }
            menu.Items.Add(MenuItem(S.Regenerate, Icons.Regenerate, msg, Regenerate_Click));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(S.Delete, Icons.Delete, msg, DeleteMessage_Click, true));
            ShowMenu(menu, el, e);
        }

        private void Conversation_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var el = (FrameworkElement)sender;
            var info = el.DataContext as ConversationInfo;
            if (info == null) return;
            var menu = ThemeManager.Themed(new MenuFlyout());
            menu.Items.Add(MenuItem(info.PinLabel, info.Pinned ? Icons.Unpin : Icons.Pin, info, PinConversation_Click));
            menu.Items.Add(MenuItem(S.Rename, Icons.Rename, info, RenameConversation_Click));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(S.Delete, Icons.Delete, info, DeleteConversation_Click, true));
            ShowMenu(menu, el, e);
        }

        // ================================================================ message actions

        private static ChatMessage Msg(object sender)
        {
            return (sender as FrameworkElement)?.DataContext as ChatMessage;
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.Text)) return;
            var dp = new DataPackage();
            dp.SetText(msg.Text);
            Clipboard.SetContent(dp);
            ShowToast(Icons.Copy, S.Copied);
        }

        private async void SelectText_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null) return;
            var box = new TextBox
            {
                Text = msg.Text ?? "",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MaxHeight = 420
            };
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            var dialog = new ContentDialog { RequestedTheme = ThemeManager.Theme, Title = S.SelectText, Content = box, PrimaryButtonText = S.Done };
            await dialog.ShowAsync();
        }

        private void ShareMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.Text)) return;
            _shareFile = null;
            _shareText = msg.Text;
            DataTransferManager.ShowShareUI();
        }

        private async void Regenerate_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || _busy) return;
            var idx = _conv.Messages.IndexOf(msg);
            if (idx < 0) return;
            // Drop this answer and everything after it, then ask again.
            while (_conv.Messages.Count > idx) _conv.Messages.RemoveAt(_conv.Messages.Count - 1);
            if (_conv.Messages.Count == 0) { UpdateEmptyState(); return; }
            await RunModelAsync();
        }

        private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || _busy) return;
            _conv.Messages.Remove(msg);
            UpdateEmptyState();
            if (_conversations.Contains(_conv.Info)) await ChatStore.SaveAsync(_conv);
        }

        private async void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || _busy) return;
            var idx = _conv.Messages.IndexOf(msg);
            if (idx < 0) return;
            while (_conv.Messages.Count > idx) _conv.Messages.RemoveAt(_conv.Messages.Count - 1);

            // A voice note can't be edited: resend it as is.
            if (!string.IsNullOrEmpty(msg.AudioFile) && string.IsNullOrEmpty(msg.Text))
            {
                if (!string.IsNullOrEmpty(msg.ImageFile)) SetPendingImage(msg.ImageFile);
                await SendAsync("", msg.AudioFile, msg.AudioSeconds);
                return;
            }

            InputBox.Text = msg.Text;
            InputBox.SelectionStart = InputBox.Text.Length;
            if (!string.IsNullOrEmpty(msg.ImageFile)) SetPendingImage(msg.ImageFile);
            UpdateEmptyState();
            UpdateSendState();
            InputBox.Focus(FocusState.Programmatic);
        }

        // ================================================================ generated images & files

        private async void GeneratedImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.ImageFile)) return;
            try { await FileTools.OpenAsync(await FileTools.GetImageAsync(msg.ImageFile)); }
            catch (Exception) { ShowToast(Icons.Warning, S.ErrGeneric, true); }
        }

        private async void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.ImageFile)) return;
            try
            {
                var file = await FileTools.GetImageAsync(msg.ImageFile);
                if (await FileTools.SaveAsAsync(file, "Lumen " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + file.FileType))
                    ShowToast(Icons.Save, S.Saved);
            }
            catch (Exception) { ShowToast(Icons.Warning, S.ErrGeneric, true); }
        }

        private async void ShareImage_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.ImageFile)) return;
            try
            {
                _shareFile = await FileTools.GetImageAsync(msg.ImageFile);
                DataTransferManager.ShowShareUI();
            }
            catch (Exception) { ShowToast(Icons.Warning, S.ErrGeneric, true); }
        }

        private async void OpenDoc_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.DocFile)) return;
            try { await FileTools.OpenAsync(await FileTools.GetDocAsync(msg.DocFile)); }
            catch (Exception) { ShowToast(Icons.Warning, S.NoAppForFile, true); }
        }

        private async void SaveDoc_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.DocFile)) return;
            try
            {
                if (await FileTools.SaveAsAsync(await FileTools.GetDocAsync(msg.DocFile), msg.DocName))
                    ShowToast(Icons.Save, string.Format(S.SavedFile, msg.DocName));
            }
            catch (Exception) { ShowToast(Icons.Warning, S.ErrGeneric, true); }
        }

        private async void ShareDoc_Click(object sender, RoutedEventArgs e)
        {
            var msg = Msg(sender);
            if (msg == null || string.IsNullOrEmpty(msg.DocFile)) return;
            try
            {
                _shareFile = await FileTools.GetDocAsync(msg.DocFile);
                DataTransferManager.ShowShareUI();
            }
            catch (Exception) { ShowToast(Icons.Warning, S.ErrGeneric, true); }
        }
    }
}
