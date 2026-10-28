using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Lumen.Core
{
    public sealed class AccentPalette
    {
        public string Name { get; set; }
        public Color A { get; set; }       // gradient start
        public Color B { get; set; }       // gradient middle
        public Color C { get; set; }       // gradient end
        public Color Solid { get; set; }   // links, headers, toggles
        public Color Bubble1 { get; set; } // user bubble start
        public Color Bubble2 { get; set; } // user bubble end

        public SolidColorBrush SwatchBrush { get { return new SolidColorBrush(B); } }
    }

    /// <summary>
    /// Live theming without reloading pages: every page references the same brush objects
    /// from App.xaml via StaticResource, so we just recolor those objects in place.
    /// </summary>
    public static class ThemeManager
    {
        private static Color Hex(uint argb)
        {
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        public static readonly List<AccentPalette> Palettes = new List<AccentPalette>
        {
            new AccentPalette { Name = "Aurora",  A = Hex(0xFF4F8BFF), B = Hex(0xFF9B6BFF), C = Hex(0xFFFF6FB5), Solid = Hex(0xFF8AB4FF), Bubble1 = Hex(0xFF3E63F2), Bubble2 = Hex(0xFF6A4CF0) },
            new AccentPalette { Name = "Lime",    A = Hex(0xFF2E9E2A), B = Hex(0xFF60C13A), C = Hex(0xFFB4E06A), Solid = Hex(0xFF8BD65A), Bubble1 = Hex(0xFF2E8B2A), Bubble2 = Hex(0xFF4BA83A) },
            new AccentPalette { Name = "Cyan",    A = Hex(0xFF0078D7), B = Hex(0xFF1BA1E2), C = Hex(0xFF63D8F5), Solid = Hex(0xFF5CC8F2), Bubble1 = Hex(0xFF0A6EC4), Bubble2 = Hex(0xFF1793D1) },
            new AccentPalette { Name = "Magenta", A = Hex(0xFFA4006A), B = Hex(0xFFE3008C), C = Hex(0xFFFF6FBF), Solid = Hex(0xFFFF6FBF), Bubble1 = Hex(0xFFB0006F), Bubble2 = Hex(0xFFD6007F) },
            new AccentPalette { Name = "Orange",  A = Hex(0xFFE04A00), B = Hex(0xFFFA6800), C = Hex(0xFFFFB347), Solid = Hex(0xFFFF9A3C), Bubble1 = Hex(0xFFD45200), Bubble2 = Hex(0xFFF07000) },
            new AccentPalette { Name = "Red",     A = Hex(0xFFB3141B), B = Hex(0xFFE51400), C = Hex(0xFFFF7A6E), Solid = Hex(0xFFFF7A6E), Bubble1 = Hex(0xFFB5121B), Bubble2 = Hex(0xFFD9261C) },
            new AccentPalette { Name = "Violet",  A = Hex(0xFF4B1FA8), B = Hex(0xFF7A3FE0), C = Hex(0xFFC59BFF), Solid = Hex(0xFFB48CFF), Bubble1 = Hex(0xFF4F2AB8), Bubble2 = Hex(0xFF6E3BD6) },
            new AccentPalette { Name = "Teal",    A = Hex(0xFF00695C), B = Hex(0xFF00ABA9), C = Hex(0xFF7FE3D4), Solid = Hex(0xFF5FD9CB), Bubble1 = Hex(0xFF00796B), Bubble2 = Hex(0xFF00968A) },
        };

        public static AccentPalette Current
        {
            get
            {
                var i = AppSettings.AccentIndex;
                return Palettes[i >= 0 && i < Palettes.Count ? i : 0];
            }
        }

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

        /// <summary>Call once at startup (before any page) and again after every change.</summary>
        public static void Apply()
        {
            var res = Application.Current.Resources;
            var p = Current;
            var black = AppSettings.PureBlack;

            SetBrush(res, "BgBrush", black ? Hex(0xFF000000) : Hex(0xFF0E0F13));
            SetBrush(res, "SurfaceBrush", black ? Hex(0xFF0D0D0F) : Hex(0xFF181A21));
            SetBrush(res, "Surface2Brush", black ? Hex(0xFF1A1A1E) : Hex(0xFF23262F));
            SetBrush(res, "StrokeBrush", black ? Hex(0xFF222228) : Hex(0xFF2E323D));
            SetBrush(res, "AccentBrush", p.Solid);

            SetGradient(res, "AccentGradient", p.A, p.B, p.C);
            SetGradient(res, "UserBubbleBrush", p.Bubble1, p.Bubble2, p.Bubble2);

            // Built-in controls (ToggleSwitch, Slider, selection borders) read this at startup: they follow a new accent after a restart.
            res["SystemAccentColor"] = p.B;
        }

        private static void SetBrush(ResourceDictionary res, string key, Color c)
        {
            var b = res[key] as SolidColorBrush;
            if (b != null) b.Color = c;
        }

        private static void SetGradient(ResourceDictionary res, string key, Color a, Color b, Color c)
        {
            var g = res[key] as LinearGradientBrush;
            if (g == null || g.GradientStops.Count < 3) return;
            g.GradientStops[0].Color = a;
            g.GradientStops[1].Color = b;
            g.GradientStops[2].Color = c;
        }
    }
}
