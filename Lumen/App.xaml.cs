using System;
using Lumen.Core;
using Lumen.Views;
using Windows.ApplicationModel.Activation;
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
        public App()
        {
            InitializeComponent();
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

        private Frame CreateFrame()
        {
            var frame = new Frame();
            if (AppSettings.Animations)
            {
                frame.ContentTransitions = new TransitionCollection { new NavigationThemeTransition() };
            }
            frame.Navigated += OnNavigated;
            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
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
