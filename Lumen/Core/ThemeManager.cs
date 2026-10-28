using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Lumen.Core
{
    [Windows.UI.Xaml.Data.Bindable]
    public sealed class AccentPalette : Observable
    {
        public string Name { get; set; }
        public Color Color { get; set; }
        public SolidColorBrush SwatchBrush { get { return new SolidColorBrush(Name == "phone" ? ThemeManager.PhoneAccentColor : Color); } }

        /// <summary>Only the "phone accent" tile carries a caption.</summary>
        public string Label { get { return Name == "phone" ? S.PhoneAccent : ""; } }

        private bool _selected;
        public bool Selected
        {
            get { return _selected; }
            set { if (Set(ref _selected, value)) { Raise(nameof(SelectedVisibility)); } }
        }
        public Visibility SelectedVisibility { get { return _selected ? Visibility.Visible : Visibility.Collapsed; } }
    }

    /// <summary>
    /// Windows Phone style theming: one flat accent colour (the classic 20, or the phone's own accent)
    /// on a black or white background. Pages share the brush objects from App.xaml, recoloured in place.
    /// </summary>
    public static class ThemeManager
    {
        private static Color Hex(uint argb)
        {
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        /// <summary>Index 0 is "phone accent"; the rest are the Windows Phone accent colours.</summary>
        public static readonly List<AccentPalette> Palettes = new List<AccentPalette>
        {
            new AccentPalette { Name = "phone", Color = PhoneAccent() },
            new AccentPalette { Name = "lime", Color = Hex(0xFFA4C400) },
            new AccentPalette { Name = "green", Color = Hex(0xFF60A917) },
            new AccentPalette { Name = "emerald", Color = Hex(0xFF008A00) },
            new AccentPalette { Name = "teal", Color = Hex(0xFF00ABA9) },
            new AccentPalette { Name = "cyan", Color = Hex(0xFF1BA1E2) },
            new AccentPalette { Name = "cobalt", Color = Hex(0xFF0050EF) },
            new AccentPalette { Name = "indigo", Color = Hex(0xFF6A00FF) },
            new AccentPalette { Name = "violet", Color = Hex(0xFFAA00FF) },
            new AccentPalette { Name = "pink", Color = Hex(0xFFF472D0) },
            new AccentPalette { Name = "magenta", Color = Hex(0xFFD80073) },
            new AccentPalette { Name = "crimson", Color = Hex(0xFFA20025) },
            new AccentPalette { Name = "red", Color = Hex(0xFFE51400) },
            new AccentPalette { Name = "orange", Color = Hex(0xFFFA6800) },
            new AccentPalette { Name = "amber", Color = Hex(0xFFF0A30A) },
            new AccentPalette { Name = "yellow", Color = Hex(0xFFE3C800) },
            new AccentPalette { Name = "brown", Color = Hex(0xFF825A2C) },
            new AccentPalette { Name = "olive", Color = Hex(0xFF6D8764) },
            new AccentPalette { Name = "steel", Color = Hex(0xFF647687) },
            new AccentPalette { Name = "mauve", Color = Hex(0xFF76608A) },
            new AccentPalette { Name = "taupe", Color = Hex(0xFF87794E) },
        };

        public static Color PhoneAccentColor { get { return PhoneAccent(); } }

        /// <summary>Marks the current accent tile as selected (for the swatch grids).</summary>
        public static void SyncSelection(IList<AccentPalette> list)
        {
            for (int i = 0; i < list.Count; i++) list[i].Selected = i == AppSettings.AccentIndex;
        }

        /// <summary>Fresh swatch items for one page: bound objects must never be shared across windows (share target).</summary>
        public static List<AccentPalette> NewSwatches()
        {
            var list = new List<AccentPalette>();
            foreach (var p in Palettes) list.Add(new AccentPalette { Name = p.Name, Color = p.Color });
            SyncSelection(list);
            return list;
        }

        /// <summary>Menus built in code follow the app theme chosen in settings, even before a restart.</summary>
        public static MenuFlyout Themed(MenuFlyout menu)
        {
            var style = new Style(typeof(MenuFlyoutPresenter));
            style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, Theme));
            menu.MenuFlyoutPresenterStyle = style;
            return menu;
        }

        private static Color PhoneAccent()
        {
            try { return new UISettings().GetColorValue(UIColorType.Accent); }
            catch (Exception) { return Hex(0xFF1BA1E2); }
        }

        public static AccentPalette Current
        {
            get
            {
                var i = AppSettings.AccentIndex;
                return Palettes[i >= 0 && i < Palettes.Count ? i : 0];
            }
        }

        public static Color Accent
        {
            get { return AppSettings.AccentIndex == 0 ? PhoneAccent() : Current.Color; }
        }

        public static ElementTheme Theme { get { return AppSettings.LightTheme ? ElementTheme.Light : ElementTheme.Dark; } }

        public static double MessageFontSize
        {
            get
            {
                switch (AppSettings.TextSize)
                {
                    case 0: return 13.5;
                    case 2: return 17.5;
                    default: return 15;
                }
            }
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(255,
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Call once at startup (before any page) and again after every change.</summary>
        public static void Apply()
        {
            var res = Application.Current.Resources;
            var accent = Accent;
            var light = AppSettings.LightTheme;
            var black = Hex(0xFF000000);
            var white = Hex(0xFFFFFFFF);

            SetBrush(res, "BgBrush", light ? white : black);
            SetBrush(res, "SurfaceBrush", light ? Hex(0xFFF0F0F0) : Hex(0xFF1F1F1F));
            SetBrush(res, "Surface2Brush", light ? Hex(0xFFE2E2E2) : Hex(0xFF2B2B2B));
            SetBrush(res, "PaneBrush", light ? Hex(0xFFF2F2F2) : Hex(0xFF171717));
            SetBrush(res, "GlassBrush", light ? Hex(0xFFF2F2F2) : Hex(0xFF171717));
            SetBrush(res, "StrokeBrush", light ? Hex(0x33000000) : Hex(0x40FFFFFF));
            SetBrush(res, "HighlightBrush", light ? Hex(0x14000000) : Hex(0x14FFFFFF));
            SetBrush(res, "TextPrimaryBrush", light ? black : white);
            SetBrush(res, "TextSecondaryBrush", light ? Hex(0xFF6B6B6B) : Hex(0xFF9A9A9A));
            SetBrush(res, "AccentBrush", accent);
            SetBrush(res, "AccentSoftBrush", Color.FromArgb(0x33, accent.R, accent.G, accent.B));
            // Messaging-style bubbles: yours in the accent, Lumen's in a deeper shade of it.
            SetBrush(res, "UserBubbleSolid", accent);
            SetBrush(res, "ModelBubbleSolid", Mix(accent, black, light ? 0.18 : 0.55));

            SetGradient(res, "AccentGradient", accent, accent, accent);
            SetGradient(res, "UserBubbleBrush", accent, accent, accent);

            res["SystemAccentColor"] = accent;
        }

        private static void SetBrush(ResourceDictionary res, string key, Color c)
        {
            object o;
            if (!res.TryGetValue(key, out o)) return;
            var b = o as SolidColorBrush;
            if (b != null) b.Color = c;
        }

        private static void SetGradient(ResourceDictionary res, string key, Color a, Color b, Color c)
        {
            object o;
            if (!res.TryGetValue(key, out o)) return;
            var g = o as LinearGradientBrush;
            if (g == null || g.GradientStops.Count < 3) return;
            g.GradientStops[0].Color = a;
            g.GradientStops[1].Color = b;
            g.GradientStops[2].Color = c;
        }
    }
}
