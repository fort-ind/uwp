using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        private static readonly ConditionalWeakTable<RichTextBlock, FillState> s_states =
            new ConditionalWeakTable<RichTextBlock, FillState>();

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

            FillState previous;
            if (s_states.TryGetValue(target, out previous)) previous.Reset();

            var paragraph = new Paragraph();
            Append(paragraph.Inlines, segments, interactive, emojiSize, interactive ? target : null);
            target.Blocks.Add(paragraph);
        }

        public static void Append(InlineCollection inlines, IReadOnlyList<MfmSegment> segments, bool interactive, double emojiSize)
        {
            Append(inlines, segments, interactive, emojiSize, null);
        }

        private static void Append(InlineCollection inlines, IReadOnlyList<MfmSegment> segments, bool interactive, double emojiSize, RichTextBlock owner)
        {
            if (segments == null) return;

            var afterBlock = false;
            foreach (var segment in segments)
            {
                var followsBlock = afterBlock;
                afterBlock = false;

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
                    case MfmSegmentKind.CodeBlock:
                        if (owner == null)
                        {
                            AppendText(inlines, segment.Text);
                            break;
                        }

                        DropTrailingBreak(inlines);
                        inlines.Add(CodeBlockInline(segment, owner));
                        afterBlock = true;
                        break;
                    case MfmSegmentKind.UnixTime:
                        inlines.Add(UnixTimeInline(segment));
                        break;
                    case MfmSegmentKind.InlineCode:
                        if (owner == null)
                        {
                            AppendText(inlines, segment.Text);
                        }
                        else
                        {
                            inlines.Add(InlineCodeRun(segment, owner));
                        }
                        break;
                    default:
                        AppendText(inlines, followsBlock ? WithoutLeadingBreak(segment.Text) : segment.Text);
                        break;
                }
            }
        }

        private static void DropTrailingBreak(InlineCollection inlines)
        {
            if (inlines.Count > 0 && inlines[inlines.Count - 1] is LineBreak)
            {
                inlines.RemoveAt(inlines.Count - 1);
            }
        }

        private static string WithoutLeadingBreak(string text)
        {
            return !string.IsNullOrEmpty(text) && text[0] == '\n' ? text.Substring(1) : text;
        }

        private static FillState StateOf(RichTextBlock owner)
        {
            return s_states.GetValue(owner, CreateState);
        }

        private static FillState CreateState(RichTextBlock owner)
        {
            return new FillState(owner);
        }

        private static Inline CodeBlockInline(MfmSegment segment, RichTextBlock owner)
        {
            var state = StateOf(owner);
            var block = new SocialCodeBlock();
            block.Load(segment, state.IsDark);
            state.Add(block);
            return new InlineUIContainer { Child = block };
        }

        private static Inline UnixTimeInline(MfmSegment segment)
        {
            var chip = new SocialTimeChip();
            chip.Show(segment.Time);
            return new InlineUIContainer { Child = chip };
        }

        private static Inline InlineCodeRun(MfmSegment segment, RichTextBlock owner)
        {
            var state = StateOf(owner);
            var run = new Run { Text = segment.Text, FontFamily = new FontFamily(CodePalette.FontName) };
            CodePalette.Paint(run, CodePalette.InlineCode(state.IsDark));
            state.Add(run);
            return run;
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

        private sealed class FillState
        {
            private readonly RichTextBlock _target;

            private readonly List<SocialCodeBlock> _blocks = new List<SocialCodeBlock>();

            private readonly List<Run> _runs = new List<Run>();

            public FillState(RichTextBlock target)
            {
                _target = target;
                target.SizeChanged += OnSizeChanged;
                target.ActualThemeChanged += OnActualThemeChanged;
            }

            public bool IsDark
            {
                get { return CodePalette.IsDark(_target); }
            }

            private double ContentWidth
            {
                get
                {
                    var width = _target.ActualWidth - _target.Padding.Left - _target.Padding.Right;
                    return width > 0 ? width : 0;
                }
            }

            public void Reset()
            {
                _blocks.Clear();
                _runs.Clear();
            }

            public void Add(SocialCodeBlock block)
            {
                _blocks.Add(block);

                var width = ContentWidth;
                if (width > 0) block.Width = width;
            }

            public void Add(Run run)
            {
                _runs.Add(run);
            }

            private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            {
                try
                {
                    var width = ContentWidth;
                    if (width <= 0) return;

                    foreach (var block in _blocks)
                    {
                        block.Width = width;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmInlineBuilder: sizing a code block failed", ex);
                }
            }

            private void OnActualThemeChanged(FrameworkElement sender, object args)
            {
                try
                {
                    var dark = IsDark;
                    foreach (var block in _blocks)
                    {
                        block.Recolor(dark);
                    }

                    var inlineBrush = CodePalette.InlineCode(dark);
                    foreach (var run in _runs)
                    {
                        CodePalette.Paint(run, inlineBrush);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmInlineBuilder: recolouring code failed", ex);
                }
            }
        }
    }
}
