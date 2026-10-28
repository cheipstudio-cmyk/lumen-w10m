using System;
using System.Collections.Generic;
using System.Linq;
using Lumen.Core;
using Lumen.Views;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace Lumen
{
    /// <summary>Pages that want first shot at the hardware / system back button.</summary>
    public interface IBackAware
    {
        bool HandleBack();
    }

    sealed partial class App : Application
    {
        /// <summary>Languages Lumen ships, with their native names.</summary>
        public static readonly List<Tuple<string, string>> Languages = new List<Tuple<string, string>>
        {
            Tuple.Create("en-US", "English"),
            Tuple.Create("it-IT", "Italiano"),
            Tuple.Create("es-ES", "Español"),
            Tuple.Create("fr-FR", "Français"),
            Tuple.Create("de-DE", "Deutsch"),
            Tuple.Create("pt-BR", "Português (Brasil)"),
        };

        public App()
        {
            InitializeComponent();
            // Can only be set here; covers dialogs, menus and the status bar from the first frame.
            RequestedTheme = AppSettings.LightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        }

        /// <summary>Language actually in use: the override if set, else the best match of the phone language.</summary>
        public static string CurrentLanguage
        {
            get
            {
                var o = ApplicationLanguages.PrimaryLanguageOverride;
                if (!string.IsNullOrEmpty(o)) return o;
                var phone = ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
                var match = Languages.FirstOrDefault(l => phone.StartsWith(l.Item1.Substring(0, 2), StringComparison.OrdinalIgnoreCase));
                return match != null ? match.Item1 : "en-US";
            }
        }

        public static bool HasLanguageOverride { get { return !string.IsNullOrEmpty(ApplicationLanguages.PrimaryLanguageOverride); } }

        /// <summary>Switches the UI language ("" = follow the phone). The system persists the override across launches.</summary>
        public static void SetLanguage(string code)
        {
            ApplicationLanguages.PrimaryLanguageOverride = code ?? "";
            try { ResourceContext.GetForCurrentView().Reset(); } catch (Exception) { }
            try { ResourceContext.GetForViewIndependentUse().Reset(); } catch (Exception) { }
            S.Reset();
        }

        /// <summary>
        /// Rebuilds the whole UI so every x:Uid string is read again in the new language.
        /// MainPage is always the root; <paramref name="top"/> (if any) is pushed on top of it.
        /// </summary>
        public static void ReloadUi(Type top, object parameter = null)
        {
            var frame = CreateFrame();
            Window.Current.Content = frame;
            if (top == typeof(OnboardingPage))
            {
                frame.Navigate(typeof(OnboardingPage), parameter, new SuppressNavigationTransitionInfo());
                return;
            }
            frame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
            if (top != null && top != typeof(MainPage)) frame.Navigate(top, parameter, new SuppressNavigationTransitionInfo());
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                ThemeManager.Apply();
                frame = CreateFrame();
                Window.Current.Content = frame;
            }
            if (e.PrelaunchActivated) return;
            if (frame.Content == null)
            {
                frame.Navigate(AppSettings.Onboarded ? typeof(MainPage) : typeof(OnboardingPage), null);
            }
            Window.Current.Activate();
        }

        protected override void OnShareTargetActivated(ShareTargetActivatedEventArgs args)
        {
            // Share target runs in its own window: give it a full chat, prefilled with the shared content.
            ThemeManager.Apply();
            var frame = CreateFrame();
            Window.Current.Content = frame;
            frame.Navigate(typeof(MainPage), args.ShareOperation);
            Window.Current.Activate();
        }

        // One BackRequested handler per view (the share target runs in its own view and thread).
        [ThreadStatic] private static bool _backHooked;

        private static Frame CreateFrame()
        {
            var frame = new Frame();
            frame.RequestedTheme = ThemeManager.Theme;
            if (AppSettings.Animations)
            {
                frame.ContentTransitions = new TransitionCollection { new NavigationThemeTransition() };
            }
            frame.Navigated += OnNavigated;
            if (!_backHooked)
            {
                // One handler per view: it always looks up the current frame.
                SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
                _backHooked = true;
            }
            return frame;
        }

        private static void OnNavigated(object sender, NavigationEventArgs e)
        {
            var frame = (Frame)sender;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                frame.CanGoBack ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;
        }

        private static void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null) return;

            var aware = frame.Content as IBackAware;
            if (aware != null && aware.HandleBack())
            {
                e.Handled = true;
                return;
            }
            if (frame.CanGoBack)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }
    }
}
