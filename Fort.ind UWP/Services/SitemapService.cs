using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public class SitemapService
    {
        private static volatile IReadOnlyList<SearchItem> s_allItems;
        private static volatile IReadOnlyList<SearchItem> s_gameItems;

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

            var result = all.Where(item => item.CategoryKey != null &&
                                           item.CategoryKey.StartsWith(AppConstants.CategoryGames, StringComparison.Ordinal))
                            .ToList()
                            .AsReadOnly();
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
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/sitemap.xml"));
                var text = await FileIO.ReadTextAsync(file);

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

        private sealed class SitemapEntry
        {
            public string Title;
            public string CategoryKey;
            public string Url;
        }

        private static List<SitemapEntry> ReadSitemapEntries(string documentText)
        {
            var urls = ReadLocValues(documentText);
            if (urls == null)
            {
                return null;
            }

            return urls.Select(CreateEntryFromUrl)
                       .Where(entry => entry != null)
                       .ToList();
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

                values.Remove(AppConstants.LegacySitemapCacheTimestampKey);
                values.Remove(AppConstants.LegacySitemapCacheAppVersionKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: could not remove the legacy URL cache – {ex.Message}");
            }
        }

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
