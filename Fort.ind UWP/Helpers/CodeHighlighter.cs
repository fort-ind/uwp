using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public enum CodeTokenKind : byte
    {
        Plain,
        Comment,
        String,
        Number,
        Keyword,
        Type,
        Function,
        Tag,
        Attribute,
        Punctuation
    }

    public struct CodeToken
    {
        public CodeToken(int start, int length, CodeTokenKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int Start { get; }

        public int Length { get; }

        public CodeTokenKind Kind { get; }
    }

    public sealed class CodeHighlight
    {
        public CodeHighlight(string label, string displayText, IReadOnlyList<CodeToken> tokens)
        {
            Label = label;
            DisplayText = displayText;
            Tokens = tokens;
        }

        public string Label { get; }

        public string DisplayText { get; }

        public IReadOnlyList<CodeToken> Tokens { get; }
    }

    public static class CodeHighlighter
    {
        public const int MaximumLength = 6000;

        private const int TabWidth = 4;

        private const int MaximumTagLength = 20;

        private const int MaximumScopeDepth = 32;

        private const int MaximumLoggedFailures = 3;

        private static readonly object s_gate = new object();

        private static readonly IReadOnlyList<CodeToken> s_noTokens = new CodeToken[0];

        private static readonly Dictionary<string, CodeTokenKind> s_kinds = BuildKinds();

        private static readonly Dictionary<string, string> s_aliases = BuildAliases();

        private static Collector s_collector;

        private static int s_failures;

        public static bool IsLanguageTag(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Length <= MaximumTagLength && text.All(IsTagCharacter);
        }

        public static CodeHighlight Analyze(string code, string tag)
        {
            var display = ExpandTabs(code ?? "");
            var label = "";

            try
            {
                ILanguage language;
                if (string.IsNullOrEmpty(tag))
                {
                    language = Guess(display);
                    if (language != null) label = language.Name;
                }
                else
                {
                    language = Resolve(tag);
                    label = language != null ? language.Name : tag;
                }

                var tokens = language != null && display.Length <= MaximumLength
                    ? Tokenize(display, language)
                    : s_noTokens;
                return new CodeHighlight(label, display, tokens);
            }
            catch (Exception ex)
            {
                if (Interlocked.Increment(ref s_failures) <= MaximumLoggedFailures)
                {
                    AppLog.Error("CodeHighlighter: colouring a code block failed", ex);
                }
                return new CodeHighlight(label, display, s_noTokens);
            }
        }

        private static bool IsTagCharacter(char ch)
        {
            return (ch >= 'a' && ch <= 'z')
                   || (ch >= 'A' && ch <= 'Z')
                   || (ch >= '0' && ch <= '9')
                   || ch == '+' || ch == '#' || ch == '-' || ch == '_' || ch == '.';
        }

        private static ILanguage Resolve(string tag)
        {
            string id;
            return s_aliases.TryGetValue(tag.ToLowerInvariant(), out id) ? Languages.FindById(id) : null;
        }

        private static ILanguage Guess(string code)
        {
            var trimmed = code.TrimStart();
            if (trimmed.Length < 4 || trimmed.Length > MaximumLength) return null;

            var first = trimmed[0];
            if (first == '<')
            {
                if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                {
                    return Languages.FindById(LanguageId.Xml);
                }

                var second = trimmed[1];
                var looksLikeMarkup = (char.IsLetter(second) || second == '!' || second == '/')
                                      && trimmed.IndexOf('>') > 1;
                return looksLikeMarkup ? Languages.FindById(LanguageId.Html) : null;
            }

            if (first == '{' || first == '[')
            {
                try
                {
                    JToken.Parse(trimmed);
                    return Languages.FindById(LanguageId.Json);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }

        private static string ExpandTabs(string code)
        {
            if (code.IndexOf('\t') < 0) return code;

            var builder = new StringBuilder(code.Length + 16);
            var column = 0;
            foreach (var ch in code)
            {
                if (ch == '\t')
                {
                    var pad = TabWidth - column % TabWidth;
                    builder.Append(' ', pad);
                    column += pad;
                    continue;
                }

                builder.Append(ch);
                column = ch == '\n' ? 0 : column + 1;
            }
            return builder.ToString();
        }

        private static IReadOnlyList<CodeToken> Tokenize(string code, ILanguage language)
        {
            var kinds = new CodeTokenKind[code.Length];
            bool complete;
            lock (s_gate)
            {
                if (s_collector == null) s_collector = new Collector();
                complete = s_collector.Run(code, language, kinds);
            }
            return complete ? Compact(kinds) : s_noTokens;
        }

        private static IReadOnlyList<CodeToken> Compact(CodeTokenKind[] kinds)
        {
            var tokens = new List<CodeToken>();
            var i = 0;
            while (i < kinds.Length)
            {
                var kind = kinds[i];
                var j = i + 1;
                while (j < kinds.Length && kinds[j] == kind) j++;

                if (kind != CodeTokenKind.Plain) tokens.Add(new CodeToken(i, j - i, kind));
                i = j;
            }
            return tokens;
        }

        private static void Paint(IList<Scope> scopes, CodeTokenKind[] kinds, int offset, int depth)
        {
            if (scopes == null || depth > MaximumScopeDepth) return;

            foreach (var scope in scopes)
            {
                CodeTokenKind kind;
                if (scope.Name != null && s_kinds.TryGetValue(scope.Name, out kind) && kind != CodeTokenKind.Plain)
                {
                    var start = offset + scope.Index;
                    var end = Math.Min(kinds.Length, start + scope.Length);
                    for (var i = Math.Max(0, start); i < end; i++)
                    {
                        kinds[i] = kind;
                    }
                }

                Paint(scope.Children, kinds, offset, depth + 1);
            }
        }

        private static void Map(Dictionary<string, CodeTokenKind> map, CodeTokenKind kind, params string[] names)
        {
            foreach (var name in names)
            {
                map[name] = kind;
            }
        }

        private static Dictionary<string, CodeTokenKind> BuildKinds()
        {
            var map = new Dictionary<string, CodeTokenKind>(StringComparer.Ordinal);
            Map(map, CodeTokenKind.Comment, ScopeName.Comment, ScopeName.HtmlComment, ScopeName.XmlComment,
                ScopeName.XmlDocComment, ScopeName.XmlDocTag);
            Map(map, CodeTokenKind.String, ScopeName.String, ScopeName.StringCSharpVerbatim, ScopeName.StringEscape,
                ScopeName.HtmlAttributeValue, ScopeName.XmlAttributeValue, ScopeName.XmlAttributeQuotes,
                ScopeName.XmlCDataSection, ScopeName.JsonString, ScopeName.CssPropertyValue, ScopeName.MarkdownCode);
            Map(map, CodeTokenKind.Number, ScopeName.Number, ScopeName.JsonNumber);
            Map(map, CodeTokenKind.Keyword, ScopeName.Keyword, ScopeName.ControlKeyword, ScopeName.PseudoKeyword,
                ScopeName.PreprocessorKeyword, ScopeName.JsonConst, ScopeName.BuiltinValue, ScopeName.MarkdownHeader);
            Map(map, CodeTokenKind.Type, ScopeName.ClassName, ScopeName.Type, ScopeName.TypeVariable,
                ScopeName.NameSpace, ScopeName.Constructor, ScopeName.PowerShellType, ScopeName.Predefined);
            Map(map, CodeTokenKind.Function, ScopeName.BuiltinFunction, ScopeName.SqlSystemFunction,
                ScopeName.PowerShellCommand, ScopeName.Intrinsic);
            Map(map, CodeTokenKind.Tag, ScopeName.HtmlElementName, ScopeName.XmlName, ScopeName.CssSelector);
            Map(map, CodeTokenKind.Attribute, ScopeName.HtmlAttributeName, ScopeName.XmlAttribute,
                ScopeName.CssPropertyName, ScopeName.JsonKey, ScopeName.Attribute, ScopeName.PowerShellAttribute,
                ScopeName.PowerShellParameter, ScopeName.PowerShellVariable);
            Map(map, CodeTokenKind.Punctuation, ScopeName.HtmlOperator, ScopeName.HtmlTagDelimiter,
                ScopeName.XmlDelimiter, ScopeName.Operator, ScopeName.Delimiter, ScopeName.PowerShellOperator,
                ScopeName.Brackets, ScopeName.SpecialCharacter, ScopeName.MarkdownListItem);
            return map;
        }

        private static Dictionary<string, string> BuildAliases()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            Alias(map, LanguageId.Html, "html", "htm", "xhtml", "svg");
            Alias(map, LanguageId.Xml, "xml", "xaml", "csproj", "xsd", "xslt", "plist", "rss", "atom");
            Alias(map, LanguageId.JavaScript, "js", "javascript", "mjs", "cjs", "jsx", "node", "nodejs");
            Alias(map, LanguageId.TypeScript, "ts", "typescript", "tsx");
            Alias(map, LanguageId.Json, "json", "jsonc");
            Alias(map, LanguageId.Css, "css");
            Alias(map, LanguageId.Python, "py", "python", "python3", "py3");
            Alias(map, LanguageId.CSharp, "cs", "csharp", "c#");
            Alias(map, LanguageId.Cpp, "c", "h", "cpp", "c++", "cc", "cxx", "hpp", "hh");
            Alias(map, LanguageId.Java, "java");
            Alias(map, LanguageId.Php, "php");
            Alias(map, LanguageId.PowerShell, "ps1", "powershell", "pwsh", "posh");
            Alias(map, LanguageId.Sql, "sql", "tsql", "mysql", "postgresql", "pgsql", "sqlite");
            Alias(map, LanguageId.VbDotNet, "vb", "vbnet", "vb.net");
            Alias(map, LanguageId.FSharp, "fs", "fsharp", "f#");
            Alias(map, LanguageId.Haskell, "hs", "haskell");
            Alias(map, LanguageId.Markdown, "md", "markdown");
            Alias(map, LanguageId.MatLab, "matlab");
            Alias(map, LanguageId.Fortran, "fortran");
            return map;
        }

        private static void Alias(Dictionary<string, string> map, string id, params string[] tags)
        {
            foreach (var tag in tags)
            {
                map[tag] = id;
            }
        }

        private sealed class Collector : CodeColorizerBase
        {
            private CodeTokenKind[] _kinds;

            private int _offset;

            public Collector()
                : base(null, null)
            {
            }

            public bool Run(string code, ILanguage language, CodeTokenKind[] kinds)
            {
                _kinds = kinds;
                _offset = 0;
                try
                {
                    languageParser.Parse(code, language, Write);
                    return _offset == code.Length;
                }
                finally
                {
                    _kinds = null;
                }
            }

            protected override void Write(string parsedSourceCode, IList<Scope> scopes)
            {
                Paint(scopes, _kinds, _offset, 0);
                _offset += parsedSourceCode.Length;
            }
        }
    }
}
