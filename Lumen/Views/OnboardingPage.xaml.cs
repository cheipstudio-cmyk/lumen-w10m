using System;
using System.Threading;
using Lumen.Core;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Lumen.Views
{
    public sealed partial class OnboardingPage : Page
    {
        private bool _ready;

        public OnboardingPage()
        {
            InitializeComponent();
            Swatches.ItemsSource = ThemeManager.Palettes;
            Loaded += OnLoaded;
            Unloaded += (s, e) => { ScreenLoop.Stop(); FloatLoop.Stop(); };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            KeyBox.Password = AppSettings.ApiKey;
            Swatches.SelectedIndex = AppSettings.AccentIndex;
            BlackToggle.IsOn = AppSettings.PureBlack;
            _ready = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ScreenLoop.Begin();
            if (Fx.Enabled)
            {
                FloatLoop.Begin();
            }
            else
            {
                // Static frame: the full conversation, no motion.
                ScreenLoop.Seek(TimeSpan.FromSeconds(5));
                ScreenLoop.Pause();
            }
            Fx.FadeInUp(Stage, 0, 40, 700);
            Fx.FadeInUp(HeroText, 250, 24, 600);
        }

        // ------------------------------------------------------------ paging

        private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // FlipView can select its first item while InitializeComponent is still running.
            if (Dots == null || NextText == null || SkipButton == null) return;
            var i = Pages.SelectedIndex;
            for (int d = 0; d < Dots.Children.Count; d++)
            {
                var dot = (Border)Dots.Children[d];
                var active = d == i;
                dot.Width = active ? 22 : 8;
                dot.Background = (Brush)Application.Current.Resources[active ? "AccentGradient" : "StrokeBrush"];
            }
            var last = i == Pages.Items.Count - 1;
            NextText.Text = last ? "Start" : "Next";
            SkipButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;

            // Entrance for the page we just landed on.
            if (i == 1) StaggerChildren(FeaturesPanel);
            else if (i == 2) Fx.FadeInUp(KeyPanel, 0, 24, 420);
            else if (i == 3) Fx.FadeInUp(ThemePanel, 0, 24, 420);
        }

        private static void StaggerChildren(Panel panel)
        {
            for (int c = 0; c < panel.Children.Count; c++) Fx.FadeInUp(panel.Children[c], c * 90, 26, 460);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            Fx.Pop(NextButton, 0.9, 260);
            if (Pages.SelectedIndex < Pages.Items.Count - 1) Pages.SelectedIndex++;
            else Finish();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Finish();
        }

        private void Finish()
        {
            if (KeyBox.Password.Trim().Length > 0) AppSettings.ApiKey = KeyBox.Password;
            AppSettings.Onboarded = true;
            Frame.Navigate(typeof(MainPage));
            Frame.BackStack.Clear();
        }

        // ------------------------------------------------------------ key

        private async void OpenStudio_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://aistudio.google.com/apikey"));
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            var key = KeyBox.Password.Trim();
            if (key.Length == 0) { VerifyStatus.Text = S.KeyMissing; return; }

            VerifyButton.IsEnabled = false;
            VerifyRing.IsActive = true;
            VerifyStatus.Text = S.Verifying;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var models = await GeminiClient.Shared.ListModelsAsync(key, AppSettings.BaseUrl, cts.Token);
                    AppSettings.ApiKey = key;
                    if (models.Count > 0) AppSettings.Models = models;
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

        // ------------------------------------------------------------ theme

        private void Swatches_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || Swatches.SelectedIndex < 0) return;
            AppSettings.AccentIndex = Swatches.SelectedIndex;
            ThemeManager.Apply();
        }

        private void BlackToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            AppSettings.PureBlack = BlackToggle.IsOn;
            ThemeManager.Apply();
        }
    }
}
