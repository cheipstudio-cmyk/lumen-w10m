using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lumen.Core;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace Lumen.Views
{
    public sealed partial class SettingsPage : Page, IBackAware
    {
        private string _shownProvider;
        private string _section;
        private System.Collections.Generic.List<AccentPalette> _swatches;
        private bool _loading;
        private List<Tuple<string, string>> _voices = new List<Tuple<string, string>>();

        public SettingsPage()
        {
            InitializeComponent();
            _swatches = ThemeManager.NewSwatches();
            Swatches.ItemsSource = _swatches;
            AboutTitle.Text = string.Format(S.AboutTitle, S.AppVersion);
            AboutSubtitle.Text = AboutTitle.Text;
            ProviderCombo.ItemsSource = Providers.All;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            ProviderCombo.SelectedIndex = Providers.All.FindIndex(p => p.Id == AppSettings.ProviderId);
            ShowProvider(AppSettings.ProviderId);
            ThemeManager.SyncSelection(_swatches);
            ThemeToggle.IsOn = AppSettings.LightTheme;
            TextSizeCombo.ItemsSource = new List<string> { S.Small, S.Normal, S.Large };
            TextSizeCombo.SelectedIndex = AppSettings.TextSize;
            LoadLanguages();
            AnimToggle.IsOn = AppSettings.Animations;
            WikiToggle.IsOn = AppSettings.Wikipedia;
            CreativeToggle.IsOn = AppSettings.Creative;
            TempSlider.Value = AppSettings.Temperature;
            TempValue.Text = AppSettings.Temperature.ToString("0.0");
            PromptBox.Text = AppSettings.CustomPrompt;
            LoadVoices();
            UpdateKeyState();
            _loading = false;

            ShowSection(e.Parameter as string, false);

            var bytes = await FileTools.UsageAsync();
            UsageText.Text = string.Format(S.StorageUsed, FormatSize(bytes));
            UsageDetail.Text = UsageText.Text;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Text fields are saved once on leave (TextChanged fires asynchronously and would race the loader).
            SaveFields();
        }

        private static string FormatSize(ulong b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            return (b / (1024.0 * 1024)).ToString("0.#") + " MB";
        }

        // ------------------------------------------------------------ sections (W10M Settings: list, then a page per category)

        public bool HandleBack()
        {
            if (_section == null) return false;
            ShowSection(null, true);
            return true;
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            ShowSection((string)((FrameworkElement)sender).Tag, true);
        }

        private void ShowSection(string id, bool animate)
        {
            SaveFields();
            UpdateKeyState();
            _section = id;
            var map = new Dictionary<string, Tuple<FrameworkElement, string>>
            {
                { "ai", Tuple.Create<FrameworkElement, string>(AiSection, S.SecAi) },
                { "look", Tuple.Create<FrameworkElement, string>(LookSection, S.SecLook) },
                { "assistant", Tuple.Create<FrameworkElement, string>(AssistantSection, S.SecAssistant) },
                { "storage", Tuple.Create<FrameworkElement, string>(StorageSection, S.SecStorage) },
                { "about", Tuple.Create<FrameworkElement, string>(AboutSection, S.SecAbout) },
            };
            if (id != null && !map.ContainsKey(id)) id = _section = null;
            Home.Visibility = id == null ? Visibility.Visible : Visibility.Collapsed;
            foreach (var kv in map) kv.Value.Item1.Visibility = kv.Key == id ? Visibility.Visible : Visibility.Collapsed;
            HeaderText.Text = (id == null ? S.SettingsTitle : map[id].Item2).ToUpperInvariant();
            if (animate) Fx.FadeInUp(id == null ? (UIElement)Home : map[id].Item1, 0, 18, 260);
        }

        // ------------------------------------------------------------ AI service

        private void UpdateKeyState()
        {
            var ok = AppSettings.HasApiKey;
            KeyStateText.Text = ok
                ? AppSettings.Provider.Name + " · " + AppSettings.ModelDisplayName
                : S.NoKeyYet;
            var img = FileTools.ImageProvider;
            ImageModelText.Text = img == null || string.IsNullOrEmpty(AppSettings.ImageModelFor(img))
                ? (img == null ? S.ImageModelNone : "")
                : string.Format(S.ImageModelUses, AppSettings.ImageModelFor(img));
        }

        /// <summary>Fills key, endpoint and models for one provider (and makes it the active one).</summary>
        private void ShowProvider(string id)
        {
            var p = Providers.Find(id);
            _shownProvider = id;
            ApiKeyBox.Password = AppSettings.KeyFor(id);
            ApiKeyBox.PlaceholderText = p.KeyPlaceholder;
            BaseUrlBox.Text = AppSettings.BaseUrlFor(id);
            BaseUrlBox.PlaceholderText = id == "custom" ? "https://openrouter.ai/api/v1" : p.DefaultBaseUrl;
            EndpointHint.Text = id == "custom" ? S.EndpointCustomHint : S.EndpointHint;
            GetKeyLink.Visibility = string.IsNullOrEmpty(p.KeyUrl) ? Visibility.Collapsed : Visibility.Visible;
            ProviderHint.Text = id == "gemini" ? S.HintGemini : id == "anthropic" ? S.HintClaude : id == "openai" ? S.HintOpenAI : S.HintCustom;
            VerifyStatus.Text = "";
            LoadModels();
        }

        private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var p = ProviderCombo.SelectedItem as Provider;
            if (p == null || p.Id == _shownProvider) return;
            SaveFields();
            AppSettings.ProviderId = p.Id;
            _loading = true;
            ShowProvider(p.Id);
            _loading = false;
            UpdateKeyState();
        }

        private void LoadModels()
        {
            var models = AppSettings.ModelsFor(_shownProvider);
            var current = AppSettings.ModelFor(_shownProvider);
            if (!models.Any(m => m.Id == current)) models.Insert(0, new ModelInfo { Id = current, DisplayName = current });
            ModelsCombo.ItemsSource = models;
            ModelsCombo.SelectedIndex = models.FindIndex(m => m.Id == current);
        }

        private void LoadVoices()
        {
            _voices = new List<Tuple<string, string>> { Tuple.Create("", S.SystemDefault) };
            foreach (var v in Speech.Voices) _voices.Add(Tuple.Create(v.Id, v.DisplayName + " (" + v.Language + ")"));
            VoiceCombo.ItemsSource = _voices.Select(v => v.Item2).ToList();
            var idx = _voices.FindIndex(v => v.Item1 == AppSettings.VoiceId);
            VoiceCombo.SelectedIndex = idx < 0 ? 0 : idx;
        }

        private void LoadLanguages()
        {
            var names = new List<string> { S.SystemDefault };
            names.AddRange(App.Languages.Select(l => l.Item2));
            LanguageCombo.ItemsSource = names;
            var ov = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            var idx = string.IsNullOrEmpty(ov) ? 0 : App.Languages.FindIndex(l => l.Item1 == ov) + 1;
            LanguageCombo.SelectedIndex = Math.Max(0, idx);
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || LanguageCombo.SelectedIndex < 0) return;
            var code = LanguageCombo.SelectedIndex == 0 ? "" : App.Languages[LanguageCombo.SelectedIndex - 1].Item1;
            if (code == Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride) return;
            if (MainPage.IsBusy)
            {
                // Rebuilding the UI mid-answer would lose the chat: wait for Lumen to finish.
                _loading = true;
                LoadLanguages();
                _loading = false;
                return;
            }
            SaveFields();
            App.SetLanguage(code);
            App.ReloadUi(typeof(SettingsPage), "look");
        }

        private void SaveFields()
        {
            if (_shownProvider == null) return;
            AppSettings.SetKeyFor(_shownProvider, ApiKeyBox.Password);
            var url = BaseUrlBox.Text.Trim().TrimEnd('/');
            // Saving the default as an override would pin it forever: store only real changes.
            AppSettings.SetBaseUrlFor(_shownProvider, url == Providers.Find(_shownProvider).DefaultBaseUrl ? "" : url);
            AppSettings.CustomPrompt = PromptBox.Text.Trim();
        }

        private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (MainPage.IsBusy)
            {
                _loading = true;
                ThemeToggle.IsOn = AppSettings.LightTheme;
                _loading = false;
                return;
            }
            AppSettings.LightTheme = ThemeToggle.IsOn;
            ThemeManager.Apply();
            SaveFields();
            // System controls follow RequestedTheme on the root frame: rebuild it.
            App.ReloadUi(typeof(SettingsPage), "look");
        }

        private async void GetKey_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri(Providers.Find(_shownProvider).KeyUrl));
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            SaveFields();
            var id = _shownProvider;
            if (!AppSettings.HasKeyFor(id) && id != "custom")
            {
                VerifyStatus.Text = S.KeyMissing;
                return;
            }
            if (!AppSettings.HasBaseUrlFor(id))
            {
                VerifyStatus.Text = S.ErrNoEndpoint;
                return;
            }

            VerifyButton.IsEnabled = false;
            VerifyRing.IsActive = true;
            VerifyStatus.Text = S.Verifying;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var models = await Ai.ListModelsAsync(id, AppSettings.KeyFor(id), AppSettings.BaseUrlFor(id), cts.Token);
                    if (models.Count > 0)
                    {
                        AppSettings.SetModelsFor(id, models);
                        if (!models.Any(m => m.Id == AppSettings.ModelFor(id))) AppSettings.SetModelFor(id, models[0].Id);
                    }
                    _loading = true;
                    LoadModels();
                    _loading = false;
                    UpdateKeyState();
                    VerifyStatus.Text = "✓  " + string.Format(S.ModelsLoaded, models.Count);
                    Fx.Pop(VerifyStatus, 0.8, 300);
                }
            }
            catch (Exception ex)
            {
                VerifyStatus.Text = GeminiClient.Describe(ex);
            }
            finally
            {
                VerifyButton.IsEnabled = true;
                VerifyRing.IsActive = false;
            }
        }

        private void ModelsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var m = ModelsCombo.SelectedItem as ModelInfo;
            if (m != null) { AppSettings.SetModelFor(_shownProvider, m.Id); UpdateKeyState(); }
        }

        private void Swatches_ItemClick(object sender, ItemClickEventArgs e)
        {
            var p = e.ClickedItem as AccentPalette;
            if (p == null) return;
            AppSettings.AccentIndex = _swatches.IndexOf(p);
            ThemeManager.SyncSelection(_swatches);
            ThemeManager.Apply();
        }

        private void TextSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TextSizeCombo.SelectedIndex < 0) return;
            AppSettings.TextSize = TextSizeCombo.SelectedIndex;
        }

        private void AnimToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Animations = AnimToggle.IsOn;
        }

        private void WikiToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Wikipedia = WikiToggle.IsOn;
        }

        private void CreativeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Creative = CreativeToggle.IsOn;
        }

        private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || VoiceCombo.SelectedIndex < 0 || VoiceCombo.SelectedIndex >= _voices.Count) return;
            AppSettings.VoiceId = _voices[VoiceCombo.SelectedIndex].Item1;
        }

        private void TempSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (TempValue == null) return;
            TempValue.Text = e.NewValue.ToString("0.0");
            if (_loading) return;
            AppSettings.Temperature = e.NewValue;
        }

        private void Tour_Click(object sender, RoutedEventArgs e)
        {
            if (MainPage.IsBusy) return;
            SaveFields();
            Frame.Navigate(typeof(OnboardingPage));
        }

        private async void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                RequestedTheme = ThemeManager.Theme,
                Title = S.DeleteAllTitle,
                Content = S.DeleteAllBody,
                PrimaryButtonText = S.Delete,
                SecondaryButtonText = S.Cancel
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            await ChatStore.DeleteAllAsync();
            TileHelper.Clear();
            // MainPage notices the new generation on its next OnNavigatedTo and resets itself.
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
