using System;

namespace Lumen.Core
{
    /// <summary>
    /// Fluent System Icons (MIT, Microsoft) pre-rendered as white PNG masks in Assets/Icons.
    /// Shown with BitmapIcon, which tints them with Foreground: no font loading involved, works on every Windows 10 Mobile build.
    /// </summary>
    public static class Icons
    {
        private static Uri U(string name) { return new Uri("ms-appx:///Assets/Icons/" + name + ".png"); }

        public static readonly Uri Menu = U("Menu");
        public static readonly Uri Add = U("Add");
        public static readonly Uri Settings = U("Settings");
        public static readonly Uri Send = U("Send");
        public static readonly Uri Mic = U("Mic");
        public static readonly Uri Stop = U("Stop");
        public static readonly Uri Camera = U("Camera");
        public static readonly Uri Image = U("Image");
        public static readonly Uri Copy = U("Copy");
        public static readonly Uri Share = U("Share");
        public static readonly Uri Regenerate = U("Regenerate");
        public static readonly Uri Delete = U("Delete");
        public static readonly Uri Pin = U("Pin");
        public static readonly Uri Unpin = U("Unpin");
        public static readonly Uri Rename = U("Rename");
        public static readonly Uri Edit = U("Edit");
        public static readonly Uri Search = U("Search");
        public static readonly Uri Chat = U("Chat");
        public static readonly Uri Speak = U("Speak");
        public static readonly Uri SelectText = U("SelectText");
        public static readonly Uri Globe = U("Globe");
        public static readonly Uri Back = U("Back");
        public static readonly Uri Save = U("Save");
        public static readonly Uri Document = U("Document");
        public static readonly Uri Sparkle = U("Sparkle");
        public static readonly Uri Color = U("Color");
        public static readonly Uri DarkTheme = U("DarkTheme");
        public static readonly Uri TextSize = U("TextSize");
        public static readonly Uri Info = U("Info");
        public static readonly Uri Key = U("Key");
        public static readonly Uri Model = U("Model");
        public static readonly Uri Shield = U("Shield");
        public static readonly Uri Storage = U("Storage");
        public static readonly Uri Play = U("Play");
        public static readonly Uri Pause = U("Pause");
        public static readonly Uri Check = U("Check");
        public static readonly Uri Dismiss = U("Dismiss");
        public static readonly Uri Open = U("Open");
        public static readonly Uri ImageSparkle = U("ImageSparkle");
        public static readonly Uri DocumentAdd = U("DocumentAdd");
        public static readonly Uri ChevronDown = U("ChevronDown");
        public static readonly Uri ChevronRight = U("ChevronRight");
        public static readonly Uri Person = U("Person");
        public static readonly Uri Bot = U("Bot");
        public static readonly Uri Lightbulb = U("Lightbulb");
        public static readonly Uri Book = U("Book");
        public static readonly Uri History = U("History");
        public static readonly Uri Warning = U("Warning");
        public static readonly Uri Error = U("Error");
        public static readonly Uri CheckCircle = U("CheckCircle");
        public static readonly Uri Link = U("Link");
        public static readonly Uri Phone = U("Phone");
        public static readonly Uri Temperature = U("Temperature");
        public static readonly Uri Lock = U("Lock");
        public static readonly Uri Flash = U("Flash");
        public static readonly Uri Eye = U("Eye");
        public static readonly Uri Folder = U("Folder");
        public static readonly Uri Wand = U("Wand");
        public static readonly Uri Paint = U("Paint");
        public static readonly Uri Options = U("Options");
        public static readonly Uri More = U("More");
        public static readonly Uri Cloud = U("Cloud");
        public static readonly Uri Language = U("Language");
    }
}
