using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Lumen.Core
{
    /// <summary>
    /// Small animation helpers. Everything animates Opacity / RenderTransform only,
    /// which XAML runs on the compositor thread: smooth even while the UI thread is busy.
    /// </summary>
    public static class Fx
    {
        public static bool Enabled { get { return AppSettings.Animations; } }

        private static EasingFunctionBase EaseOut { get { return new CubicEase { EasingMode = EasingMode.EaseOut }; } }
        private static EasingFunctionBase BackOut { get { return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }; } }

        private static T EnsureTransform<T>(UIElement el) where T : Transform, new()
        {
            var t = el.RenderTransform as T;
            if (t == null)
            {
                t = new T();
                el.RenderTransform = t;
            }
            return t;
        }

        /// <summary>Fade in while sliding up by <paramref name="distance"/> px.</summary>
        public static void FadeInUp(UIElement el, int delayMs = 0, double distance = 18, int durationMs = 420)
        {
            if (el == null) return;
            if (!Enabled) { el.Opacity = 1; return; }

            var t = EnsureTransform<TranslateTransform>(el);
            el.Opacity = 0;
            t.Y = distance;

            var sb = new Storyboard();
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));

            var fade = new DoubleAnimation { From = 0, To = 1, Duration = dur, BeginTime = begin, EasingFunction = EaseOut };
            Storyboard.SetTarget(fade, el);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var slide = new DoubleAnimation { From = distance, To = 0, Duration = dur, BeginTime = begin, EasingFunction = EaseOut };
            Storyboard.SetTarget(slide, t);
            Storyboard.SetTargetProperty(slide, "Y");

            sb.Children.Add(fade);
            sb.Children.Add(slide);
            sb.Begin();
        }

        /// <summary>Quick pop used on buttons and new bubbles.</summary>
        public static void Pop(UIElement el, double from = 0.85, int durationMs = 320)
        {
            if (el == null || !Enabled) return;
            var s = EnsureTransform<ScaleTransform>(el);
            var fe = el as FrameworkElement;
            if (fe != null) el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            var sb = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var x = new DoubleAnimation { From = from, To = 1, Duration = dur, EasingFunction = BackOut };
            var y = new DoubleAnimation { From = from, To = 1, Duration = dur, EasingFunction = BackOut };
            Storyboard.SetTarget(x, s); Storyboard.SetTargetProperty(x, "ScaleX");
            Storyboard.SetTarget(y, s); Storyboard.SetTargetProperty(y, "ScaleY");
            sb.Children.Add(x);
            sb.Children.Add(y);
            sb.Begin();
        }

        /// <summary>Endless gentle "breathing" scale, for the logo orb. Returns the storyboard so callers can stop it.</summary>
        public static Storyboard Breathe(UIElement el, double amount = 0.06, int periodMs = 2600)
        {
            if (el == null || !Enabled) return null;
            el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var s = EnsureTransform<ScaleTransform>(el);
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            var dur = new Duration(TimeSpan.FromMilliseconds(periodMs / 2));
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var x = new DoubleAnimation { From = 1, To = 1 + amount, Duration = dur, EasingFunction = ease };
            var y = new DoubleAnimation { From = 1, To = 1 + amount, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(x, s); Storyboard.SetTargetProperty(x, "ScaleX");
            Storyboard.SetTarget(y, s); Storyboard.SetTargetProperty(y, "ScaleY");
            sb.Children.Add(x);
            sb.Children.Add(y);
            sb.Begin();
            return sb;
        }

        /// <summary>Endless opacity pulse (recording dot).</summary>
        public static Storyboard Pulse(UIElement el, int periodMs = 900)
        {
            if (el == null) return null;
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            var a = new DoubleAnimation { From = 1, To = 0.25, Duration = new Duration(TimeSpan.FromMilliseconds(periodMs / 2)) };
            Storyboard.SetTarget(a, el);
            Storyboard.SetTargetProperty(a, "Opacity");
            sb.Children.Add(a);
            sb.Begin();
            return sb;
        }
    }

    /// <summary>Three bouncing dots shown while the model is thinking.</summary>
    public sealed class TypingIndicator : StackPanel
    {
        private Storyboard _sb;

        private bool _loaded;

        public TypingIndicator()
        {
            Orientation = Orientation.Horizontal;
            Height = 22;
            Loaded += (s, e) => { _loaded = true; Update(); };
            Unloaded += (s, e) => { _loaded = false; Update(); };
            // Every model bubble carries one of these, collapsed: only animate while actually visible.
            RegisterPropertyChangedCallback(VisibilityProperty, (s, dp) => Update());
        }

        private void Update()
        {
            if (_loaded && Visibility == Visibility.Visible) Start();
            else Stop();
        }

        private void Start()
        {
            if (Children.Count == 0)
            {
                var brush = Application.Current.Resources["AccentBrush"] as Brush;
                for (int i = 0; i < 3; i++)
                {
                    Children.Add(new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Margin = new Thickness(0, 7, 6, 0),
                        Fill = brush,
                        RenderTransform = new TranslateTransform()
                    });
                }
            }
            if (_sb != null) return;

            _sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            for (int i = 0; i < Children.Count; i++)
            {
                var dot = (Ellipse)Children[i];
                var bounce = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 150) };
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 0 });
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(260), Value = -6, EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(520), Value = 0, EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } });
                bounce.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1000), Value = 0 });
                Storyboard.SetTarget(bounce, dot.RenderTransform);
                Storyboard.SetTargetProperty(bounce, "Y");
                _sb.Children.Add(bounce);

                var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 150) };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 0.35 });
                fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(260), Value = 1 });
                fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(520), Value = 0.35 });
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1000), Value = 0.35 });
                Storyboard.SetTarget(fade, dot);
                Storyboard.SetTargetProperty(fade, "Opacity");
                _sb.Children.Add(fade);
            }
            _sb.Begin();
        }

        private void Stop()
        {
            if (_sb == null) return;
            _sb.Stop();
            _sb = null;
        }
    }
}
