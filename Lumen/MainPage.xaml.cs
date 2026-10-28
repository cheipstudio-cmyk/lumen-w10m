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
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Lumen
{
    public sealed partial class MainPage : Page, IBackAware
    {
        private static readonly GeminiClient Client = new GeminiClient();

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

        public MainPage()
        {
            InitializeComponent();
            ConversationsList.ItemsSource = _conversations;
            // handledEventsToo: the multi-line TextBox would otherwise swallow Enter.
            InputBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(InputBox_KeyDown), true);

            Suggest1.Content = Suggest1.Tag = Loc.Get("Suggest1");
            Suggest2.Content = Suggest2.Tag = Loc.Get("Suggest2");
            Suggest3.Content = Suggest3.Tag = Loc.Get("Suggest3");
            Suggest4.Content = Suggest4.Tag = Loc.Get("Suggest4");

            DataTransferManager.GetForCurrentView().DataRequested += OnShareRequested;
            StartNewConversation();
        }

        // ================================================================ navigation

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshHeader();

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

        public bool HandleBack()
        {
            if (Drawer.IsPaneOpen) { Drawer.IsPaneOpen = false; return true; }
            if (_share != null)
            {
                try { _share.ReportCompleted(); } catch (Exception) { }
                return true;
            }
            return false;
        }

        private void RefreshHeader()
        {
            ModelLabel.Text = AppSettings.Model + (AppSettings.WebSearch ? "  ·  Google Search" : "");
            NoKeyCard.Visibility = AppSettings.HasApiKey ? Visibility.Collapsed : Visibility.Visible;
            SuggestionsPanel.Visibility = AppSettings.HasApiKey ? Visibility.Visible : Visibility.Collapsed;
            GreetingText.Text = Greeting();
        }

        private static string Greeting()
        {
            var h = DateTime.Now.Hour;
            if (h >= 5 && h < 12) return Loc.Get("GreetMorning");
            if (h >= 12 && h < 18) return Loc.Get("GreetAfternoon");
            return Loc.Get("GreetEvening");
        }

        // ================================================================ conversations

        private void StartNewConversation()
        {
            _conv = new Conversation();
            _conv.Info.Title = Loc.Get("NewChatTitle");
            MessagesList.ItemsSource = _conv.Messages;
            ConversationsList.SelectedItem = null;
            UpdateEmptyState();
        }

        private async Task OpenConversationAsync(ConversationInfo info)
        {
            if (_busy) return;
            if (_conv != null && _conv.Info.Id == info.Id) return;
            _conv = await ChatStore.LoadAsync(info);
            MessagesList.ItemsSource = _conv.Messages;
            UpdateEmptyState();
            ScrollToEnd(false);
        }

        private void UpdateEmptyState()
        {
            EmptyState.Visibility = _conv.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        private static string MakeTitle(string text)
        {
            var t = (text ?? "").Replace('\n', ' ').Trim();
            if (t.Length == 0) return Loc.Get("ImageOnly");
            return t.Length > 42 ? t.Substring(0, 42).TrimEnd() + "…" : t;
        }

        // ================================================================ sending

        private async Task SendAsync(string text)
        {
            if (_busy) return;
            text = (text ?? "").Trim();
            if (text.Length == 0 && _pendingImage == null) return;

            if (!AppSettings.HasApiKey)
            {
                Frame.Navigate(typeof(SettingsPage));
                return;
            }

            if (_conv.Messages.Count == 0) _conv.Info.Title = MakeTitle(text);

            _conv.Messages.Add(new ChatMessage { Role = ChatMessage.RoleUser, Text = text, ImageFile = _pendingImage });
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

            // Push text to the UI at ~15 fps instead of on every chunk: smooth on a Lumia 550.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
            timer.Tick += (s, e) =>
            {
                string snapshot = null;
                lock (buffer)
                {
                    if (dirty) { snapshot = buffer.ToString(); dirty = false; }
                }
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
            bool search = AppSettings.WebSearch;

            try
            {
                var result = await Task.Run(() => Client.StreamAsync(apiKey, baseUrl, model, system, temp, search, history,
                    delta => { lock (buffer) { buffer.Append(delta); dirty = true; } }, cts.Token));

                string final;
                lock (buffer) final = buffer.ToString();
                if (final.Trim().Length == 0)
                {
                    reply.IsError = true;
                    reply.Text = !string.IsNullOrEmpty(result.BlockReason) || result.FinishReason == "SAFETY" || result.FinishReason == "PROHIBITED_CONTENT"
                        ? Loc.Get("ErrBlocked")
                        : Loc.Get("ErrEmpty");
                }
                else
                {
                    if (result.FinishReason == "MAX_TOKENS") final += "\n\n_" + Loc.Get("Truncated") + "_";
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
                    reply.Text = partial + "\n\n_" + (cancelled ? Loc.Get("Stopped") : GeminiClient.Describe(ex)) + "_";
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
            SendGlyph.Glyph = busy ? "" : "";
            AttachButton.IsEnabled = !busy;
            UpdateSendEnabled();
        }

        private void UpdateSendEnabled()
        {
            SendButton.IsEnabled = _busy || InputBox.Text.Trim().Length > 0 || _pendingImage != null;
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
            sv.ChangeView(null, sv.ScrollableHeight, null, !animate);
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
                _pendingImage = name;
                var bmp = new BitmapImage { DecodePixelWidth = 144 };
                bmp.UriSource = new Uri("ms-appdata:///local/images/" + name);
                AttachmentImage.Source = bmp;
                AttachmentPreview.Visibility = Visibility.Visible;
                UpdateSendEnabled();
            }
            catch (Exception)
            {
                ShowToast(Loc.Get("ImageError"));
            }
        }

        private void ClearAttachment()
        {
            _pendingImage = null;
            AttachmentImage.Source = null;
            AttachmentPreview.Visibility = Visibility.Collapsed;
            UpdateSendEnabled();
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
            UpdateSendEnabled();
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
            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
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
            UpdateSendEnabled();
        }

        private async void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            await SendAsync(b.Tag as string);
        }

        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = await ImageHelper.PickAsync();
                await AttachAsync(file);
            }
            catch (Exception)
            {
                ShowToast(Loc.Get("ImageError"));
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

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            Drawer.IsPaneOpen = false;
            if (_busy) return;
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
            ShowToast(Loc.Get("Copied"));
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

        private void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as ChatMessage;
            if (msg == null || _busy) return;
            var idx = _conv.Messages.IndexOf(msg);
            if (idx < 0) return;
            while (_conv.Messages.Count > idx) _conv.Messages.RemoveAt(_conv.Messages.Count - 1);
            InputBox.Text = msg.Text;
            InputBox.SelectionStart = InputBox.Text.Length;
            if (!string.IsNullOrEmpty(msg.ImageFile))
            {
                _pendingImage = msg.ImageFile;
                var bmp = new BitmapImage { DecodePixelWidth = 144 };
                bmp.UriSource = new Uri("ms-appdata:///local/images/" + msg.ImageFile);
                AttachmentImage.Source = bmp;
                AttachmentPreview.Visibility = Visibility.Visible;
            }
            UpdateEmptyState();
            UpdateSendEnabled();
            InputBox.Focus(FocusState.Programmatic);
        }
    }
}
