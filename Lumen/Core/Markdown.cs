using System;
using System.Collections.Generic;
using System.Text;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace Lumen.Core
{
    /// <summary>
    /// Tiny markdown to RichTextBlock renderer. Covers what chat models actually emit:
    /// headings, lists, quotes, fenced code, tables (monospace), **bold**, *italic*, `code`, [links](url).
    /// Single pass, no regex, no allocations per character beyond the output runs.
    /// </summary>
    public static class Markdown
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
            "Source", typeof(string), typeof(Markdown), new PropertyMetadata(null, OnSourceChanged));

        public static string GetSource(DependencyObject o) { return (string)o.GetValue(SourceProperty); }
        public static void SetSource(DependencyObject o, string v) { o.SetValue(SourceProperty, v); }

        // XAML objects are bound to the thread that created them; the share window runs on its own thread.
        [ThreadStatic] private static FontFamily _mono;
        [ThreadStatic] private static Brush _codeBrush, _quoteBrush, _linkBrush;

        private static void EnsureBrushes()
        {
            if (_mono != null) return;
            _mono = new FontFamily("Consolas");
            _codeBrush = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xE0, 0xA0));
            _quoteBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            // Text sits on the accent bubble: links are white and underlined, quotes a softer white.
            _linkBrush = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xFF, 0xFF));
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var rtb = d as RichTextBlock;
            if (rtb == null) return;
            rtb.Blocks.Clear();
            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text)) return;
            EnsureBrushes();
            try
            {
                Render(rtb.Blocks, text);
            }
            catch (Exception)
            {
                rtb.Blocks.Clear();
                var p = new Paragraph();
                p.Inlines.Add(new Run { Text = text });
                rtb.Blocks.Add(p);
            }
        }

        private static void Render(BlockCollection blocks, string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            Paragraph para = null;          // current flowing paragraph
            StringBuilder code = null;      // inside ``` fence

            Action flush = () => { para = null; };

            for (int li = 0; li < lines.Length; li++)
            {
                var raw = lines[li];
                var trimmed = raw.TrimStart();

                // ---- fenced code
                if (trimmed.StartsWith("```"))
                {
                    if (code == null) { code = new StringBuilder(); flush(); }
                    else { blocks.Add(CodeBlock(code.ToString().TrimEnd('\n'))); code = null; }
                    continue;
                }
                if (code != null) { code.Append(raw).Append('\n'); continue; }

                // ---- blank line = paragraph break
                if (trimmed.Length == 0) { flush(); continue; }

                // ---- horizontal rule
                if (IsRule(trimmed)) { flush(); continue; }

                // ---- heading
                int hashes = 0;
                while (hashes < trimmed.Length && hashes < 6 && trimmed[hashes] == '#') hashes++;
                if (hashes > 0 && hashes < trimmed.Length && trimmed[hashes] == ' ')
                {
                    flush();
                    var p = new Paragraph { Margin = new Thickness(0, 6, 0, 4) };
                    var span = new Span { FontWeight = FontWeights.SemiBold, FontSize = hashes == 1 ? 21 : hashes == 2 ? 19 : 17 };
                    AddInlines(span.Inlines, trimmed.Substring(hashes + 1));
                    p.Inlines.Add(span);
                    blocks.Add(p);
                    continue;
                }

                // ---- table rows: keep them aligned in monospace
                if (trimmed.StartsWith("|"))
                {
                    flush();
                    if (IsTableSeparator(trimmed)) continue;
                    var p = new Paragraph { Margin = new Thickness(0), FontFamily = _mono, FontSize = 12 };
                    AddInlines(p.Inlines, trimmed);
                    blocks.Add(p);
                    continue;
                }

                // ---- quote
                if (trimmed.StartsWith(">"))
                {
                    flush();
                    var p = new Paragraph { Margin = new Thickness(10, 2, 0, 6), Foreground = _quoteBrush, FontStyle = FontStyle.Italic };
                    AddInlines(p.Inlines, trimmed.TrimStart('>', ' '));
                    blocks.Add(p);
                    continue;
                }

                // ---- lists
                int indent = raw.Length - trimmed.Length;
                string marker, rest;
                if (TryList(trimmed, out marker, out rest))
                {
                    flush();
                    var level = Math.Min(4, indent / 2);
                    var p = new Paragraph { Margin = new Thickness(6 + level * 14, 1, 0, 3), TextIndent = -14 };
                    p.Inlines.Add(new Run { Text = marker });
                    AddInlines(p.Inlines, rest);
                    blocks.Add(p);
                    continue;
                }

                // ---- normal text, consecutive lines flow into one paragraph
                if (para == null)
                {
                    para = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    blocks.Add(para);
                }
                else
                {
                    para.Inlines.Add(new LineBreak());
                }
                AddInlines(para.Inlines, trimmed);
            }

            if (code != null) blocks.Add(CodeBlock(code.ToString().TrimEnd('\n')));

            // Drop trailing margin of the last block so bubbles stay tight.
            if (blocks.Count > 0)
            {
                var last = blocks[blocks.Count - 1];
                var m = last.Margin;
                last.Margin = new Thickness(m.Left, m.Top, m.Right, 0);
            }
        }

        private static Paragraph CodeBlock(string code)
        {
            var p = new Paragraph { Margin = new Thickness(0, 4, 0, 8), FontFamily = _mono, FontSize = 12.5, Foreground = _codeBrush };
            p.Inlines.Add(new Run { Text = code });
            return p;
        }

        private static bool IsRule(string s)
        {
            if (s.Length < 3) return false;
            char c = s[0];
            if (c != '-' && c != '*' && c != '_') return false;
            foreach (var ch in s) if (ch != c && ch != ' ') return false;
            return true;
        }

        private static bool IsTableSeparator(string s)
        {
            foreach (var ch in s) if (ch != '|' && ch != '-' && ch != ':' && ch != ' ') return false;
            return true;
        }

        private static bool TryList(string s, out string marker, out string rest)
        {
            marker = rest = null;
            if (s.Length >= 2 && (s[0] == '-' || s[0] == '*' || s[0] == '+') && s[1] == ' ')
            {
                marker = "•  ";
                rest = s.Substring(2);
                return true;
            }
            int i = 0;
            while (i < s.Length && i < 3 && char.IsDigit(s[i])) i++;
            if (i > 0 && i + 1 < s.Length && (s[i] == '.' || s[i] == ')') && s[i + 1] == ' ')
            {
                marker = s.Substring(0, i + 1) + " ";
                rest = s.Substring(i + 2);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ inline

        private static void AddInlines(InlineCollection target, string s)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];

                if (c == '\\' && i + 1 < s.Length && "*_`[]\\#".IndexOf(s[i + 1]) >= 0)
                {
                    sb.Append(s[i + 1]); i += 2; continue;
                }

                if (c == '`')
                {
                    int end = s.IndexOf('`', i + 1);
                    if (end > i + 1)
                    {
                        FlushText(target, sb);
                        target.Add(new Run { Text = s.Substring(i + 1, end - i - 1), FontFamily = _mono, Foreground = _codeBrush });
                        i = end + 1; continue;
                    }
                }

                if ((c == '*' || c == '_') && i + 1 < s.Length && s[i + 1] == c)
                {
                    int end = s.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal);
                    if (end > i + 2)
                    {
                        FlushText(target, sb);
                        var bold = new Bold();
                        AddInlines(bold.Inlines, s.Substring(i + 2, end - i - 2));
                        target.Add(bold);
                        i = end + 2; continue;
                    }
                }

                // *italic*: only when it looks like emphasis, not "2 * 3"
                if (c == '*' && i + 1 < s.Length && s[i + 1] != ' ' && s[i + 1] != '*')
                {
                    int end = s.IndexOf('*', i + 1);
                    if (end > i + 1 && s[end - 1] != ' ')
                    {
                        FlushText(target, sb);
                        var it = new Italic();
                        AddInlines(it.Inlines, s.Substring(i + 1, end - i - 1));
                        target.Add(it);
                        i = end + 1; continue;
                    }
                }

                if (c == '[')
                {
                    int close = s.IndexOf("](", i + 1, StringComparison.Ordinal);
                    int paren = close > 0 ? s.IndexOf(')', close + 2) : -1;
                    if (close > i && paren > close)
                    {
                        var label = s.Substring(i + 1, close - i - 1);
                        var url = s.Substring(close + 2, paren - close - 2).Trim();
                        Uri uri;
                        if (Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                        {
                            FlushText(target, sb);
                            var link = new Hyperlink { NavigateUri = uri, Foreground = _linkBrush, UnderlineStyle = UnderlineStyle.Single };
                            link.Inlines.Add(new Run { Text = label });
                            target.Add(link);
                            i = paren + 1; continue;
                        }
                    }
                }

                sb.Append(c);
                i++;
            }
            FlushText(target, sb);
        }

        private static void FlushText(InlineCollection target, StringBuilder sb)
        {
            if (sb.Length == 0) return;
            target.Add(new Run { Text = sb.ToString() });
            sb.Clear();
        }
    }
}
