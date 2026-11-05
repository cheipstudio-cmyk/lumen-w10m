namespace Lumen.Core
{
    /// <summary>
    /// Segoe MDL2 Assets glyphs: the system icon font of Windows 10 Mobile, so icons look exactly like the built-in apps.
    /// </summary>
    public static class Icons
    {
        public const string Font = "Segoe MDL2 Assets";

        public const string Menu = "\uE700";
        public const string Add = "\uE710";
        public const string Settings = "\uE713";
        public const string Send = "\uE724";
        public const string Mic = "\uE720";
        public const string Stop = "\uE71A";
        public const string Camera = "\uE722";
        public const string Image = "\uEB9F";
        public const string Copy = "\uE8C8";
        public const string Share = "\uE72D";
        public const string Regenerate = "\uE72C";
        public const string Delete = "\uE74D";
        public const string Pin = "\uE718";
        public const string Unpin = "\uE77A";
        public const string Rename = "\uE8AC";
        public const string Edit = "\uE70F";
        public const string Search = "\uE721";
        public const string Chat = "\uE8BD";
        public const string Speak = "\uE767";
        public const string SelectText = "\uE8B3";
        public const string Globe = "\uE774";
        public const string Back = "\uE72B";
        public const string Save = "\uE74E";
        public const string Document = "\uE8A5";
        public const string Sparkle = "\uEA80";
        public const string Color = "\uE790";
        public const string DarkTheme = "\uE708";
        public const string TextSize = "\uE8D2";
        public const string Info = "\uE946";
        public const string Key = "\uE8D7";
        public const string Model = "\uE99A";
        public const string Shield = "\uEA18";
        public const string Storage = "\uEDA2";
        public const string Play = "\uE768";
        public const string Pause = "\uE769";
        public const string Check = "\uE73E";
        public const string Dismiss = "\uE711";
        public const string Open = "\uE8A7";
        public const string ImageSparkle = "\uE91B";
        public const string DocumentAdd = "\uE7C3";
        public const string ChevronDown = "\uE70D";
        public const string ChevronRight = "\uE76C";
        public const string Person = "\uE77B";
        public const string Bot = "\uE99A";
        public const string Lightbulb = "\uEA80";
        public const string Book = "\uE82D";
        public const string History = "\uE81C";
        public const string Warning = "\uE7BA";
        public const string Error = "\uE783";
        public const string CheckCircle = "\uE930";
        public const string Link = "\uE71B";
        public const string Phone = "\uE717";
        public const string Temperature = "\uE9CA";
        public const string Lock = "\uE72E";
        public const string Flash = "\uE945";
        public const string Eye = "\uE7B3";
        public const string Folder = "\uE8B7";
        public const string Wand = "\uE945";
        public const string Paint = "\uE771";
        public const string Options = "\uE713";
        public const string More = "\uE712";
        public const string Cloud = "\uE753";
        public const string Language = "\uE774";

        /// <summary>A FontIcon for code-built UI (menus, rows).</summary>
        public static Windows.UI.Xaml.Controls.FontIcon Make(string glyph, double size = 20)
        {
            return new Windows.UI.Xaml.Controls.FontIcon
            {
                Glyph = glyph,
                FontSize = size,
                FontFamily = new Windows.UI.Xaml.Media.FontFamily(Font)
            };
        }
    }
}
