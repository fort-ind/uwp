using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fort.ind_UWP
{
    public enum MfmSegmentKind
    {
        Text,
        Link,
        Mention,
        Hashtag,
        Emoji,
        InlineCode,
        CodeBlock,
        UnixTime,
        Bold,
        Italic,
        Strike,
        Small,
        Function,
        Math,
        Center,
        Quote,
        MathBlock,
        Search
    }

    public sealed class MfmSegment
    {
        public MfmSegmentKind Kind { get; internal set; }

        public string Text { get; internal set; }

        public string Url { get; internal set; }

        public bool IsSilent { get; internal set; }

        public string Username { get; internal set; }

        public string Host { get; internal set; }

        public string UserId { get; internal set; }

        public string Tag { get; internal set; }

        public string EmojiName { get; internal set; }

        public Uri EmojiUri { get; internal set; }

        public string Language { get; internal set; }

        public string DisplayText { get; internal set; }

        public IReadOnlyList<CodeToken> CodeTokens { get; internal set; }

        public DateTimeOffset Time { get; internal set; }

        public IReadOnlyList<MfmSegment> Children { get; internal set; }

        public string FunctionName { get; internal set; }

        public IReadOnlyDictionary<string, string> Arguments { get; internal set; }

        public string Query { get; internal set; }

        public bool IsBlock
        {
            get
            {
                return Kind == MfmSegmentKind.CodeBlock || Kind == MfmSegmentKind.Quote || Kind == MfmSegmentKind.Center
                       || Kind == MfmSegmentKind.MathBlock || Kind == MfmSegmentKind.Search;
            }
        }

        public bool IsSpoiler
        {
            get { return Kind == MfmSegmentKind.Function && FunctionName == "blur"; }
        }

        public string Argument(string name)
        {
            string value;
            return Arguments != null && Arguments.TryGetValue(name, out value) ? value : null;
        }

        public bool HasArgument(string name)
        {
            return Arguments != null && Arguments.ContainsKey(name);
        }
    }

    public static class MfmText
    {
        private const int MaximumLength = 10000;

        private const int MaximumNesting = 16;

        private static readonly IReadOnlyList<MfmSegment> s_none = new MfmSegment[0];

        private static readonly IReadOnlyDictionary<string, string> s_noArguments = new Dictionary<string, string>();

        private static readonly Regex s_searchLine = new Regex(@"^(.+?)[ 　](検索|\[検索\]|Search|\[Search\])$",
                                                               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] s_innerTags = { "b", "i", "s", "small", "center" };

        public static IReadOnlyList<MfmSegment> Parse(string text,
                                                     IReadOnlyDictionary<string, string> noteEmojis,
                                                     IReadOnlyDictionary<string, Uri> localEmojis,
                                                     IReadOnlyDictionary<string, string> mentionHandles)
        {
            if (string.IsNullOrEmpty(text)) return s_none;

            text = text.Replace("\r\n", "\n");
            if (text.Length > MaximumLength)
            {
                return new[] { new MfmSegment { Kind = MfmSegmentKind.Text, Text = text } };
            }

            var scanner = new Scanner(text, new EmojiResolver(noteEmojis, localEmojis), MentionLookup(mentionHandles), true);
            scanner.Run(0, text.Length, 0, true);
            return scanner.Finish();
        }

        public static IReadOnlyList<MfmSegment> ParseName(string name, IReadOnlyDictionary<string, string> userEmojis)
        {
            if (string.IsNullOrEmpty(name)) return s_none;

            if (name.Length > MaximumLength)
            {
                return new[] { new MfmSegment { Kind = MfmSegmentKind.Text, Text = name } };
            }

            var scanner = new Scanner(name, new EmojiResolver(userEmojis, null), null, false);
            scanner.Run(0, name.Length, 0, false);
            return scanner.Finish();
        }

        public static string PlainText(IReadOnlyList<MfmSegment> segments)
        {
            if (segments == null || segments.Count == 0) return "";

            var builder = new StringBuilder();
            foreach (var segment in segments)
            {
                builder.Append(segment.Text);
            }
            return builder.ToString();
        }

        public static IEnumerable<MfmSegment> Descendants(IReadOnlyList<MfmSegment> segments)
        {
            if (segments == null) yield break;

            var pending = new Stack<IEnumerator<MfmSegment>>();
            pending.Push(segments.GetEnumerator());
            while (pending.Count > 0)
            {
                var current = pending.Peek();
                if (!current.MoveNext())
                {
                    pending.Pop().Dispose();
                    continue;
                }

                var segment = current.Current;
                yield return segment;
                if (segment.Children != null && segment.Children.Count > 0) pending.Push(segment.Children.GetEnumerator());
            }
        }

        public static string PlainName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var builder = new StringBuilder(name.Length);
            var i = 0;
            while (i < name.Length)
            {
                int end;
                if (name[i] == ':' && TryReadEmojiCode(name, i, name.Length, out end))
                {
                    i = end;
                    continue;
                }

                builder.Append(name[i]);
                i++;
            }

            return CollapseSpaces(builder.ToString());
        }

        public static string Preview(string text, IReadOnlyDictionary<string, string> mentionHandles)
        {
            var segments = Parse(text, null, null, mentionHandles);
            if (segments.Count == 0) return "";

            var builder = new StringBuilder();
            AppendPreview(builder, segments);
            return PlainName(builder.ToString());
        }

        private static void AppendPreview(StringBuilder builder, IReadOnlyList<MfmSegment> segments)
        {
            foreach (var segment in segments)
            {
                if (segment.Kind == MfmSegmentKind.Link)
                {
                    builder.Append(LinkPreviewText(segment));
                }
                else if (segment.Children != null && segment.Kind != MfmSegmentKind.Search)
                {
                    AppendPreview(builder, segment.Children);
                    if (segment.IsBlock) builder.Append('\n');
                }
                else
                {
                    builder.Append(segment.Text);
                }
            }
        }

        private static string LinkPreviewText(MfmSegment segment)
        {
            if (!string.Equals(segment.Text, segment.Url, StringComparison.Ordinal)) return segment.Text;

            var uri = WebLauncher.TryCreateWebUri(segment.Url);
            return uri == null ? segment.Text : uri.Host;
        }

        public static MfmSegment FirstPreviewLink(IReadOnlyList<MfmSegment> segments)
        {
            if (segments == null) return null;

            foreach (var segment in segments)
            {
                if (segment.Kind == MfmSegmentKind.Link)
                {
                    if (!segment.IsSilent) return segment;
                    continue;
                }

                if (segment.Children == null || segment.IsSpoiler) continue;

                var nested = FirstPreviewLink(segment.Children);
                if (nested != null) return nested;
            }

            return null;
        }

        private static string CollapseSpaces(string text)
        {
            var builder = new StringBuilder(text.Length);
            var pendingSpace = false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(ch);
            }
            return builder.ToString();
        }

        private static Dictionary<string, string> MentionLookup(IReadOnlyDictionary<string, string> mentionHandles)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (mentionHandles == null) return lookup;

            foreach (var pair in mentionHandles)
            {
                var handle = pair.Value;
                if (string.IsNullOrEmpty(handle) || string.IsNullOrEmpty(pair.Key)) continue;

                if (handle[0] != '@') handle = "@" + handle;
                lookup[handle] = pair.Key;

                var localSuffix = "@" + MisskeyAuthService.InstanceHost;
                if (handle.EndsWith(localSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    lookup[handle.Substring(0, handle.Length - localSuffix.Length)] = pair.Key;
                }
            }

            return lookup;
        }

        private static bool IsAsciiLetterOrDigit(char ch)
        {
            return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9');
        }

        private static bool IsEmojiNameChar(char ch)
        {
            return IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '+' || ch == '-';
        }

        private static bool IsFunctionNameChar(char ch)
        {
            return IsAsciiLetterOrDigit(ch) || ch == '_';
        }

        private static bool IsFunctionArgumentChar(char ch)
        {
            return IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == ',' || ch == '=' || ch == '-' || ch == '#';
        }

        private static bool IsEmphasisChar(char ch)
        {
            return IsAsciiLetterOrDigit(ch) || ch == ' ' || ch == '\t';
        }

        private static bool TryReadEmojiCode(string s, int start, int end, out int next)
        {
            next = start;
            if (start > 0 && IsAsciiLetterOrDigit(s[start - 1])) return false;

            var j = start + 1;
            while (j < end && IsEmojiNameChar(s[j])) j++;

            if (j == start + 1 || j >= end || s[j] != ':') return false;
            if (j + 1 < end && IsAsciiLetterOrDigit(s[j + 1])) return false;

            next = j + 1;
            return true;
        }

        private static IReadOnlyDictionary<string, string> ParseArguments(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return s_noArguments;

            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in raw.Split(','))
            {
                if (part.Length == 0) continue;

                var split = part.IndexOf('=');
                if (split < 0) arguments[part] = null;
                else if (split > 0) arguments[part.Substring(0, split)] = part.Substring(split + 1);
            }
            return arguments;
        }

        private sealed class EmojiResolver
        {
            private readonly IReadOnlyDictionary<string, string> _sources;

            private readonly IReadOnlyDictionary<string, Uri> _local;

            private Dictionary<string, string> _bareNames;

            public EmojiResolver(IReadOnlyDictionary<string, string> sources, IReadOnlyDictionary<string, Uri> local)
            {
                _sources = sources;
                _local = local;
            }

            public Uri Resolve(string name)
            {
                if (_sources != null && _sources.Count > 0)
                {
                    string source;
                    if (_sources.TryGetValue(name, out source) || BareNames().TryGetValue(name, out source))
                    {
                        var uri = SocialLinks.StaticEmojiUri(source);
                        if (uri != null) return uri;
                    }
                }

                Uri local;
                if (_local != null && _local.TryGetValue(name, out local)) return local;

                return null;
            }

            private Dictionary<string, string> BareNames()
            {
                if (_bareNames != null) return _bareNames;

                _bareNames = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in _sources)
                {
                    var at = pair.Key.IndexOf('@');
                    if (at <= 0) continue;

                    var bare = pair.Key.Substring(0, at);
                    if (!_bareNames.ContainsKey(bare)) _bareNames[bare] = pair.Value;
                }
                return _bareNames;
            }
        }

        private sealed class Scanner
        {
            private readonly string _s;

            private readonly EmojiResolver _emoji;

            private readonly Dictionary<string, string> _mentions;

            private readonly bool _full;

            private List<MfmSegment> _segments = new List<MfmSegment>();

            private StringBuilder _text = new StringBuilder();

            public Scanner(string s, EmojiResolver emoji, Dictionary<string, string> mentions, bool full)
            {
                _s = s;
                _emoji = emoji;
                _mentions = mentions;
                _full = full;
            }

            public IReadOnlyList<MfmSegment> Finish()
            {
                FlushText();
                return _segments;
            }

            public void Run(int start, int end, int depth, bool blocks)
            {
                var i = start;
                while (i < end)
                {
                    int next;
                    if ((_full && blocks && (i == start || _s[i - 1] == '\n') && TryBlock(i, end, depth, out next))
                        || (_full && TryInlineCode(i, end, out next))
                        || (_full && TryMath(i, end, out next))
                        || (_full && TryWrapped(i, end, depth, out next))
                        || (_full && TryLink(i, end, out next))
                        || (_full && TryMention(i, end, out next))
                        || (_full && TryHashtag(i, end, out next))
                        || TryEmoji(i, end, out next))
                    {
                        i = next;
                        continue;
                    }

                    _text.Append(_s[i]);
                    i++;
                }
            }

            private List<MfmSegment> Collect(int start, int end, int depth)
            {
                var outerSegments = _segments;
                var outerText = _text;
                _segments = new List<MfmSegment>();
                _text = new StringBuilder();
                try
                {
                    Run(start, end, depth, false);
                    FlushText();
                    return _segments;
                }
                finally
                {
                    _segments = outerSegments;
                    _text = outerText;
                }
            }

            private void AddContainer(MfmSegment segment, IReadOnlyList<MfmSegment> children)
            {
                FlushText();
                segment.Children = children;
                if (segment.Text == null) segment.Text = PlainText(children);
                _segments.Add(segment);
            }

            private bool StartsWith(int index, int end, string token)
            {
                return index + token.Length <= end && string.CompareOrdinal(_s, index, token, 0, token.Length) == 0;
            }

            private int IndexOf(string token, int from, int end)
            {
                if (from >= end) return -1;
                var found = _s.IndexOf(token, from, end - from, StringComparison.Ordinal);
                return found >= 0 && found + token.Length <= end ? found : -1;
            }

            private int LineEnd(int from, int end)
            {
                if (from >= end) return end;
                var found = _s.IndexOf('\n', from, end - from);
                return found < 0 ? end : found;
            }

            private bool TryBlock(int i, int end, int depth, out int next)
            {
                next = i;
                switch (_s[i])
                {
                    case '>':
                        if (TryQuote(i, end, depth, out next)) return true;
                        break;
                    case '<':
                        if (TryCenter(i, end, depth, out next)) return true;
                        break;
                    case '\\':
                        if (TryMathBlock(i, end, out next)) return true;
                        break;
                }

                return TrySearch(i, end, out next);
            }

            private bool TryQuote(int i, int end, int depth, out int next)
            {
                next = i;
                if (depth + 1 >= MaximumNesting) return false;

                var lines = new List<string>();
                var cursor = i;
                var stop = i;
                while (cursor < end && _s[cursor] == '>')
                {
                    var lineEnd = LineEnd(cursor, end);
                    var line = _s.Substring(cursor + 1, lineEnd - cursor - 1);
                    if (line.Length > 0 && line[0] == ' ') line = line.Substring(1);
                    lines.Add(line);
                    stop = lineEnd;
                    cursor = lineEnd + 1;
                }

                var content = string.Join("\n", lines);
                if (string.IsNullOrWhiteSpace(content)) return false;

                var inner = new Scanner(content, _emoji, _mentions, true);
                inner.Run(0, content.Length, depth + 1, true);

                AddContainer(new MfmSegment { Kind = MfmSegmentKind.Quote }, inner.Finish());
                next = stop;
                return true;
            }

            private bool TryCenter(int i, int end, int depth, out int next)
            {
                next = i;
                if (depth + 1 >= MaximumNesting || !StartsWith(i, end, "<center>")) return false;

                var innerStart = i + "<center>".Length;
                var close = IndexOf("</center>", innerStart, end);
                if (close < 0) return false;

                var after = close + "</center>".Length;
                if (after < end && _s[after] != '\n') return false;

                if (innerStart < close && _s[innerStart] == '\n') innerStart++;
                var innerEnd = close;
                if (innerEnd > innerStart && _s[innerEnd - 1] == '\n') innerEnd--;

                var children = Collect(innerStart, innerEnd, depth + 1);
                if (children.Count == 0) return false;

                AddContainer(new MfmSegment { Kind = MfmSegmentKind.Center }, children);
                next = after;
                return true;
            }

            private bool TryMathBlock(int i, int end, out int next)
            {
                next = i;
                if (!StartsWith(i, end, "\\[")) return false;

                var close = IndexOf("\\]", i + 2, end);
                if (close < 0) return false;

                var after = close + 2;
                if (after < end && _s[after] != '\n') return false;

                var formula = _s.Substring(i + 2, close - i - 2).Trim('\n');
                if (string.IsNullOrWhiteSpace(formula)) return false;

                FlushText();
                _segments.Add(new MfmSegment { Kind = MfmSegmentKind.MathBlock, Text = formula, DisplayText = formula.Replace("\t", "    ") });
                next = after;
                return true;
            }

            private bool TrySearch(int i, int end, out int next)
            {
                next = i;
                var lineEnd = LineEnd(i, end);
                if (lineEnd == i) return false;

                var line = _s.Substring(i, lineEnd - i);
                var match = s_searchLine.Match(line);
                if (!match.Success) return false;

                var query = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(query)) return false;

                FlushText();
                _segments.Add(new MfmSegment { Kind = MfmSegmentKind.Search, Text = line, Query = query.Trim() });
                next = lineEnd;
                return true;
            }

            private bool TryInlineCode(int i, int end, out int next)
            {
                next = i;

                if (StartsWith(i, end, "```"))
                {
                    var close = IndexOf("```", i + 3, end);
                    if (close < 0) return false;

                    var inner = _s.Substring(i + 3, close - i - 3);
                    var tag = "";
                    var firstBreak = inner.IndexOf('\n');
                    var isBlock = firstBreak >= 0;
                    if (isBlock)
                    {
                        var firstLine = inner.Substring(0, firstBreak).Trim();
                        if (firstLine.Length == 0 || CodeHighlighter.IsLanguageTag(firstLine))
                        {
                            tag = firstLine;
                            inner = inner.Substring(firstBreak + 1);
                        }
                    }

                    var code = inner.TrimEnd('\n');
                    next = close + 3;
                    if (code.Length == 0) return true;

                    FlushText();
                    if (isBlock)
                    {
                        var highlight = CodeHighlighter.Analyze(code, tag);
                        _segments.Add(new MfmSegment
                        {
                            Kind = MfmSegmentKind.CodeBlock,
                            Text = code,
                            Language = highlight.Label,
                            DisplayText = highlight.DisplayText,
                            CodeTokens = highlight.Tokens
                        });
                    }
                    else
                    {
                        _segments.Add(new MfmSegment { Kind = MfmSegmentKind.InlineCode, Text = code });
                    }
                    return true;
                }

                if (_s[i] == '`')
                {
                    var close = IndexOf("`", i + 1, end);
                    if (close <= i + 1) return false;
                    if (_s.IndexOf('\n', i + 1, close - i - 1) >= 0) return false;

                    FlushText();
                    _segments.Add(new MfmSegment { Kind = MfmSegmentKind.InlineCode, Text = _s.Substring(i + 1, close - i - 1) });
                    next = close + 1;
                    return true;
                }

                if (StartsWith(i, end, "<plain>"))
                {
                    var close = IndexOf("</plain>", i + 7, end);
                    if (close < 0) return false;

                    _text.Append(_s, i + 7, close - i - 7);
                    next = close + 8;
                    return true;
                }

                return false;
            }

            private bool TryMath(int i, int end, out int next)
            {
                next = i;
                if (!StartsWith(i, end, "\\(")) return false;

                var close = IndexOf("\\)", i + 2, end);
                if (close <= i + 2) return false;
                if (_s.IndexOf('\n', i + 2, close - i - 2) >= 0) return false;

                FlushText();
                _segments.Add(new MfmSegment { Kind = MfmSegmentKind.Math, Text = _s.Substring(i + 2, close - i - 2) });
                next = close + 2;
                return true;
            }

            private bool TryWrapped(int i, int end, int depth, out int next)
            {
                next = i;
                if (depth + 1 >= MaximumNesting) return false;

                if (StartsWith(i, end, "$[")) return TryFunction(i, end, depth, out next);

                if (StartsWith(i, end, "***") && TryDelimited(i, end, depth, "***", new MfmSegment { Kind = MfmSegmentKind.Function, FunctionName = "tada", Arguments = s_noArguments }, out next))
                {
                    return true;
                }

                if (StartsWith(i, end, "**") && TryDelimited(i, end, depth, "**", new MfmSegment { Kind = MfmSegmentKind.Bold }, out next))
                {
                    return true;
                }

                if (StartsWith(i, end, "~~"))
                {
                    var close = IndexOf("~~", i + 2, end);
                    if (close <= i + 2 || _s.IndexOf('\n', i + 2, close - i - 2) >= 0) return false;

                    return TryDelimited(i, end, depth, "~~", new MfmSegment { Kind = MfmSegmentKind.Strike }, out next);
                }

                if (StartsWith(i, end, "__") && TryEmphasis(i, end, "__", MfmSegmentKind.Bold, out next)) return true;

                if ((_s[i] == '*' || _s[i] == '_') && !(i > 0 && IsAsciiLetterOrDigit(_s[i - 1])))
                {
                    return TryEmphasis(i, end, _s[i].ToString(), MfmSegmentKind.Italic, out next);
                }

                if (_s[i] == '<')
                {
                    foreach (var tag in s_innerTags)
                    {
                        var open = "<" + tag + ">";
                        if (!StartsWith(i, end, open)) continue;

                        var closeTag = "</" + tag + ">";
                        var close = IndexOf(closeTag, i + open.Length, end);
                        if (close < 0) return false;

                        if (tag == "center")
                        {
                            Run(i + open.Length, close, depth + 1, false);
                        }
                        else
                        {
                            var children = Collect(i + open.Length, close, depth + 1);
                            if (children.Count == 0) return false;

                            AddContainer(new MfmSegment { Kind = TagKind(tag) }, children);
                        }

                        next = close + closeTag.Length;
                        return true;
                    }
                }

                return false;
            }

            private static MfmSegmentKind TagKind(string tag)
            {
                switch (tag)
                {
                    case "b":
                        return MfmSegmentKind.Bold;
                    case "i":
                        return MfmSegmentKind.Italic;
                    case "s":
                        return MfmSegmentKind.Strike;
                    default:
                        return MfmSegmentKind.Small;
                }
            }

            private bool TryDelimited(int i, int end, int depth, string marker, MfmSegment segment, out int next)
            {
                next = i;
                var close = IndexOf(marker, i + marker.Length, end);
                if (close <= i + marker.Length) return false;

                var children = Collect(i + marker.Length, close, depth + 1);
                if (children.Count == 0) return false;

                AddContainer(segment, children);
                next = close + marker.Length;
                return true;
            }

            private bool TryEmphasis(int i, int end, string marker, MfmSegmentKind kind, out int next)
            {
                next = i;
                var start = i + marker.Length;
                var stop = start;
                while (stop < end && IsEmphasisChar(_s[stop])) stop++;

                if (stop == start || !StartsWith(stop, end, marker)) return false;
                if (marker.Length == 1 && StartsWith(stop, end, marker + marker)) return false;

                var text = _s.Substring(start, stop - start);
                AddContainer(new MfmSegment { Kind = kind }, new[] { new MfmSegment { Kind = MfmSegmentKind.Text, Text = text } });
                next = stop + marker.Length;
                return true;
            }

            private bool TryFunction(int i, int end, int depth, out int next)
            {
                next = i;

                var nameEnd = i + 2;
                while (nameEnd < end && IsFunctionNameChar(_s[nameEnd])) nameEnd++;
                if (nameEnd == i + 2) return false;

                var functionName = _s.Substring(i + 2, nameEnd - i - 2);
                var cursor = nameEnd;
                string rawArguments = null;
                if (cursor < end && _s[cursor] == '.')
                {
                    var argumentEnd = cursor + 1;
                    while (argumentEnd < end && IsFunctionArgumentChar(_s[argumentEnd])) argumentEnd++;
                    if (argumentEnd == cursor + 1) return false;

                    rawArguments = _s.Substring(cursor + 1, argumentEnd - cursor - 1);
                    cursor = argumentEnd;
                }

                if (cursor >= end || _s[cursor] != ' ') return false;

                var close = MatchingBracket(i + 1, end);
                if (close < 0) return false;

                DateTimeOffset moment;
                if (functionName == "unixtime"
                    && UnixTimeText.TryParse(_s.Substring(cursor + 1, close - cursor - 1), out moment))
                {
                    FlushText();
                    _segments.Add(new MfmSegment
                    {
                        Kind = MfmSegmentKind.UnixTime,
                        Text = RelativeTime.Detailed(moment),
                        Time = moment
                    });
                    next = close + 1;
                    return true;
                }

                var children = Collect(cursor + 1, close, depth + 1);
                AddContainer(new MfmSegment
                {
                    Kind = MfmSegmentKind.Function,
                    FunctionName = functionName,
                    Arguments = ParseArguments(rawArguments)
                }, children);
                next = close + 1;
                return true;
            }

            private int MatchingBracket(int openIndex, int end)
            {
                var level = 0;
                for (var j = openIndex; j < end; j++)
                {
                    if (_s[j] == '[') level++;
                    else if (_s[j] == ']')
                    {
                        level--;
                        if (level == 0) return j;
                    }
                }
                return -1;
            }

            private bool TryLink(int i, int end, out int next)
            {
                next = i;
                var ch = _s[i];

                if (ch == '[' || (ch == '?' && i + 1 < end && _s[i + 1] == '['))
                {
                    var silent = ch == '?';
                    var labelStart = silent ? i + 2 : i + 1;
                    var labelEnd = _s.IndexOf(']', labelStart, end - labelStart);
                    if (labelEnd <= labelStart || labelEnd + 1 >= end || _s[labelEnd + 1] != '(') return false;
                    if (_s.IndexOf('\n', labelStart, labelEnd - labelStart) >= 0) return false;

                    var urlStart = labelEnd + 2;
                    var urlEnd = urlStart;
                    while (urlEnd < end && _s[urlEnd] != ')' && !char.IsWhiteSpace(_s[urlEnd])) urlEnd++;
                    if (urlEnd >= end || _s[urlEnd] != ')') return false;

                    var url = _s.Substring(urlStart, urlEnd - urlStart);
                    if (WebLauncher.TryCreateWebUri(url) == null) return false;

                    AddLink(_s.Substring(labelStart, labelEnd - labelStart), url, silent);
                    next = urlEnd + 1;
                    return true;
                }

                if (ch == '<' && (StartsWith(i, end, "<https://") || StartsWith(i, end, "<http://")))
                {
                    var close = _s.IndexOf('>', i + 1, end - i - 1);
                    if (close < 0) return false;

                    var url = _s.Substring(i + 1, close - i - 1);
                    if (url.IndexOf(' ') >= 0 || WebLauncher.TryCreateWebUri(url) == null) return false;

                    AddLink(url, url, false);
                    next = close + 1;
                    return true;
                }

                if ((ch == 'h' || ch == 'H') && (StartsWith(i, end, "https://") || StartsWith(i, end, "http://")))
                {
                    if (i > 0 && IsAsciiLetterOrDigit(_s[i - 1])) return false;

                    var j = i;
                    var parens = 0;
                    var brackets = 0;
                    while (j < end)
                    {
                        var c = _s[j];
                        if (c == '(') parens++;
                        else if (c == ')') { if (parens == 0) break; parens--; }
                        else if (c == '[') brackets++;
                        else if (c == ']') { if (brackets == 0) break; brackets--; }
                        else if (!IsUrlChar(c)) break;
                        j++;
                    }

                    while (j > i && (_s[j - 1] == '.' || _s[j - 1] == ',')) j--;

                    var url = _s.Substring(i, j - i);
                    if (WebLauncher.TryCreateWebUri(url) == null) return false;

                    AddLink(url, url, false);
                    next = j;
                    return true;
                }

                return false;
            }

            private static bool IsUrlChar(char c)
            {
                if (IsAsciiLetterOrDigit(c)) return true;
                switch (c)
                {
                    case '.':
                    case ',':
                    case '_':
                    case '/':
                    case ':':
                    case '%':
                    case '#':
                    case '@':
                    case '$':
                    case '&':
                    case '?':
                    case '!':
                    case '~':
                    case '=':
                    case '+':
                    case '-':
                    case '*':
                    case ';':
                        return true;
                    default:
                        return c > 127 && !char.IsWhiteSpace(c) && !char.IsPunctuation(c);
                }
            }

            private void AddLink(string label, string url, bool silent)
            {
                FlushText();
                _segments.Add(new MfmSegment { Kind = MfmSegmentKind.Link, Text = label, Url = url, IsSilent = silent });
            }

            private bool TryMention(int i, int end, out int next)
            {
                next = i;
                if (_s[i] != '@') return false;
                if (i > 0 && (IsAsciiLetterOrDigit(_s[i - 1]) || _s[i - 1] == '_')) return false;

                var userEnd = ReadHandlePart(i + 1, end, false);
                if (userEnd == i + 1) return false;

                var username = _s.Substring(i + 1, userEnd - i - 1);
                string host = null;
                var stop = userEnd;

                if (userEnd < end && _s[userEnd] == '@')
                {
                    var hostEnd = ReadHandlePart(userEnd + 1, end, true);
                    if (hostEnd > userEnd + 1)
                    {
                        host = _s.Substring(userEnd + 1, hostEnd - userEnd - 1);
                        stop = hostEnd;
                    }
                }

                var typed = _s.Substring(i, stop - i);
                string userId = null;
                if (_mentions != null) _mentions.TryGetValue(typed, out userId);

                if (string.Equals(host, MisskeyAuthService.InstanceHost, StringComparison.OrdinalIgnoreCase)) host = null;

                FlushText();
                _segments.Add(new MfmSegment
                {
                    Kind = MfmSegmentKind.Mention,
                    Text = typed,
                    Username = username,
                    Host = host,
                    UserId = userId
                });
                next = stop;
                return true;
            }

            private int ReadHandlePart(int start, int end, bool isHost)
            {
                var j = start;
                while (j < end)
                {
                    var c = _s[j];
                    if (IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.') j++;
                    else break;
                }

                while (j > start && (_s[j - 1] == '.' || _s[j - 1] == '-')) j--;

                if (isHost && j > start && _s.IndexOf('.', start, j - start) < 0) return start;

                return j;
            }

            private bool TryHashtag(int i, int end, out int next)
            {
                next = i;
                if (_s[i] != '#') return false;
                if (i > 0 && (IsAsciiLetterOrDigit(_s[i - 1]) || _s[i - 1] == '&')) return false;

                var j = i + 1;
                while (j < end && !IsHashtagStop(_s[j])) j++;
                if (j == i + 1) return false;

                var tag = _s.Substring(i + 1, j - i - 1);
                if (tag.All(c => c >= '0' && c <= '9')) return false;

                FlushText();
                _segments.Add(new MfmSegment
                {
                    Kind = MfmSegmentKind.Hashtag,
                    Text = "#" + tag,
                    Tag = tag,
                    Url = SocialLinks.TagUrl(tag)
                });
                next = j;
                return true;
            }

            private static bool IsHashtagStop(char c)
            {
                if (char.IsWhiteSpace(c)) return true;
                switch (c)
                {
                    case '.':
                    case ',':
                    case '!':
                    case '?':
                    case '\'':
                    case '"':
                    case '#':
                    case ':':
                    case '/':
                    case '[':
                    case ']':
                    case '(':
                    case ')':
                    case '<':
                    case '>':
                    case '「':
                    case '」':
                    case '【':
                    case '】':
                    case '（':
                    case '）':
                        return true;
                    default:
                        return false;
                }
            }

            private bool TryEmoji(int i, int end, out int next)
            {
                next = i;
                if (_s[i] != ':') return false;

                int codeEnd;
                if (!TryReadEmojiCode(_s, i, end, out codeEnd)) return false;

                var name = _s.Substring(i + 1, codeEnd - i - 2);
                var uri = _emoji.Resolve(name);
                if (uri == null) return false;

                FlushText();
                _segments.Add(new MfmSegment
                {
                    Kind = MfmSegmentKind.Emoji,
                    Text = ":" + name + ":",
                    EmojiName = name,
                    EmojiUri = uri
                });
                next = codeEnd;
                return true;
            }

            private void FlushText()
            {
                if (_text.Length == 0) return;

                var text = _text.ToString();
                _text.Clear();

                var last = _segments.Count > 0 ? _segments[_segments.Count - 1] : null;
                if (last != null && last.Kind == MfmSegmentKind.Text)
                {
                    last.Text += text;
                    return;
                }

                _segments.Add(new MfmSegment { Kind = MfmSegmentKind.Text, Text = text });
            }
        }
    }
}
