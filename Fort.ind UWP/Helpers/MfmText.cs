using System;
using System.Collections.Generic;
using System.Text;

namespace Fort.ind_UWP
{
    public enum MfmSegmentKind
    {
        Text,
        Link,
        Mention,
        Hashtag,
        Emoji
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
    }

    public static class MfmText
    {
        private const int MaximumLength = 10000;

        private const int MaximumNesting = 16;

        private static readonly IReadOnlyList<MfmSegment> s_none = new MfmSegment[0];

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
            scanner.Run(0, text.Length, 0);
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
            scanner.Run(0, name.Length, 0);
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

        public static MfmSegment FirstPreviewLink(IReadOnlyList<MfmSegment> segments)
        {
            if (segments == null) return null;

            foreach (var segment in segments)
            {
                if (segment.Kind == MfmSegmentKind.Link && !segment.IsSilent) return segment;
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

            private readonly List<MfmSegment> _segments = new List<MfmSegment>();

            private readonly StringBuilder _text = new StringBuilder();

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

            public void Run(int start, int end, int depth)
            {
                var i = start;
                while (i < end)
                {
                    int next;
                    if ((_full && TryInlineCode(i, end, out next))
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

            private bool TryInlineCode(int i, int end, out int next)
            {
                next = i;

                if (StartsWith(i, end, "```"))
                {
                    var close = IndexOf("```", i + 3, end);
                    if (close < 0) return false;

                    var inner = _s.Substring(i + 3, close - i - 3);
                    var firstBreak = inner.IndexOf('\n');
                    if (firstBreak >= 0 && inner.Substring(0, firstBreak).Trim().IndexOf(' ') < 0)
                    {
                        inner = inner.Substring(firstBreak + 1);
                    }
                    _text.Append(inner.TrimEnd('\n'));
                    next = close + 3;
                    return true;
                }

                if (_s[i] == '`')
                {
                    var close = IndexOf("`", i + 1, end);
                    if (close <= i + 1) return false;
                    if (_s.IndexOf('\n', i + 1, close - i - 1) >= 0) return false;

                    _text.Append(_s, i + 1, close - i - 1);
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

            private bool TryWrapped(int i, int end, int depth, out int next)
            {
                next = i;
                if (depth >= MaximumNesting) return false;

                if (StartsWith(i, end, "$["))
                {
                    var nameEnd = i + 2;
                    while (nameEnd < end && (IsAsciiLetterOrDigit(_s[nameEnd]) || _s[nameEnd] == '_' || _s[nameEnd] == '.'
                                             || _s[nameEnd] == '=' || _s[nameEnd] == ',' || _s[nameEnd] == '-'))
                    {
                        nameEnd++;
                    }
                    if (nameEnd == i + 2 || nameEnd >= end || _s[nameEnd] != ' ') return false;

                    var close = MatchingBracket(i + 1, end);
                    if (close < 0) return false;

                    Run(nameEnd + 1, close, depth + 1);
                    next = close + 1;
                    return true;
                }

                if (StartsWith(i, end, "**") || StartsWith(i, end, "~~"))
                {
                    var marker = _s.Substring(i, 2);
                    var close = IndexOf(marker, i + 2, end);
                    if (close <= i + 2) return false;

                    Run(i + 2, close, depth + 1);
                    next = close + 2;
                    return true;
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

                        Run(i + open.Length, close, depth + 1);
                        next = close + closeTag.Length;
                        return true;
                    }
                }

                return false;
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
                var allDigits = true;
                foreach (var c in tag)
                {
                    if (c < '0' || c > '9')
                    {
                        allDigits = false;
                        break;
                    }
                }
                if (allDigits) return false;

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
