using System;
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
        private ScrollViewer _scroller;
        private DispatcherTimer _toastTimer;

        // voice
        private readonly AudioRecorder _recorder = new AudioRecorder();
        private DispatcherTimer _recordTimer;
        private Storyboard _recordPulse;
        private MediaPlayer _player;
        private ChatMessage _playing;

        private Storyboard _orbBreath;

        public MainPage()
        {
            InitializeComponent();
            ConversationsList.ItemsSource = _conversations;
            // handledEventsToo: the multi-line TextBox would otherwise swallow Enter.
            InputBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(InputBox_KeyDown), true);

            Suggest1.Content = Suggest1.Tag = S.Suggest1;
            Suggest2.Content = Suggest2.Tag = S.Suggest2;
            Suggest3.Content = Suggest3.Tag = S.Suggest3;
            Suggest4.Content = Suggest4.Tag = S.Suggest4;

            DataTransferManager.GetForCurrentView().DataRequested += OnShareRequested;
            StartNewConversation();
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
            if (Drawer.IsPaneOpen) { Drawer.IsPaneOpen = false; return true; }
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
                ? new TransitionCollection { new AddDeleteThemeTransition(), new EntranceThemeTransition { FromVerticalOffset = 36, IsStaggeringEnabled = true } }
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
            UpdateEmptyState();
        }

        private async Task OpenConversationAsync(ConversationInfo info)
        {
            if (_busy) return;
            if (_conv != null && _conv.Info.Id == info.Id) return;
            StopPlayback();
            _conv = await ChatStore.LoadAsync(info);
            MessagesList.ItemsSource = _conv.Messages;
            UpdateEmptyState();
            ScrollToEnd(false);
        }

        private void UpdateEmptyState()
        {
            var empty = _conv.Messages.Count == 0;
            var wasVisible = EmptyState.Visibility == Visibility.Visible;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

            if (empty && (!wasVisible || _orbBreath == null))
            {
                // Greeting slides in, chips cascade, the orb breathes.
                Fx.FadeInUp(GreetingText, 0, 20, 500);
                Fx.FadeInUp(EmptySubtitle, 90, 20, 500);
                for (int i = 0; i < SuggestionsPanel.Children.Count; i++) Fx.FadeInUp(SuggestionsPanel.Children[i], 200 + i * 80, 24, 480);
                if (_orbBreath == null) _orbBreath = Fx.Breathe(EmptyOrb);
                else _orbBreath.Begin();
            }
            else if (!empty && _orbBreath != null)
            {
                _orbBreath.Stop();
            }
        }

        private async Task PersistAsync()
        {
            var info = _conv.Info;
            info.Updated = DateTimeOffset.Now;
            var idx = _conversations.IndexOf(info);
            if (idx < 0) _conversations.Insert(0, info);
            else if (idx > 0) _conversations.Move(idx, 0);
            ConversationsList.SelectedItem = info;
            await ChatStore.SaveAsync(_conv);
            await ChatStore.SaveIndexAsync(_conversations);
        }

        private static string MakeTitle(string text, bool hasImage, bool hasAudio)
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

            if (_conv.Messages.Count == 0) _conv.Info.Title = MakeTitle(text, _pendingImage != null, audioFile != null);

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
            var buffer = new StringBuilder();
            var dirty = false;
            string status = null;
            var statusDirty = false;

            // Push text to the UI at ~15 fps instead of on every chunk: smooth on a Lumia 735.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
            timer.Tick += (s, e) =>
            {
                string snapshot = null, st = null;
                bool stChanged;
                lock (buffer)
                {
                    if (dirty) { snapshot = buffer.ToString(); dirty = false; }
                    stChanged = statusDirty;
                    st = status;
                    statusDirty = false;
                }
                if (stChanged) reply.Status = st;
                if (snapshot != null)
                {
                    var stick = IsNearBottom();
                    reply.Text = snapshot;
                    if (stick) ScrollToEnd(false);
                }
            };
            timer.Start();

            string apiKey = AppSettings.ApiKey, baseUrl = AppSettings.BaseUrl, model = AppSettings.Model, system = AppSettings.SystemPrompt;
            double temp = AppSettings.Temperature;
            bool wiki = AppSettings.Wikipedia;

            try
            {
                var result = await Task.Run(() => Client.StreamAsync(apiKey, baseUrl, model, system, temp, wiki, history,
                    delta => { lock (buffer) { buffer.Append(delta); dirty = true; } },
                    query => { lock (buffer) { status = query == null ? null : string.Format(S.LookingUp, query); statusDirty = true; } },
                    cts.Token));

                string final;
                lock (buffer) final = buffer.ToString();
                if (final.Trim().Length == 0)
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
                }
            }
            catch (Exception ex)
            {
                string partial;
                lock (buffer) partial = buffer.ToString();
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
            AttachButton.IsEnabled = !busy;
            UpdateSendState();
        }

        /// <summary>One button, four jobs: stop (answering), send (recording or something typed), mic (empty).</summary>
        private void UpdateSendState()
        {
            string glyph;
            if (_busy) glyph = "";
            else if (_recorder.IsRecording) glyph = "";
            else if (InputBox.Text.Trim().Length > 0 || _pendingImage != null) glyph = "";
            else glyph = "";

            if (SendGlyph.Glyph != glyph)
            {
                SendGlyph.Glyph = glyph;
                Fx.Pop(SendGlyph, 0.6, 260);
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
                ShowToast(S.MicDenied);
                return;
            }
            catch (Exception)
            {
                ShowToast(S.MicError);
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
            if (result == null) { ShowToast(S.TooShort); return; }
            await SendAsync(InputBox.Text, result.Item1, result.Item2);
        }

        private void StopPlayback()
        {
            if (_player != null) _player.Pause();
            if (_playing != null) _playing.IsPlaying = false;
            _playing = null;
        }

        private async void PlayAudio_Click(object sender, RoutedEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as ChatMessage;
            if (msg == null || string.IsNullOrEmpty(msg.AudioFile)) return;

            if (_playing == msg) { StopPlayback(); return; }
            StopPlayback();

            if (_player == null)
            {
                _player = new MediaPlayer { AutoPlay = false };
                _player.MediaEnded += async (s, a) =>
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, StopPlayback);
            }
            try
            {
                var folder = await AudioRecorder.AudioFolderAsync();
                var file = await folder.GetFileAsync(msg.AudioFile);
                _player.Source = MediaSource.CreateFromStorageFile(file);
                _player.Play();
                _playing = msg;
                msg.IsPlaying = true;
            }
            catch (Exception)
            {
                ShowToast(S.ErrGeneric);
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
            sv.UpdateLayout();
            sv.ChangeView(null, sv.ScrollableHeight, null, !animate || !Fx.Enabled);
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
                ShowToast(S.ImageError);
            }
        }

        private void SetPendingImage(string name)
        {
            _pendingImage = name;
            var bmp = new BitmapImage { DecodePixelWidth = 144 };
            bmp.UriSource = new Uri("ms-appdata:///local/images/" + name);
            AttachmentImage.Source = bmp;
            AttachmentPreview.Visibility = Visibility.Visible;
            Fx.Pop(AttachmentPreview, 0.7, 320);
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
            if (string.IsNullOrEmpty(_shareText)) return;
            args.Request.Data.Properties.Title = "Lumen";
            args.Request.Data.SetText(_shareText);
        }

        // ================================================================ toast

        private void ShowToast(string text)
        {
            ToastText.Text = text;
            Toast.Visibility = Visibility.Visible;
            Fx.FadeInUp(Toast, 0, -10, 240);
            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
                _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        // ================================================================ UI events

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
                ShowToast(S.CameraError);
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
                ShowToast(S.ImageError);
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            ClearAttachment();
        }

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            Drawer.IsPaneOpen = !Drawer.IsPaneOpen;
        }

        private void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            // Quick switch without going to Settings.
            var flyout = new MenuFlyout();
            var current = AppSettings.Model;
            foreach (var m in AppSettings.Models)
            {
                var item = new ToggleMenuFlyoutItem { Text = m.DisplayName, IsChecked = m.Id == current, Tag = m.Id };
                item.Click += (s, a) =>
                {
                    AppSettings.Model = (string)((FrameworkElement)s).Tag;
                    ModelLabel.Text = AppSettings.ModelDisplayName;
                };
                flyout.Items.Add(item);
            }
            flyout.ShowAt(ModelButton);
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            Drawer.IsPaneOpen = false;
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
            Drawer.IsPaneOpen = false;
            var info = e.ClickedItem as ConversationInfo;
            if (info != null) await OpenConversationAsync(info);
        }

        private async void DeleteConversation_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement)?.DataContext as ConversationInfo;
            if (info == null) return;
            if (_busy && _conv.Info == info) return;
            _conversations.Remove(info);
            await ChatStore.DeleteAsync(info);
            await ChatStore.SaveIndexAsync(_conversations);
            if (_conv.Info.Id == info.Id) StartNewConversation();
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as ChatMessage;
            if (msg == null || string.IsNullOrEmpty(msg.Text)) return;
            var dp = new DataPackage();
            dp.SetText(msg.Text);
            Clipboard.SetContent(dp);
            ShowToast(S.Copied);
        }

        private void ShareMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as ChatMessage;
            if (msg == null || string.IsNullOrEmpty(msg.Text)) return;
            _shareText = msg.Text;
            DataTransferManager.ShowShareUI();
        }

        private async void Regenerate_Click(object sender, RoutedEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as ChatMessage;
            if (msg == null || _busy) return;
            var idx = _conv.Messages.IndexOf(msg);
            if (idx < 0) return;
            // Drop this answer and everything after it, then ask again.
            while (_conv.Messages.Count > idx) _conv.Messages.RemoveAt(_conv.Messages.Count - 1);
            if (_conv.Messages.Count == 0) { UpdateEmptyState(); return; }
            await RunModelAsync();
        }

        private async void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as ChatMessage;
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
    }
}
