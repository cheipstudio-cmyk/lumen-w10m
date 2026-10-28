using System;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Lumen.Core
{
    /// <summary>Live tile: shows the latest conversation on the Start screen (medium, wide and large tiles).</summary>
    public static class TileHelper
    {
        public static void Update(string title, string preview)
        {
            try
            {
                var t = Escape(Trim(title, 60));
                var p = Escape(Trim(preview, 140));
                var xml =
                    "<tile><visual branding='nameAndLogo' displayName='Lumen'>" +
                    "<binding template='TileMedium'>" +
                    "<text hint-style='caption' hint-wrap='true' hint-maxLines='2'>" + t + "</text>" +
                    "<text hint-style='captionSubtle' hint-wrap='true' hint-maxLines='3'>" + p + "</text>" +
                    "</binding>" +
                    "<binding template='TileWide'>" +
                    "<text hint-style='body'>" + t + "</text>" +
                    "<text hint-style='captionSubtle' hint-wrap='true' hint-maxLines='3'>" + p + "</text>" +
                    "</binding>" +
                    "<binding template='TileLarge'>" +
                    "<text hint-style='subtitle' hint-wrap='true' hint-maxLines='2'>" + t + "</text>" +
                    "<text hint-style='bodySubtle' hint-wrap='true' hint-maxLines='6'>" + p + "</text>" +
                    "</binding>" +
                    "</visual></tile>";
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                TileUpdateManager.CreateTileUpdaterForApplication().Update(new TileNotification(doc));
            }
            catch (Exception) { }
        }

        public static void Clear()
        {
            try { TileUpdateManager.CreateTileUpdaterForApplication().Clear(); } catch (Exception) { }
        }

        // System.Security.SecurityElement isn't part of the UWP .NET surface.
        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static string Trim(string s, int max)
        {
            s = (s ?? "").Replace('\n', ' ').Replace("**", "").Replace("#", "").Trim();
            return s.Length > max ? s.Substring(0, max).TrimEnd() + "…" : s;
        }
    }
}
