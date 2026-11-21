using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
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

        private const double MaximumScale = 8;

        private const double SmallScale = 0.8;

        private const double RubyFallbackScale = 0.6;

        private static readonly ConditionalWeakTable<RichTextBlock, FillState> s_states =
            new ConditionalWeakTable<RichTextBlock, FillState>();

        private static readonly FontFamily s_codeFont = new FontFamily(CodePalette.FontName);

        private static int s_lastLinkTicks;

        private static int s_linkInvoked;

        [ThreadStatic]
        private static SolidColorBrush[] t_paletteBrushes;

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
            var state = StateOf(target);
            state.Prepare(segments, new Context(interactive, emojiSize, target.FontSize, MfmEffects.AnimationsEnabled, MfmStyle.Default));
            Render(target, state);
        }

        private static void Render(RichTextBlock target, FillState state)
        {
            target.Blocks.Clear();
            target.TextHighlighters.Clear();
            state.Reset();

            var writer = new Writer(target, state);
            writer.Append(state.Segments, state.Context.Root);
            writer.Finish();
        }

        private static void MarkLinkInvoked()
        {
            Volatile.Write(ref s_lastLinkTicks, Environment.TickCount);
            Volatile.Write(ref s_linkInvoked, 1);
        }

        private static FillState StateOf(RichTextBlock owner)
        {
            return s_states.GetValue(owner, CreateState);
        }

        private static FillState CreateState(RichTextBlock owner)
        {
            return new FillState(owner);
        }

        private static Brush Subtle(bool dark)
        {
            return PaletteBrush(dark ? 0 : 1, dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        }

        private static Brush Spoiler(bool dark)
        {
            return PaletteBrush(dark ? 2 : 3, dark ? Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A) : Color.FromArgb(0xFF, 0x7A, 0x7A, 0x7A));
        }

        private static Brush PaletteBrush(int slot, Color color)
        {
            if (t_paletteBrushes == null) t_paletteBrushes = new SolidColorBrush[4];
            if (t_paletteBrushes[slot] == null) t_paletteBrushes[slot] = new SolidColorBrush(color);
            return t_paletteBrushes[slot];
        }

        private static FontFamily FontFor(MfmSegment function)
        {
            if (function.HasArgument("serif")) return new FontFamily("Times New Roman");
            if (function.HasArgument("monospace")) return s_codeFont;
            if (function.HasArgument("cursive")) return new FontFamily("Comic Sans MS");
            if (function.HasArgument("fantasy")) return new FontFamily("Impact");
            return null;
        }

        private static async void OnLinkClick(Hyperlink link, MfmSegment segment)
        {
            try
            {
                MarkLinkInvoked();

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

        private sealed class MfmStyle
        {
            public static readonly MfmStyle Default = new MfmStyle();

            public bool Bold { get; private set; }

            public bool Italic { get; private set; }

            public bool Strike { get; private set; }

            public bool Subtle { get; private set; }

            public double Scale { get; private set; } = 1;

            public Brush Foreground { get; private set; }

            public FontFamily Family { get; private set; }

            public bool InBox { get; private set; }

            private MfmStyle Copy()
            {
                return (MfmStyle)MemberwiseClone();
            }

            public MfmStyle WithBold()
            {
                var copy = Copy();
                copy.Bold = true;
                return copy;
            }

            public MfmStyle WithItalic()
            {
                var copy = Copy();
                copy.Italic = true;
                return copy;
            }

            public MfmStyle WithStrike()
            {
                var copy = Copy();
                copy.Strike = true;
                return copy;
            }

            public MfmStyle WithSmall()
            {
                var copy = Copy();
                copy.Subtle = true;
                copy.Scale = Scale * SmallScale;
                return copy;
            }

            public MfmStyle Scaled(double factor)
            {
                var copy = Copy();
                copy.Scale = Math.Min(Scale * factor, MaximumScale);
                return copy;
            }

            public MfmStyle WithForeground(Brush brush)
            {
                var copy = Copy();
                copy.Foreground = brush;
                copy.Subtle = false;
                return copy;
            }

            public MfmStyle WithFamily(FontFamily family)
            {
                var copy = Copy();
                copy.Family = family;
                return copy;
            }

            public MfmStyle ForBox()
            {
                var copy = Copy();
                copy.Scale = 1;
                copy.InBox = true;
                return copy;
            }
        }

        private sealed class Context
        {
            public Context(bool interactive, double emojiSize, double baseSize, bool animate, MfmStyle root)
            {
                Interactive = interactive;
                EmojiSize = emojiSize;
                BaseSize = baseSize;
                Animate = animate;
                Root = root;
            }

            public bool Interactive { get; }

            public double EmojiSize { get; }

            public double BaseSize { get; }

            public bool Animate { get; }

            public MfmStyle Root { get; }
        }

        private sealed class PendingHighlight
        {
            public TextElement First;

            public TextElement Last;

            public Brush Background;

            public Brush Foreground;

            public bool IsSpoiler;
        }

        private sealed class Writer
        {
            private readonly RichTextBlock _target;

            private readonly FillState _state;

            private readonly Context _context;

            private readonly List<Paragraph> _paragraphs = new List<Paragraph>();

            private readonly List<TextElement> _elements = new List<TextElement>();

            private readonly List<PendingHighlight> _highlights = new List<PendingHighlight>();

            private Paragraph _current = new Paragraph();

            private bool _afterBlock;

            private int _spoilerIndex;

            public Writer(RichTextBlock target, FillState state)
            {
                _target = target;
                _state = state;
                _context = state.Context;
            }

            private InlineCollection Inlines
            {
                get { return _current.Inlines; }
            }

            public void Finish()
            {
                FinishParagraph();
                foreach (var paragraph in _paragraphs)
                {
                    _target.Blocks.Add(paragraph);
                }

                foreach (var pending in _highlights)
                {
                    ApplyHighlight(pending);
                }
            }

            private void FinishParagraph()
            {
                if (_current.Inlines.Count > 0) _paragraphs.Add(_current);
                _current = new Paragraph();
            }

            private void ApplyHighlight(PendingHighlight pending)
            {
                try
                {
                    var start = pending.First.ContentStart.Offset;
                    var end = pending.Last.ContentEnd.Offset;
                    if (end <= start) return;

                    var highlighter = new TextHighlighter { Background = pending.Background };
                    if (pending.Foreground != null) highlighter.Foreground = pending.Foreground;
                    highlighter.Ranges.Add(new TextRange { StartIndex = start, Length = end - start });
                    _target.TextHighlighters.Add(highlighter);
                    if (pending.IsSpoiler) _state.AddSpoiler(highlighter);
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmInlineBuilder: highlighting MFM text failed", ex);
                }
            }

            public void Append(IReadOnlyList<MfmSegment> segments, MfmStyle style)
            {
                if (segments == null) return;

                foreach (var segment in segments)
                {
                    var followsBlock = _afterBlock;
                    _afterBlock = false;
                    AppendSegment(segment, style, followsBlock);
                }
            }

            private void AppendSegment(MfmSegment segment, MfmStyle style, bool followsBlock)
            {
                switch (segment.Kind)
                {
                    case MfmSegmentKind.Emoji:
                        Inlines.Add(EmojiInline(segment, _context.EmojiSize * style.Scale));
                        break;
                    case MfmSegmentKind.Link:
                    case MfmSegmentKind.Mention:
                    case MfmSegmentKind.Hashtag:
                        if (_context.Interactive) Inlines.Add(LinkInline(segment, style));
                        else AppendText(segment.Text, style);
                        break;
                    case MfmSegmentKind.InlineCode:
                    case MfmSegmentKind.Math:
                        Inlines.Add(InlineCodeRun(segment.Text, style));
                        break;
                    case MfmSegmentKind.CodeBlock:
                    case MfmSegmentKind.MathBlock:
                        if (!_context.Interactive)
                        {
                            AppendText(segment.Text, style);
                            break;
                        }

                        AddCard(CodeBlock(segment));
                        break;
                    case MfmSegmentKind.Quote:
                        var quote = new SocialQuoteBlock();
                        quote.Load(segment.Children, _target, _context.Interactive, _context.EmojiSize);
                        AddCard(quote);
                        break;
                    case MfmSegmentKind.Search:
                        if (!_context.Interactive)
                        {
                            AppendText(segment.Text, style);
                            break;
                        }

                        var search = new SocialSearchBlock();
                        search.Load(segment);
                        AddCard(search);
                        break;
                    case MfmSegmentKind.Center:
                        DropTrailingBreak();
                        FinishParagraph();
                        _current.TextAlignment = TextAlignment.Center;
                        Append(segment.Children, style);
                        FinishParagraph();
                        _afterBlock = true;
                        break;
                    case MfmSegmentKind.UnixTime:
                        Inlines.Add(UnixTimeInline(segment));
                        break;
                    case MfmSegmentKind.Bold:
                        Append(segment.Children, style.WithBold());
                        break;
                    case MfmSegmentKind.Italic:
                        Append(segment.Children, style.WithItalic());
                        break;
                    case MfmSegmentKind.Strike:
                        Append(segment.Children, style.WithStrike());
                        break;
                    case MfmSegmentKind.Small:
                        Append(segment.Children, style.WithSmall());
                        break;
                    case MfmSegmentKind.Function:
                        AppendFunction(segment, style);
                        break;
                    default:
                        AppendText(followsBlock ? WithoutLeadingBreak(segment.Text) : segment.Text, style);
                        break;
                }
            }

            private void AddCard(FrameworkElement card)
            {
                DropTrailingBreak();
                _state.AddCard(card);
                Inlines.Add(new InlineUIContainer { Child = card });
                _afterBlock = true;
            }

            private SocialCodeBlock CodeBlock(MfmSegment segment)
            {
                var block = new SocialCodeBlock();
                block.Load(segment, _state.IsDark);
                _state.AddCode(block);
                return block;
            }

            private void AppendFunction(MfmSegment function, MfmStyle style)
            {
                var name = function.FunctionName;
                Color color;
                switch (name)
                {
                    case "x2":
                    case "x3":
                    case "x4":
                        Append(function.Children, style.Scaled(name[1] - '0'));
                        return;
                    case "fg":
                        Append(function.Children, MfmEffects.TryParseColor(function.Argument("color"), out color)
                                                  ? style.WithForeground(new SolidColorBrush(color))
                                                  : style);
                        return;
                    case "bg":
                        if (!MfmEffects.TryParseColor(function.Argument("color"), out color))
                        {
                            Append(function.Children, style);
                            return;
                        }

                        var mark = _elements.Count;
                        Append(function.Children, style);
                        Highlight(mark, new SolidColorBrush(color), null, false);
                        return;
                    case "font":
                        var family = FontFor(function);
                        Append(function.Children, family == null ? style : style.WithFamily(family));
                        return;
                    case "blur":
                        AppendSpoiler(function, style);
                        return;
                    case "ruby":
                        AppendRuby(function, style);
                        return;
                }

                if (MfmEffects.IsAnimated(name))
                {
                    if (_context.Animate && !style.InBox)
                    {
                        AppendBox(function, style, name == "tada" ? MfmEffects.TadaScale : 1, true);
                    }
                    else
                    {
                        Append(function.Children, name == "tada" ? style.Scaled(MfmEffects.TadaScale) : style);
                    }
                    return;
                }

                if (MfmEffects.IsBoxed(name) && !style.InBox)
                {
                    AppendBox(function, style, 1, false);
                    return;
                }

                Append(function.Children, style);
            }

            private void Highlight(int mark, Brush background, Brush foreground, bool spoiler)
            {
                if (_elements.Count <= mark) return;

                _highlights.Add(new PendingHighlight
                {
                    First = _elements[mark],
                    Last = _elements[_elements.Count - 1],
                    Background = background,
                    Foreground = foreground,
                    IsSpoiler = spoiler
                });
            }

            private void AppendSpoiler(MfmSegment function, MfmStyle style)
            {
                var index = _spoilerIndex++;
                if (_state.IsRevealed(index))
                {
                    Append(function.Children, style);
                    return;
                }

                var text = MfmText.PlainText(function.Children);
                if (string.IsNullOrEmpty(text)) return;

                var brush = Spoiler(_state.IsDark);
                var run = new Run { Text = text.Replace('\n', ' ') };
                Apply(run, style);
                run.Foreground = brush;
                _state.AddSpoiler(run);

                var mark = _elements.Count;
                if (_context.Interactive)
                {
                    var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None, Foreground = brush };
                    AutomationProperties.SetName(link, LocalizedStrings.Get("SocialMfmSpoilerName"));
                    link.Inlines.Add(run);
                    link.Click += (sender, args) => _state.Reveal(index);
                    _state.AddSpoiler(link);
                    Inlines.Add(link);
                }
                else
                {
                    Inlines.Add(run);
                }

                _elements.Add(run);
                Highlight(mark, brush, brush, true);
            }

            private void AppendRuby(MfmSegment function, MfmStyle style)
            {
                var text = MfmText.PlainText(function.Children).Trim();
                var space = text.IndexOf(' ');
                var baseText = space < 0 ? text : text.Substring(0, space);
                var reading = space < 0 ? "" : text.Substring(space + 1).Trim();

                if (reading.Length == 0)
                {
                    AppendText(baseText, style);
                    return;
                }

                if (style.InBox)
                {
                    AppendText(baseText, style);
                    AppendText("(" + reading + ")", style.Scaled(RubyFallbackScale));
                    return;
                }

                var size = _context.BaseSize * style.Scale;
                Inlines.Add(new InlineUIContainer { Child = MfmEffects.Ruby(baseText, reading, size, _target) });
            }

            private void AppendBox(MfmSegment function, MfmStyle style, double sizeFactor, bool animated)
            {
                var size = _context.BaseSize * Math.Min(style.Scale * sizeFactor, MaximumScale);
                var inner = new RichTextBlock
                {
                    TextWrapping = TextWrapping.NoWrap,
                    IsTextSelectionEnabled = false,
                    FontSize = size,
                    FontFamily = _target.FontFamily
                };
                inner.SetBinding(RichTextBlock.ForegroundProperty, new Binding { Source = _target, Path = new PropertyPath("Foreground") });

                var innerState = StateOf(inner);
                innerState.Prepare(function.Children, new Context(_context.Interactive,
                                                                  _context.EmojiSize * size / _context.BaseSize,
                                                                  size,
                                                                  _context.Animate,
                                                                  style.ForBox()));
                Render(inner, innerState);

                Inlines.Add(new InlineUIContainer { Child = MfmEffects.Box(function, inner, size, animated) });
            }

            private void Apply(TextElement element, MfmStyle style)
            {
                if (style.Bold) element.FontWeight = FontWeights.Bold;
                if (style.Italic) element.FontStyle = FontStyle.Italic;
                if (style.Strike) element.TextDecorations = TextDecorations.Strikethrough;
                if (!style.Scale.Equals(1.0)) element.FontSize = _context.BaseSize * style.Scale;
                if (style.Family != null) element.FontFamily = style.Family;

                if (style.Foreground != null)
                {
                    element.Foreground = style.Foreground;
                }
                else if (style.Subtle)
                {
                    element.Foreground = Subtle(_state.IsDark);
                    _state.AddSubtle(element);
                }
            }

            private void DropTrailingBreak()
            {
                var inlines = Inlines;
                if (inlines.Count > 0 && inlines[inlines.Count - 1] is LineBreak)
                {
                    inlines.RemoveAt(inlines.Count - 1);
                }
            }

            private static string WithoutLeadingBreak(string text)
            {
                return !string.IsNullOrEmpty(text) && text[0] == '\n' ? text.Substring(1) : text;
            }

            private void AppendText(string text, MfmStyle style)
            {
                if (string.IsNullOrEmpty(text)) return;

                var start = 0;
                while (start <= text.Length)
                {
                    var lineEnd = text.IndexOf('\n', start);
                    var piece = lineEnd < 0 ? text.Substring(start) : text.Substring(start, lineEnd - start);
                    if (piece.Length > 0)
                    {
                        var run = new Run { Text = piece };
                        Apply(run, style);
                        Inlines.Add(run);
                        _elements.Add(run);
                    }

                    if (lineEnd < 0) break;

                    Inlines.Add(new LineBreak());
                    start = lineEnd + 1;
                }
            }

            private Inline InlineCodeRun(string text, MfmStyle style)
            {
                var run = new Run { Text = text };
                Apply(run, style);
                run.FontFamily = s_codeFont;
                if (style.Foreground == null)
                {
                    CodePalette.Paint(run, CodePalette.InlineCode(_state.IsDark));
                    _state.AddCodeRun(run);
                }
                _elements.Add(run);
                return run;
            }

            private Inline LinkInline(MfmSegment segment, MfmStyle style)
            {
                var run = new Run { Text = segment.Text };
                Apply(run, style.WithForeground(null));
                _elements.Add(run);

                var link = new Hyperlink();
                link.Inlines.Add(run);
                link.Click += (sender, args) => OnLinkClick(sender, segment);
                return link;
            }

            private static Inline UnixTimeInline(MfmSegment segment)
            {
                var chip = new SocialTimeChip();
                chip.Show(segment.Time);
                return new InlineUIContainer { Child = chip };
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

        private sealed class FillState
        {
            private readonly RichTextBlock _target;

            private readonly List<FrameworkElement> _cards = new List<FrameworkElement>();

            private readonly List<SocialCodeBlock> _codeBlocks = new List<SocialCodeBlock>();

            private readonly List<Run> _codeRuns = new List<Run>();

            private readonly List<TextElement> _subtle = new List<TextElement>();

            private readonly List<TextElement> _spoilerElements = new List<TextElement>();

            private readonly List<TextHighlighter> _spoilerHighlighters = new List<TextHighlighter>();

            private readonly HashSet<int> _revealed = new HashSet<int>();

            public FillState(RichTextBlock target)
            {
                _target = target;
                target.SizeChanged += OnSizeChanged;
                target.ActualThemeChanged += OnActualThemeChanged;
            }

            public IReadOnlyList<MfmSegment> Segments { get; private set; }

            public Context Context { get; private set; }

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

            public void Prepare(IReadOnlyList<MfmSegment> segments, Context context)
            {
                if (!ReferenceEquals(Segments, segments)) _revealed.Clear();
                Segments = segments;
                Context = context;
            }

            public void Reset()
            {
                _cards.Clear();
                _codeBlocks.Clear();
                _codeRuns.Clear();
                _subtle.Clear();
                _spoilerElements.Clear();
                _spoilerHighlighters.Clear();
            }

            public bool IsRevealed(int index)
            {
                return _revealed.Contains(index);
            }

            public void Reveal(int index)
            {
                MarkLinkInvoked();
                if (!_revealed.Add(index)) return;

                var ignored = _target.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        Render(_target, this);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("MfmInlineBuilder: revealing a spoiler failed", ex);
                    }
                });
            }

            public void AddCard(FrameworkElement card)
            {
                _cards.Add(card);

                var width = ContentWidth;
                if (width > 0) card.Width = width;
            }

            public void AddCode(SocialCodeBlock block)
            {
                _codeBlocks.Add(block);
            }

            public void AddCodeRun(Run run)
            {
                _codeRuns.Add(run);
            }

            public void AddSubtle(TextElement element)
            {
                _subtle.Add(element);
            }

            public void AddSpoiler(TextElement element)
            {
                _spoilerElements.Add(element);
            }

            public void AddSpoiler(TextHighlighter highlighter)
            {
                _spoilerHighlighters.Add(highlighter);
            }

            private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            {
                try
                {
                    var width = ContentWidth;
                    if (width <= 0) return;

                    foreach (var card in _cards)
                    {
                        card.Width = width;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmInlineBuilder: sizing a card failed", ex);
                }
            }

            private void OnActualThemeChanged(FrameworkElement sender, object args)
            {
                try
                {
                    var dark = IsDark;
                    foreach (var block in _codeBlocks)
                    {
                        block.Recolor(dark);
                    }

                    var inlineBrush = CodePalette.InlineCode(dark);
                    foreach (var run in _codeRuns)
                    {
                        CodePalette.Paint(run, inlineBrush);
                    }

                    var subtle = Subtle(dark);
                    foreach (var element in _subtle)
                    {
                        element.Foreground = subtle;
                    }

                    var spoiler = Spoiler(dark);
                    foreach (var element in _spoilerElements)
                    {
                        element.Foreground = spoiler;
                    }

                    foreach (var highlighter in _spoilerHighlighters)
                    {
                        highlighter.Background = spoiler;
                        highlighter.Foreground = spoiler;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("MfmInlineBuilder: recolouring MFM failed", ex);
                }
            }
        }
    }
}
