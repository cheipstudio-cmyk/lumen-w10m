using System;
using System.Linq;
using System.Threading;
using Lumen.Core;
using Windows.System;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Navigation;

namespace Lumen.Views
{
    public sealed partial class OnboardingPage : Page
    {
        private System.Collections.Generic.List<AccentPalette> _swatches;
        private const int HeroPage = 1;
        private bool _ready;
        private int _startPage;

        public OnboardingPage()
        {
            InitializeComponent();
            _swatches = ThemeManager.NewSwatches();
            Swatches.ItemsSource = _swatches;
            ProviderCombo.ItemsSource = Providers.All;
            BuildLanguageList();
            Unloaded += (s, e) => { try { HeroVideo.Stop(); } catch (Exception) { } };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ProviderCombo.SelectedIndex = Providers.All.FindIndex(p => p.Id == AppSettings.ProviderId);
            ShowProvider(AppSettings.ProviderId);
            ThemeManager.SyncSelection(_swatches);
            ThemeToggle.IsOn = AppSettings.LightTheme;
            _startPage = e.Parameter is int ? (int)e.Parameter : 0;
            _ready = true;
            if (_startPage > 0) Pages.SelectedIndex = _startPage;
            UpdateChrome();
            if (_startPage == 0)
            {
                for (int i = 0; i < LangList.Children.Count; i++) Fx.FadeInUp(LangList.Children[i], 120 + i * 60, 22, 420);
            }
        }

        // ------------------------------------------------------------ language (page 0)

        private void BuildLanguageList()
        {
            // W10M setup style list: plain rows, a check next to the current language.
            var current = App.CurrentLanguage;
            foreach (var lang in App.Languages)
            {
                var selected = lang.Item1 == current;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = lang.Item2,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiLight,
                    Foreground = (Brush)Application.Current.Resources[selected ? "AccentTextBrush" : "TextPrimaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (selected)
                {
                    var check = new BitmapIcon
                    {
                        UriSource = Icons.Check,
                        Width = 18,
                        Height = 18,
                        Foreground = (Brush)Application.Current.Resources["AccentTextBrush"]
                    };
                    Grid.SetColumn(check, 1);
                    row.Children.Add(check);
                }
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["ChipButton"],
                    Content = row,
                    Padding = new Thickness(12, 10, 16, 10),
                    Tag = lang.Item1
                };
                button.Click += Language_Click;
                LangList.Children.Add(button);
            }
        }

        private void Language_Click(object sender, RoutedEventArgs e)
        {
            var code = (string)((Button)sender).Tag;
            if (code == App.CurrentLanguage && App.HasLanguageOverride)
            {
                Pages.SelectedIndex = HeroPage;
                return;
            }
            SaveProvider();
            App.SetLanguage(code);
            // Rebuild the onboarding in the new language, straight on the 3D Lumia page.
            App.ReloadUi(typeof(OnboardingPage), HeroPage);
        }

        // ------------------------------------------------------------ paging

        private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // FlipView can select its first item while InitializeComponent is still running.
            if (!_ready || Dots == null || NextText == null || NextIcon == null || SkipButton == null || HeroVideo == null) return;
            UpdateChrome();

            var i = Pages.SelectedIndex;
            if (i == 2) StaggerChildren(FeaturesPanel);
            else if (i == 3) Fx.FadeInUp(KeyPanel, 0, 24, 420);
            else if (i == 4) Fx.FadeInUp(ThemePanel, 0, 24, 420);
            else if (i == HeroPage) Fx.FadeInUp(HeroText, 200, 28, 600);
        }

        private void UpdateChrome()
        {
            var i = Pages.SelectedIndex;
            for (int d = 0; d < Dots.Children.Count; d++)
            {
                var dot = (Ellipse)Dots.Children[d];
                dot.Fill = (Brush)Application.Current.Resources[d == i ? "AccentBrush" : "StrokeBrush"];
            }
            var last = i == Pages.Items.Count - 1;
            NextText.Text = last ? S.Start : S.Next;
            NextIcon.UriSource = last ? Icons.Sparkle : Icons.ChevronRight;
            SkipButton.Visibility = last || i == 0 ? Visibility.Collapsed : Visibility.Visible;

            // The video only plays while visible: no wasted decoding on the other pages.
            try { if (i == HeroPage) HeroVideo.Play(); else HeroVideo.Pause(); } catch (Exception) { }
        }

