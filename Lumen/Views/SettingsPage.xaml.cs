using System;
using System.Linq;
using System.Threading;
using Lumen.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace Lumen.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
            Swatches.ItemsSource = ThemeManager.Palettes;
            AboutText.Text = "Lumen " + S.AppVersion + " by Second Dream. Unofficial client, not affiliated with Google. Gemini is a trademark of Google LLC.";
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
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
            TempSlider.Value = AppSettings.Temperature;
            TempValue.Text = AppSettings.Temperature.ToString("0.0");
            PromptBox.Text = AppSettings.CustomPrompt;
            BaseUrlBox.Text = AppSettings.BaseUrl;
            _loading = false;

            for (int i = 0; i < Root.Children.Count && i < 12; i++) Fx.FadeInUp(Root.Children[i], i * 30, 14, 360);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Text fields are saved once on leave (TextChanged fires asynchronously and would race the loader).
            AppSettings.ApiKey = ApiKeyBox.Password;
            AppSettings.CustomPrompt = PromptBox.Text.Trim();
            AppSettings.BaseUrl = BaseUrlBox.Text;
        }

        private void LoadModels()
        {
            var models = AppSettings.Models;
            var current = AppSettings.Model;
            if (!models.Any(m => m.Id == current)) models.Insert(0, new ModelInfo { Id = current, DisplayName = current });
            ModelsCombo.ItemsSource = models;
            ModelsCombo.SelectedIndex = models.FindIndex(m => m.Id == current);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
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
                    var models = await GeminiClient.Shared.ListModelsAsync(AppSettings.ApiKey, BaseUrlBox.Text.Trim().Length > 0 ? BaseUrlBox.Text.Trim().TrimEnd('/') : AppSettings.BaseUrl, cts.Token);
                    if (models.Count > 0)
                    {
                        AppSettings.Models = models;
                        if (!models.Any(m => m.Id == AppSettings.Model)) AppSettings.Model = models[0].Id;
                    }
                    _loading = true;
                    LoadModels();
                    _loading = false;
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
            if (m != null) AppSettings.Model = m.Id;
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
            // MainPage notices the new generation on its next OnNavigatedTo and resets itself.
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
