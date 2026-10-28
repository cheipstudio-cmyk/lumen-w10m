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
        private bool _loading;
        private Windows.UI.Xaml.Media.Animation.Storyboard _breath;
        private List<Tuple<string, string>> _voices = new List<Tuple<string, string>>();

        public SettingsPage()
        {
            InitializeComponent();
            Swatches.ItemsSource = ThemeManager.Palettes;
            AboutTitle.Text = "Lumen " + S.AppVersion + " by Second Dream";
            HeroTitle.Text = "Lumen " + S.AppVersion;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            ApiKeyBox.Password = AppSettings.ApiKey;
            LoadModels();
            Swatches.SelectedIndex = AppSettings.AccentIndex;
            BlackToggle.IsOn = AppSettings.PureBlack;
            TextSizeCombo.SelectedIndex = AppSettings.TextSize;
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

            for (int i = 0; i < Root.Children.Count && i < 8; i++) Fx.FadeInUp(Root.Children[i], i * 40, 18, 380);
            if (_breath == null) _breath = Fx.Breathe(HeroOrb, 0.05, 3000);

            var bytes = await FileTools.UsageAsync();
            UsageText.Text = FormatSize(bytes) + " used on this phone by chats, photos, voice notes and files.";
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Text fields are saved once on leave (TextChanged fires asynchronously and would race the loader).
            AppSettings.ApiKey = ApiKeyBox.Password;
            AppSettings.CustomPrompt = PromptBox.Text.Trim();
            AppSettings.BaseUrl = BaseUrlBox.Text;
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
            KeyStateIcon.Glyph = ok ? Icons.CheckCircle : Icons.Warning;
            KeyStateText.Text = ok ? "Connected to Gemini · " + AppSettings.ModelDisplayName : "No API key yet";
            ImageModelText.Text = string.IsNullOrEmpty(AppSettings.ImageModel)
                ? "Image creation: no image model found for this key yet. Check the key to look again."
                : "Image creation uses " + AppSettings.ImageModel + ". Free keys usually have no quota for it.";
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
            _voices = new List<Tuple<string, string>> { Tuple.Create("", "System default") };
            foreach (var v in Speech.Voices) _voices.Add(Tuple.Create(v.Id, v.DisplayName + " (" + v.Language + ")"));
            VoiceCombo.ItemsSource = _voices.Select(v => v.Item2).ToList();
            var idx = _voices.FindIndex(v => v.Item1 == AppSettings.VoiceId);
            VoiceCombo.SelectedIndex = idx < 0 ? 0 : idx;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
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

        private void Swatches_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Swatches.SelectedIndex < 0) return;
            AppSettings.AccentIndex = Swatches.SelectedIndex;
            ThemeManager.Apply();
        }

        private void BlackToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.PureBlack = BlackToggle.IsOn;
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
            AppSettings.ApiKey = ApiKeyBox.Password;
            Frame.Navigate(typeof(OnboardingPage));
        }

        private async void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
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
