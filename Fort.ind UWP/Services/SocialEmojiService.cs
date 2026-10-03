using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System.Profile;

namespace Fort.ind_UWP
{
    public sealed class SocialEmojiCatalog
    {
        public SocialEmojiCatalog(IReadOnlyList<SocialEmojiEntry> custom, IReadOnlyList<SocialEmojiEntry> unicode)
        {
            Custom = custom;
            Unicode = unicode;
        }

        public IReadOnlyList<SocialEmojiEntry> Custom { get; private set; }

        public IReadOnlyList<SocialEmojiEntry> Unicode { get; private set; }

        public SocialEmojiEntry Find(string key, out string shownKey)
        {
            shownKey = key;
            if (string.IsNullOrEmpty(key)) return null;

            var name = SocialReactions.CustomName(key);
            if (name != null)
            {
                if (SocialReactions.IsRemoteCustom(key)) return null;

                return Custom.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
            }

            foreach (var entry in Unicode)
            {
                if (SocialReactions.AreSame(entry.Key, key))
                {
                    shownKey = entry.Key;
                    return entry;
                }

                if (entry.Tones == null) continue;

                foreach (var tone in entry.Tones)
                {
                    if (SocialReactions.AreSame(tone, key))
                    {
                        shownKey = tone;
                        return entry;
                    }
                }
            }

            return null;
        }
    }

    public static class SocialEmojiService
    {
        public const string CustomGroupPrefix = "custom:";

        public static readonly IReadOnlyList<string> UnicodeGroups = new[]
        {
            "smileys-emotion", "people-body", "animals-nature", "food-drink", "travel-places",
            "activities", "objects", "symbols", "flags"
        };

        private const string AssetUri = "ms-appx:///Assets/Emoji/unicode-emoji.txt";

        private const int RecentLimit = 24;

        private const char RecentSeparator = '\n';

        private static readonly object s_lock = new object();

        private static readonly IReadOnlyList<SocialEmojiEntry> s_none = new SocialEmojiEntry[0];

        private static Task<IReadOnlyList<SocialEmojiEntry>> s_unicodeTask;

        private static IReadOnlyList<SocialCustomEmoji> s_customSource;

        private static IReadOnlyList<SocialEmojiEntry> s_customEntries;

        public static async Task<SocialEmojiCatalog> GetCatalogAsync()
        {
            var unicodeTask = GetUnicodeAsync();
            var custom = await SocialContentService.GetCustomEmojisAsync();
            var unicode = await unicodeTask;

            return new SocialEmojiCatalog(CustomEntries(custom), unicode);
        }

        public static int SkinTone
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialEmojiSkinTone];
                    var tone = stored == null ? 0 : Convert.ToInt32(stored, CultureInfo.InvariantCulture);
                    return tone >= 0 && tone <= 5 ? tone : 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialEmojiService: could not read the skin tone - {ex.Message}");
                    return 0;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialEmojiSkinTone] = Math.Max(0, Math.Min(5, value));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialEmojiService: could not save the skin tone - {ex.Message}");
                }
            }
        }

        public static IReadOnlyList<string> Recents
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialRecentReactions] as string;
                    if (string.IsNullOrEmpty(stored)) return new string[0];

                    return stored.Split(RecentSeparator).Where(key => key.Length > 0).Take(RecentLimit).ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialEmojiService: could not read the recent emoji - {ex.Message}");
                    return new string[0];
                }
            }
        }

        public static void RememberRecent(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf(RecentSeparator) >= 0) return;

            try
            {
                var keys = new List<string> { key };
                foreach (var existing in Recents)
                {
                    if (keys.Count >= RecentLimit) break;
                    if (!SocialReactions.AreSame(existing, key)) keys.Add(existing);
                }

                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialRecentReactions] = string.Join(RecentSeparator.ToString(), keys);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiService: could not save a recent emoji - {ex.Message}");
            }
        }

        public static Task<IReadOnlyList<SocialEmojiEntry>> GetUnicodeAsync()
        {
            lock (s_lock)
            {
                if (s_unicodeTask == null) s_unicodeTask = LoadUnicodeAsync();
                return s_unicodeTask;
            }
        }

        private static async Task<IReadOnlyList<SocialEmojiEntry>> LoadUnicodeAsync()
        {
            try
            {
                var supported = MaxSupportedVersion();
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(AssetUri));
                var text = await FileIO.ReadTextAsync(file);

                return await Task.Run(() => ParseUnicode(text, supported));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiService: could not load the emoji list - {ex.GetType().Name}: {ex.Message}");
                lock (s_lock)
                {
                    s_unicodeTask = null;
                }
                return s_none;
            }
        }

        private static IReadOnlyList<SocialEmojiEntry> ParseUnicode(string text, Version supported)
        {
            var entries = new List<SocialEmojiEntry>(2000);
            var lines = text.Split('\n');

            for (var i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].TrimEnd('\r').Split('\t');
                if (fields.Length < 6 || fields[1].Length == 0) continue;

                Version version;
                if (!Version.TryParse(fields[2], out version) || version > supported) continue;

                IReadOnlyList<string> tones = null;
                Version toneVersion;
                if (fields[4].Length > 0 && Version.TryParse(fields[5], out toneVersion) && toneVersion <= supported)
                {
                    var parts = fields[4].Split(' ');
                    if (parts.Length == 5) tones = parts;
                }

                entries.Add(new SocialEmojiEntry(fields[1], fields[1], null, fields[3], null, fields[0], tones, false));
            }

            return entries;
        }

        private static IReadOnlyList<SocialEmojiEntry> CustomEntries(IReadOnlyList<SocialCustomEmoji> custom)
        {
            lock (s_lock)
            {
                if (ReferenceEquals(custom, s_customSource) && s_customEntries != null) return s_customEntries;
            }

            var entries = new List<SocialEmojiEntry>(custom.Count);
            foreach (var emoji in custom)
            {
                var uri = SocialLinks.StaticEmojiUri(emoji.Url);
                if (uri == null) continue;

                var category = string.IsNullOrWhiteSpace(emoji.Category) ? "" : emoji.Category.Trim();
                entries.Add(new SocialEmojiEntry(":" + emoji.Name + ":", null, uri, emoji.Name, emoji.Aliases,
                                                 CustomGroupPrefix + category, null, emoji.IsSensitive));
            }

            entries.Sort((a, b) =>
            {
                var aOther = a.Group.Length == CustomGroupPrefix.Length;
                var bOther = b.Group.Length == CustomGroupPrefix.Length;
                if (aOther != bOther) return aOther ? 1 : -1;

                var byGroup = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
                return byGroup != 0 ? byGroup : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            lock (s_lock)
            {
                s_customSource = custom;
                s_customEntries = entries;
            }

            return entries;
        }

        private static Version MaxSupportedVersion()
        {
            ulong build = 0;
            try
            {
                var family = ulong.Parse(AnalyticsInfo.VersionInfo.DeviceFamilyVersion, CultureInfo.InvariantCulture);
                build = (family >> 16) & 0xFFFF;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiService: could not read the Windows build - {ex.Message}");
            }

            if (build >= 26100) return new Version(15, 1);
            if (build >= 22621) return new Version(15, 0);
            if (build >= 22000) return new Version(13, 1);
            if (build >= 18362) return new Version(12, 0);
            return new Version(11, 0);
        }
    }
}
