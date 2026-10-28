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
    public sealed partial class SettingsPage : Page
    {
        private System.Collections.Generic.List<AccentPalette> _swatches;
        private bool _loading;
        private List<Tuple<string, string>> _voices = new List<Tuple<string, string>>();

        public SettingsPage()
        {
            InitializeComponent();
            _swatches = ThemeManager.NewSwatches();
            Swatches.ItemsSource = _swatches;
            AboutTitle.Text = string.Format(S.AboutTitle, S.AppVersion);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            ApiKeyBox.Password = AppSettings.ApiKey;
            LoadModels();
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
            BaseUrlBox.Text = AppSettings.BaseUrl;
            LoadVoices();
            UpdateKeyState();
            _loading = false;

            // Light entrance: just the title block.
            for (int i = 0; i < Root.Children.Count && i < 3; i++) Fx.FadeInUp(Root.Children[i], i * 40, 14, 300);

            var bytes = await FileTools.UsageAsync();
            UsageText.Text = string.Format(S.StorageUsed, FormatSize(bytes));
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

        private void UpdateKeyState()
        {
            var ok = AppSettings.HasApiKey;
            KeyStateIcon.UriSource = ok ? Icons.CheckCircle : Icons.Warning;
            KeyStateText.Text = ok ? string.Format(S.KeyConnected, AppSettings.ModelDisplayName) : S.NoKeyYet;
            ImageModelText.Text = string.IsNullOrEmpty(AppSettings.ImageModel)
                ? S.ImageModelNone
                : string.Format(S.ImageModelUses, AppSettings.ImageModel);
        }

        private void LoadModels()
        {
            var models = AppSettings.Models;
            var current = AppSettings.Model;
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
            App.ReloadUi(typeof(SettingsPage));
        }

        private void SaveFields()
        {
            AppSettings.ApiKey = ApiKeyBox.Password;
            AppSettings.CustomPrompt = PromptBox.Text.Trim();
            AppSettings.BaseUrl = BaseUrlBox.Text;
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
            App.ReloadUi(typeof(SettingsPage));
        }

        private async void GetKey_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://aistudio.google.com/apikey"));
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.ApiKey = ApiKeyBox.Password;
            if (!AppSettings.HasApiKey)
            {
                VerifyStatus.Text = S.KeyMissing;
                return;
            }

            VerifyButton.IsEnabled = false;
            VerifyRing.IsActive = true;
            VerifyStatus.Text = S.Verifying;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var baseUrl = BaseUrlBox.Text.Trim().Length > 0 ? BaseUrlBox.Text.Trim().TrimEnd('/') : AppSettings.BaseUrl;
                    var models = await GeminiClient.Shared.ListModelsAsync(AppSettings.ApiKey, baseUrl, cts.Token);
                    if (models.Count > 0)
                    {
                        AppSettings.Models = models;
                        if (!models.Any(m => m.Id == AppSettings.Model)) AppSettings.Model = models[0].Id;
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
            if (m != null) { AppSettings.Model = m.Id; UpdateKeyState(); }
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
