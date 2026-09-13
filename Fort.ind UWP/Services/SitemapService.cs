using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public class SitemapService
    {
        private static IReadOnlyList<SearchItem> s_allItems;
        private static IReadOnlyList<SearchItem> s_gameItems;

        private static readonly SemaphoreSlim s_loadGate = new SemaphoreSlim(1, 1);

        public static async Task<IReadOnlyList<SearchItem>> LoadSearchItemsAsync()
        {
            var cached = s_allItems;
            if (cached != null) return cached;

            await s_loadGate.WaitAsync();
            try
            {
                if (s_allItems != null) return s_allItems;

                var parsed = await ParseSitemapAsync();
                var result = parsed.AsReadOnly();
                if (parsed.Count > 0)
                {
                    s_allItems = result;
                }
                return result;
            }
            finally
            {
                s_loadGate.Release();
            }
        }

        public static async Task<IReadOnlyList<SearchItem>> LoadGameItemsAsync()
        {
            var cachedGames = s_gameItems;
            if (cachedGames != null) return cachedGames;

            var all = await LoadSearchItemsAsync();

            List<SearchItem> games = new List<SearchItem>();
            foreach (var item in all)
            {
                // CategoryKey, not Category: Category is the localized display name now, and
                // matching a translated string against an English constant would empty this list
                // in every other language.
                if (item.CategoryKey != null &&
                    item.CategoryKey.StartsWith(AppConstants.CategoryGames, StringComparison.Ordinal))
                {
                    games.Add(item);
                }
            }

            var result = games.AsReadOnly();
            if (all.Count > 0)
            {
                s_gameItems = result;
            }
            return result;
        }

        private static async Task<List<SearchItem>> ParseSitemapAsync()
        {
            List<SearchItem> items = new List<SearchItem>();

            try
            {
                // Read straight from the package every time. There used to be a 24h on-disk cache of
                // the parsed URLs in front of this, keyed on the app version, which bought nothing -
                // it traded one local file read for another - and cost correctness: an edited
                // sitemap.xml deployed under the same version number stayed invisible for up to a
                // day. The in-process memoization in LoadSearchItemsAsync is the only cache needed.
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/sitemap.xml"));
                var text = await FileIO.ReadTextAsync(file);

                // The XML walk, URI validation and slug-to-title work is pure string handling, so
                // it runs on the thread pool; it used to run on the UI thread in the middle of
                // MainPage's first layout. Only the SearchItem construction comes back here, because
                // that resolves display text through LocalizedStrings, which needs the UI thread.
                var entries = await Task.Run(() => ReadSitemapEntries(text));
                if (entries == null)
                {
                    return items;
                }

                items = BuildSearchItems(entries);

                DeleteLegacyUrlCacheInBackground();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: failed to load sitemap – {ex.Message}");
            }

            return items;
        }

        private static List<string> ReadLocValues(string documentText)
        {
            List<string> urls = new List<string>();

            System.Xml.XmlReaderSettings settings = new System.Xml.XmlReaderSettings()
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            try
            {
                using (System.IO.StringReader stringReader = new System.IO.StringReader(documentText))
                {
                    using (var reader = System.Xml.XmlReader.Create(stringReader, settings))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == System.Xml.XmlNodeType.Element &&
                                string.Equals(reader.LocalName, "loc", StringComparison.Ordinal))
                            {
                                var value = reader.ReadElementContentAsString();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    urls.Add(value.Trim());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: XML parsing failed – {ex.Message}");
                return null;
            }

            return urls;
        }

        /// <summary>
        /// One sitemap URL, parsed but not yet a <see cref="SearchItem"/>.
        /// </summary>
        private sealed class SitemapEntry
        {
            /// <summary>Null for the site root, whose title is a resource looked up later.</summary>
            public string Title;
            public string CategoryKey;
            public string Url;
        }

        /// <summary>
        /// Everything that can run off the UI thread: no resource lookups. Null if the XML is
        /// unreadable.
        /// </summary>
        private static List<SitemapEntry> ReadSitemapEntries(string documentText)
        {
            var urls = ReadLocValues(documentText);
            if (urls == null)
            {
                return null;
            }

            List<SitemapEntry> entries = new List<SitemapEntry>(urls.Count);
            foreach (var url in urls)
            {
                var entry = CreateEntryFromUrl(url);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            return entries;
        }

        private static SitemapEntry CreateEntryFromUrl(string urlValue)
        {
            var uri = WebLauncher.TryCreateWebUri(urlValue);
            if (uri == null)
            {
                return null;
            }

            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(path))
            {
                return new SitemapEntry { Title = null, CategoryKey = AppConstants.CategoryFortWebsite, Url = urlValue };
            }

            if (path == "404")
            {
                return null;
            }

            return new SitemapEntry { Title = GetTitle(path), CategoryKey = GetCategory(path), Url = urlValue };
        }

        /// <summary>
        /// Must run on the UI thread - the SearchItem constructor resolves localized text.
        /// </summary>
        private static List<SearchItem> BuildSearchItems(List<SitemapEntry> entries)
        {
            List<SearchItem> items = new List<SearchItem>(entries.Count);
            foreach (var entry in entries)
            {
                var title = entry.Title ?? LocalizedStrings.Get("SearchItemHome");
                items.Add(new SearchItem(title, entry.CategoryKey, null, entry.Url));
            }
            return items;
        }

        /// <summary>
        /// Removes the URL cache file and its two settings that builds before the cache was
        /// dropped wrote to LocalFolder, so they do not sit there forever.
        /// </summary>
        /// <remarks>
        /// Gated on the timestamp setting, which is a cheap in-memory lookup, so an install that
        /// never had the cache - or has already been cleaned - pays no file-system call per launch.
        /// Fire-and-forget and best-effort: nothing reads these any more, so a failure costs only a
        /// few kilobytes.
        /// </remarks>
        private static async void DeleteLegacyUrlCacheInBackground()
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (!values.ContainsKey(AppConstants.LegacySitemapCacheTimestampKey)) return;

                var cacheFile = await ApplicationData.Current.LocalFolder.TryGetItemAsync(AppConstants.LegacySitemapCacheFileName);
                if (cacheFile != null)
                {
                    await cacheFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }

                // Settings last: the timestamp key is the gate, so if the delete above throws it
                // stays in place and the cleanup is retried on the next launch.
                values.Remove(AppConstants.LegacySitemapCacheTimestampKey);
                values.Remove(AppConstants.LegacySitemapCacheAppVersionKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: could not remove the legacy URL cache – {ex.Message}");
            }
        }

        /// <summary>
        /// Maps a sitemap path to an AppConstants category KEY - never to display text.
        /// </summary>
        /// <remarks>
        /// These used to be inline English literals ("Games — HTML") that went straight into
        /// SearchItem.Category and out to the group headers and search results untranslated.
        /// StringComparison.Ordinal throughout: these are URL paths, and the culture-sensitive
        /// StartsWith overload has no business deciding whether one begins with "games/".
        /// </remarks>
        private static string GetCategory(string path)
        {
            if (path.StartsWith("games/html/", StringComparison.Ordinal)) return AppConstants.CategoryGamesHtml;
            if (path.StartsWith("games/flash/", StringComparison.Ordinal)) return AppConstants.CategoryGamesFlash;
            if (path.StartsWith("games/codepen/", StringComparison.Ordinal)) return AppConstants.CategoryGamesCodePen;
            if (path.StartsWith("games/retroclassic-mostly-emulated/", StringComparison.Ordinal)) return AppConstants.CategoryGamesRetro;
            if (path.StartsWith("games/minecraft/", StringComparison.Ordinal)) return AppConstants.CategoryGamesMinecraft;
            if (path.StartsWith("games/", StringComparison.Ordinal)) return AppConstants.CategoryGames;
            if (path.StartsWith("social/", StringComparison.Ordinal)) return AppConstants.CategorySocial;
            if (path.StartsWith("emulators/", StringComparison.Ordinal)) return AppConstants.CategoryEmulators;
            if (path.StartsWith("apps/appstone/", StringComparison.Ordinal)) return AppConstants.CategoryAppsAppStone;
            if (path.StartsWith("apps/", StringComparison.Ordinal)) return AppConstants.CategoryApps;
            if (path.StartsWith("extras/", StringComparison.Ordinal)) return AppConstants.CategoryExtras;
            if (path.StartsWith("labs-betas/", StringComparison.Ordinal)) return AppConstants.CategoryLabsAndBetas;
            return AppConstants.CategoryFortWebsite;
        }

        private static readonly HashSet<string> s_upperCaseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs", "css", "dbz", "fnaf", "gba", "gbc", "gta", "hd", "html", "mlb", "mlg",
            "n64", "nba", "nds", "nes", "nfl", "nhl", "psp", "snes", "tmnt", "tv", "ufc",
            "ufo", "wwe"
        };

        private static readonly HashSet<string> s_domainSuffixTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com", "gg", "io", "lol", "net", "org"
        };

        private static string GetTitle(string path)
        {
            var trimmed = path.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            var slug = lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;

            if (string.IsNullOrEmpty(slug)) return path;

            var tokens = slug.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return path;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(slug.Length);
            for (int i = 0; i <= tokens.Length - 1; i++)
            {
                var token = tokens[i];

                if (i > 0 && s_domainSuffixTokens.Contains(token))
                {
                    sb.Append('.');
                    sb.Append(token.ToLowerInvariant());
                    continue;
                }

                if (i > 0 && IsAllDigits(token) && IsAllDigits(tokens[i - 1]))
                {
                    sb.Append('.');
                    sb.Append(token);
                    continue;
                }

                if (i > 0) sb.Append(' ');
                sb.Append(FormatToken(token));
            }

            return sb.ToString();
        }

        private static string FormatToken(string token)
        {
            if (s_upperCaseTokens.Contains(token)) return token.ToUpperInvariant();

            System.Text.StringBuilder sb = new System.Text.StringBuilder(token.Length);
            sb.Append(char.ToUpperInvariant(token[0]));
            for (int i = 1; i <= token.Length - 1; i++)
            {
                sb.Append(char.ToLowerInvariant(token[i]));
            }
            return sb.ToString();
        }

        private static bool IsAllDigits(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i <= token.Length - 1; i++)
            {
                if (!char.IsDigit(token[i])) return false;
            }
            return true;
        }
    }
}