        private static void StaggerChildren(Panel panel)
        {
            for (int c = 0; c < panel.Children.Count; c++) Fx.FadeInUp(panel.Children[c], c * 90, 26, 460);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.SelectedIndex < Pages.Items.Count - 1) Pages.SelectedIndex++;
            else Finish();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Finish();
        }

        private void Finish()
        {
            SaveProvider();
            AppSettings.Onboarded = true;
            try { HeroVideo.Stop(); } catch (Exception) { }
            App.ReloadUi(typeof(MainPage));
        }

        // ------------------------------------------------------------ key

        private string _shownProvider;

        private void ShowProvider(string id)
        {
            var p = Providers.Find(id);
            _shownProvider = id;
            KeyBox.Password = AppSettings.KeyFor(id);
            KeyBox.PlaceholderText = p.KeyPlaceholder;
            EndpointBox.Text = id == "custom" ? AppSettings.BaseUrlFor(id) : "";
            EndpointBox.Visibility = id == "custom" ? Visibility.Visible : Visibility.Collapsed;
            ProviderHint.Text = id == "gemini" ? S.HintGemini : id == "anthropic" ? S.HintClaude : id == "openai" ? S.HintOpenAI : S.HintCustom;
            VerifyStatus.Text = "";
        }

        private void SaveProvider()
        {
            if (_shownProvider == null) return;
            if (KeyBox.Password.Trim().Length > 0) AppSettings.SetKeyFor(_shownProvider, KeyBox.Password);
            if (_shownProvider == "custom") AppSettings.SetBaseUrlFor("custom", EndpointBox.Text.Trim().TrimEnd('/'));
        }

        private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var p = ProviderCombo.SelectedItem as Provider;
            if (p == null || p.Id == _shownProvider) return;
            SaveProvider();
            AppSettings.ProviderId = p.Id;
            ShowProvider(p.Id);
        }

        private async void OpenStudio_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri(Providers.Find(_shownProvider ?? "gemini").KeyUrl));
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            var id = _shownProvider;
            var key = KeyBox.Password.Trim();
            if (key.Length == 0 && id != "custom") { VerifyStatus.Text = S.KeyMissing; return; }
            SaveProvider();
            if (!AppSettings.HasBaseUrlFor(id)) { VerifyStatus.Text = S.ErrNoEndpoint; return; }

            VerifyButton.IsEnabled = false;
            VerifyRing.IsActive = true;
            VerifyStatus.Text = S.Verifying;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var models = await Ai.ListModelsAsync(id, key, AppSettings.BaseUrlFor(id), cts.Token);
                    if (models.Count > 0)
                    {
                        AppSettings.SetModelsFor(id, models);
                        if (!models.Any(m => m.Id == AppSettings.ModelFor(id))) AppSettings.SetModelFor(id, models[0].Id);
                    }
                    VerifyStatus.Text = "✓  " + string.Format(S.ModelsLoaded, models.Count) +
                        (string.IsNullOrEmpty(AppSettings.ImageModelFor(id)) ? "" : S.KeyAlsoImage);
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

        private void Swatches_ItemClick(object sender, ItemClickEventArgs e)
        {
            var p = e.ClickedItem as AccentPalette;
            if (p == null) return;
            AppSettings.AccentIndex = _swatches.IndexOf(p);
            ThemeManager.SyncSelection(_swatches);
            ThemeManager.Apply();
        }

        private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            AppSettings.LightTheme = ThemeToggle.IsOn;
            ThemeManager.Apply();
            // Brushes are recoloured in place; the frame theme fixes the built-in controls.
            var frame = Window.Current.Content as Frame;
            if (frame != null) frame.RequestedTheme = ThemeManager.Theme;
        }
    }
}
