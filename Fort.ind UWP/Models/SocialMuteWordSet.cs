using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialMuteWordSet
    {
        private static readonly Regex s_regexLine = new Regex("^/(.+)/(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

        private const int MaxMatchesPerRule = 5;

        private readonly List<Rule> _rules;

        private SocialMuteWordSet(List<Rule> rules)
        {
            _rules = rules;
        }

        private sealed class Rule
        {
            public string[] Keywords;

            public string Source;

            public Regex Pattern;
        }

        public static SocialMuteWordSet Empty { get; } = new SocialMuteWordSet(new List<Rule>());

        public int Count
        {
            get { return _rules.Count; }
        }

        public static SocialMuteWordSet FromJson(JsonArray array)
        {
            if (array == null || array.Count == 0) return Empty;

            var rules = new List<Rule>();
            foreach (var entry in array)
            {
                if (entry.ValueType == JsonValueType.Array)
                {
                    var words = new List<string>();
                    foreach (var word in entry.GetArray())
                    {
                        if (word.ValueType == JsonValueType.String && word.GetString().Length > 0) words.Add(word.GetString());
                    }

                    if (words.Count > 0) rules.Add(new Rule { Keywords = words.ToArray() });
                }
                else if (entry.ValueType == JsonValueType.String)
                {
                    var rule = RegexRule(entry.GetString());
                    if (rule != null) rules.Add(rule);
                }
            }

            return rules.Count == 0 ? Empty : new SocialMuteWordSet(rules);
        }

        public static SocialMuteWordSet FromEditorText(string text, out string badLine)
        {
            badLine = null;
            var rules = new List<Rule>();
            foreach (var raw in SplitLines(text))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (s_regexLine.IsMatch(line))
                {
                    var rule = RegexRule(line);
                    if (rule == null)
                    {
                        badLine = line;
                        return null;
                    }

                    rules.Add(rule);
                }
                else
                {
                    var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 0) rules.Add(new Rule { Keywords = words });
                }
            }

            return rules.Count == 0 ? Empty : new SocialMuteWordSet(rules);
        }

        public JsonArray ToJson()
        {
            var array = new JsonArray();
            foreach (var rule in _rules)
            {
                if (rule.Pattern != null)
                {
                    array.Add(JsonValue.CreateStringValue(rule.Source));
                    continue;
                }

                var words = new JsonArray();
                foreach (var word in rule.Keywords)
                {
                    words.Add(JsonValue.CreateStringValue(word));
                }

                array.Add(words);
            }

            return array;
        }

        public string ToEditorText()
        {
            var text = new StringBuilder();
            foreach (var rule in _rules)
            {
                if (text.Length > 0) text.Append('\r');
                text.Append(rule.Pattern != null ? rule.Source : string.Join(" ", rule.Keywords));
            }

            return text.ToString();
        }

        public bool SameAs(SocialMuteWordSet other)
        {
            if (other == null) return false;

            return string.Equals(ToEditorText(), other.ToEditorText(), StringComparison.Ordinal);
        }

        public IReadOnlyList<string> Match(string text)
        {
            if (_rules.Count == 0 || string.IsNullOrEmpty(text)) return null;

            List<string> matched = null;
            foreach (var rule in _rules)
            {
                if (rule.Pattern == null)
                {
                    if (rule.Keywords.All(word => text.IndexOf(word, StringComparison.Ordinal) >= 0))
                    {
                        Add(ref matched, string.Join(" ", rule.Keywords));
                    }

                    continue;
                }

                try
                {
                    var seen = 0;
                    var match = rule.Pattern.Match(text);
                    while (match.Success && seen < MaxMatchesPerRule)
                    {
                        if (match.Value.Length > 0)
                        {
                            Add(ref matched, match.Value);
                            seen++;
                        }

                        match = match.NextMatch();
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    AppLog.Warning("SocialMuteWordSet: a muted-word pattern timed out and was skipped");
                }
            }

            return matched;
        }

        private static void Add(ref List<string> matched, string value)
        {
            if (matched == null) matched = new List<string>();
            if (!matched.Contains(value, StringComparer.Ordinal)) matched.Add(value);
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static Rule RegexRule(string source)
        {
            var parts = s_regexLine.Match(source ?? "");
            if (!parts.Success) return null;

            var options = RegexOptions.CultureInvariant;
            var flags = parts.Groups[2].Value;
            if (flags.IndexOf('i') >= 0) options |= RegexOptions.IgnoreCase;
            if (flags.IndexOf('m') >= 0) options |= RegexOptions.Multiline;
            if (flags.IndexOf('s') >= 0) options |= RegexOptions.Singleline;

            try
            {
                return new Rule { Source = source, Pattern = new Regex(parts.Groups[1].Value, options, MatchTimeout) };
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
