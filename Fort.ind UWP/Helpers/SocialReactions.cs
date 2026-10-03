using System;
using System.Collections.Generic;

namespace Fort.ind_UWP
{
    public static class SocialReactions
    {
        public const string Like = "❤";

        private const char VariationSelector = '️';

        private const char ZeroWidthJoiner = '‍';

        private const string LocalHost = ".";

        private static readonly Dictionary<string, string> s_legacy = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "like", "\U0001F44D" },
            { "love", "❤" },
            { "laugh", "\U0001F606" },
            { "hmm", "\U0001F914" },
            { "surprise", "\U0001F62E" },
            { "congrats", "\U0001F389" },
            { "angry", "\U0001F4A2" },
            { "confused", "\U0001F625" },
            { "rip", "\U0001F607" },
            { "pudding", "\U0001F36E" },
            { "star", "⭐" }
        };

        public static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            string legacy;
            if (s_legacy.TryGetValue(key, out legacy)) return legacy;

            string name, host;
            if (TryParseCustom(key, out name, out host))
            {
                return ":" + name + "@" + (host ?? LocalHost) + ":";
            }

            if (key.IndexOf(ZeroWidthJoiner) >= 0 || key.IndexOf(VariationSelector) < 0) return key;

            return key.Replace(VariationSelector.ToString(), "");
        }

        public static bool AreSame(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
        }

        public static bool IsLike(string key)
        {
            return string.Equals(Normalize(key), Like, StringComparison.Ordinal);
        }

        public static bool IsCustom(string key)
        {
            string name, host;
            return TryParseCustom(key, out name, out host);
        }

        public static string CustomName(string key)
        {
            string name, host;
            return TryParseCustom(key, out name, out host) ? name : null;
        }

        public static bool IsRemoteCustom(string key)
        {
            string name, host;
            return TryParseCustom(key, out name, out host) && host != null;
        }

        public static bool CanReactWith(string key)
        {
            return !string.IsNullOrEmpty(key) && !IsRemoteCustom(key);
        }

        public static string ToRequestKey(string key)
        {
            string name, host;
            if (!TryParseCustom(key, out name, out host)) return Normalize(key);

            return host == null ? ":" + name + ":" : ":" + name + "@" + host + ":";
        }

        public static Uri ImageUri(string key, IReadOnlyDictionary<string, string> reactionEmojis, IReadOnlyDictionary<string, Uri> localEmojis)
        {
            string name, host;
            if (!TryParseCustom(key, out name, out host)) return null;

            if (host == null)
            {
                Uri local;
                return localEmojis != null && localEmojis.TryGetValue(name, out local) ? local : null;
            }

            string url;
            if (reactionEmojis == null || !reactionEmojis.TryGetValue(name + "@" + host, out url)) return null;

            return SocialLinks.StaticEmojiUri(url);
        }

        public static string SpokenName(string key)
        {
            var name = CustomName(key);
            return name != null ? ":" + name + ":" : Normalize(key) ?? "";
        }

        private static bool TryParseCustom(string key, out string name, out string host)
        {
            name = null;
            host = null;
            if (string.IsNullOrEmpty(key) || key.Length < 3 || key[0] != ':' || key[key.Length - 1] != ':') return false;

            var inner = key.Substring(1, key.Length - 2);
            var at = inner.IndexOf('@');
            var candidate = at >= 0 ? inner.Substring(0, at) : inner;
            if (candidate.Length == 0 || !IsEmojiName(candidate)) return false;

            if (at >= 0)
            {
                var hostPart = inner.Substring(at + 1);
                if (hostPart.Length == 0) return false;
                host = hostPart == LocalHost ? null : hostPart;
            }

            name = candidate;
            return true;
        }

        private static bool IsEmojiName(string name)
        {
            foreach (var ch in name)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '+' || ch == '-')) return false;
            }

            return true;
        }
    }
}
