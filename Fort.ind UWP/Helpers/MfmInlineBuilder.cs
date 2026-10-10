using System;
using System.Collections.Generic;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public static class MfmInlineBuilder
    {
        public const double BodyEmojiSize = 20;

        public const double NameEmojiSize = 18;

        private const int LinkClickWindowMilliseconds = 400;

        private static int s_lastLinkTicks;

        private static int s_linkInvoked;

        public static bool WasLinkJustInvoked
        {
            get
            {
                if (Volatile.Read(ref s_linkInvoked) == 0) return false;

                var elapsed = unchecked(Environment.TickCount - Volatile.Read(ref s_lastLinkTicks));
                return elapsed >= 0 && elapsed < LinkClickWindowMilliseconds;
            }
        }

        public static void Fill(RichTextBlock target, IReadOnlyList<MfmSegment> segments, bool interactive, double emojiSize)
        {
            target.Blocks.Clear();

            var paragraph = new Paragraph();
            Append(paragraph.Inlines, segments, interactive, emojiSize);
            target.Blocks.Add(paragraph);
        }

        public static void Append(InlineCollection inlines, IReadOnlyList<MfmSegment> segments, bool interactive, double emojiSize)
        {
            if (segments == null) return;

            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case MfmSegmentKind.Emoji:
                        inlines.Add(EmojiInline(segment, emojiSize));
                        break;
                    case MfmSegmentKind.Link:
                    case MfmSegmentKind.Mention:
                    case MfmSegmentKind.Hashtag:
                        if (interactive)
                        {
                            inlines.Add(LinkInline(segment));
                        }
                        else
                        {
                            AppendText(inlines, segment.Text);
                        }
                        break;
                    default:
                        AppendText(inlines, segment.Text);
                        break;
                }
            }
        }

        private static void AppendText(InlineCollection inlines, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var start = 0;
            while (start <= text.Length)
            {
                var lineEnd = text.IndexOf('\n', start);
                var piece = lineEnd < 0 ? text.Substring(start) : text.Substring(start, lineEnd - start);
                if (piece.Length > 0) inlines.Add(new Run { Text = piece });

                if (lineEnd < 0) break;

                inlines.Add(new LineBreak());
                start = lineEnd + 1;
            }
        }

        private static Inline LinkInline(MfmSegment segment)
        {
            var link = new Hyperlink();
            link.Inlines.Add(new Run { Text = segment.Text });
            link.Click += (sender, args) => OnLinkClick(sender, segment);
            return link;
        }

        private static async void OnLinkClick(Hyperlink link, MfmSegment segment)
        {
            try
            {
                Volatile.Write(ref s_lastLinkTicks, Environment.TickCount);
                Volatile.Write(ref s_linkInvoked, 1);

                switch (segment.Kind)
                {
                    case MfmSegmentKind.Mention:
                        await SocialWindows.ShowMentionAsync(segment);
                        break;
                    case MfmSegmentKind.Hashtag:
                        var origin = link == null || link.ElementStart == null ? null : link.ElementStart.VisualParent;
                        if (!SocialThreads.OpenTag(origin, segment.Tag)) await WebLauncher.LaunchAsync(segment.Url);
                        break;
                    default:
                        await WebLauncher.LaunchAsync(segment.Url);
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MfmInlineBuilder: following a link failed", ex);
            }
        }

        private static Inline EmojiInline(MfmSegment segment, double size)
        {
            var bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelHeight = (int)Math.Ceiling(size);
            bitmap.UriSource = segment.EmojiUri;

            var image = new Image
            {
                Source = bitmap,
                Height = size,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(1, 0, 1, -Math.Round(size * 0.25))
            };
            AutomationProperties.SetName(image, segment.Text);
            ToolTipService.SetToolTip(image, segment.Text);

            return new InlineUIContainer { Child = image };
        }
    }
}
