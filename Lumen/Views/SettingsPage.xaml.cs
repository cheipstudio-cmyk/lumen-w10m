using System;
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
        private static readonly GeminiClient Client = new GeminiClient();
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            ApiKeyBox.Password = AppSettings.ApiKey;
            ModelBox.Text = AppSettings.Model;
            TempSlider.Value = AppSettings.Temperature;
            TempValue.Text = AppSettings.Temperature.ToString("0.0");
            SearchToggle.IsOn = AppSettings.WebSearch;
            PromptBox.Text = AppSettings.SystemPrompt;
            BaseUrlBox.Text = AppSettings.BaseUrl;
            _loading = false;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            AppSettings.ApiKey = ApiKeyBox.Password;
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
                VerifyStatus.Text = Loc.Get("ErrKey");
                return;
            }

            VerifyButton.IsEnabled = false;
            VerifyRing.IsActive = true;
            VerifyStatus.Text = Loc.Get("Verifying");
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var models = await Client.ListModelsAsync(AppSettings.ApiKey, AppSettings.BaseUrl, cts.Token);
                    ModelsCombo.ItemsSource = models;
                    ModelsCombo.Visibility = models.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    VerifyStatus.Text = Loc.Format("ModelsLoaded", models.Count);
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

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.ApiKey = ApiKeyBox.Password;
        }

        private void ModelBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || ModelBox.Text == AppSettings.Model) return;
            AppSettings.Model = ModelBox.Text;
        }

        private void ModelsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var m = ModelsCombo.SelectedItem as ModelInfo;
            if (m != null) ModelBox.Text = m.Id;
        }

        private void TempSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (TempValue == null) return;
            TempValue.Text = e.NewValue.ToString("0.0");
            if (_loading) return;
            AppSettings.Temperature = e.NewValue;
        }

        private void SearchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.WebSearch = SearchToggle.IsOn;
        }

        private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || PromptBox.Text == AppSettings.SystemPrompt) return;
            AppSettings.SystemPrompt = PromptBox.Text;
        }

        private void BaseUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || BaseUrlBox.Text.Trim().TrimEnd('/') == AppSettings.BaseUrl) return;
            AppSettings.BaseUrl = BaseUrlBox.Text;
        }

        private async void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("DeleteAllTitle"),
                Content = Loc.Get("DeleteAllBody"),
                PrimaryButtonText = Loc.Get("DeleteAllConfirm"),
                SecondaryButtonText = Loc.Get("Cancel")
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            await ChatStore.DeleteAllAsync();
            // MainPage notices the new generation on its next OnNavigatedTo and resets itself.
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
