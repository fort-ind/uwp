using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The set of games the user has starred, persisted to <c>favorites.json</c> in LocalFolder.
    /// </summary>
    /// <remarks>
    /// Keyed by URL because <see cref="SearchItem"/> carries no identifier of its own and the URL
    /// is what the sitemap actually provides. Consequence worth knowing: the sitemap is bundled, so
    /// a URL that changes between releases silently orphans that favorite - it is kept in the file
    /// rather than pruned, so a later build that restores the URL restores the star with it.
    ///
    /// Deliberately not wired into <see cref="SitemapService"/>. Applying favorites inside
    /// LoadSearchItemsAsync would be one tidy call site, but that method holds a non-reentrant
    /// SemaphoreSlim, and re-taking one of those fails silently rather than throwing. Callers apply
    /// favorites themselves after loading; <see cref="Apply"/> is idempotent.
    /// </remarks>
    public class FavoritesService
    {
        private static readonly StorageFolder LocalFolder = ApplicationData.Current.LocalFolder;

        /// <summary>Favorited URLs, newest first - the order the Home section shows them in.</summary>
        private static readonly List<string> s_order = new List<string>();

        /// <summary>Membership test for <see cref="s_order"/>, which is a linear scan otherwise.</summary>
        private static readonly HashSet<string> s_lookup = new HashSet<string>(StringComparer.Ordinal);

        private static bool s_loaded;

        private static readonly SemaphoreSlim s_loadGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Separate from <see cref="s_loadGate"/> on purpose: SemaphoreSlim is not reentrant, and a
        /// save happens while a load may already hold its own gate.
        /// </summary>
        private static readonly SemaphoreSlim s_saveGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Raised after any change to the set. Subscribers must detach in Unloaded and guard
        /// reattachment with a flag - UWP can raise Loaded/Unloaded more than once on one instance.
        /// </summary>
        public static event EventHandler FavoritesChanged;

        /// <summary>Reads the persisted set once per process.</summary>
        public static async Task EnsureLoadedAsync()
        {
            if (s_loaded) return;

            await s_loadGate.WaitAsync();
            StorageFile file = null;
            try
            {
                if (s_loaded) return;

                // TryGetItemAsync, not GetFileAsync: no favorites yet is a normal state on a fresh
                // install and after a reset, and GetFileAsync signals it by throwing - putting a
                // first-chance exception on the startup path for something that is not an error.
                file = await LocalFolder.TryGetItemAsync(AppConstants.FavoritesFileName) as StorageFile;
                if (file != null)
                {
                    var json = await FileIO.ReadTextAsync(file);
                    var payload = DeserializeFromJson<FavoritesPayload>(json);
                    if (payload != null && payload.Urls != null)
                    {
                        foreach (var url in payload.Urls)
                        {
                            if (string.IsNullOrEmpty(url)) continue;
                            if (s_lookup.Add(url))
                            {
                                s_order.Add(url);
                            }
                        }
                    }
                }

                s_loaded = true;
            }
            catch (Exception ex)
            {
                // A corrupt or unreadable file must not take out startup - carry on with an empty
                // set, and mark it loaded so this is not retried on every navigation.
                Debug.WriteLine($"FavoritesService: Failed to load favorites - {ex.Message}");
                s_order.Clear();
                s_lookup.Clear();
                s_loaded = true;

                // But never let the next star overwrite it: that used to replace every favorite the
                // user had with the one they had just added. Move it aside instead, and if even that
                // fails (a transient lock rather than bad JSON, say) stop saving for this session.
                if (file != null)
                {
                    s_saveBlocked = !await TryMoveAsideAsync(file);
                }
            }
            finally
            {
                s_loadGate.Release();
            }
        }

        /// <summary>
        /// Set when favorites.json could not be read or moved aside; saving would destroy it.
        /// </summary>
        private static bool s_saveBlocked;

        private static async Task<bool> TryMoveAsideAsync(StorageFile file)
        {
            try
            {
                await file.RenameAsync(AppConstants.FavoritesFileName + ".bak", NameCollisionOption.ReplaceExisting);
                Debug.WriteLine("FavoritesService: moved the unreadable favorites file aside as favorites.json.bak");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FavoritesService: could not move the unreadable favorites file aside - {ex.Message}");
                return false;
            }
        }

        public static bool IsFavorite(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return s_lookup.Contains(url);
        }

        public static int Count
        {
            get { return s_order.Count; }
        }

        /// <summary>
        /// Stamps <see cref="SearchItem.IsFavorite"/> onto already-loaded items. Sets the flag in
        /// both directions, so calling this after <see cref="ResetForAppDataWipe"/> clears every star.
        /// </summary>
        public static void Apply(IEnumerable<SearchItem> items)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item == null) continue;
                item.IsFavorite = !string.IsNullOrEmpty(item.Url) && s_lookup.Contains(item.Url);
            }
        }

        /// <summary>
        /// The favorited members of <paramref name="source"/>, newest first, capped at
        /// <paramref name="max"/>. Items whose URL is no longer in the sitemap are skipped.
        /// </summary>
        public static List<SearchItem> GetFavorites(IEnumerable<SearchItem> source, int max)
        {
            var results = new List<SearchItem>();
            if (source == null || max <= 0) return results;

            var byUrl = new Dictionary<string, SearchItem>(StringComparer.Ordinal);
            foreach (var item in source)
            {
                if (item == null || string.IsNullOrEmpty(item.Url)) continue;
                if (!byUrl.ContainsKey(item.Url))
                {
                    byUrl.Add(item.Url, item);
                }
            }

            foreach (var url in s_order)
            {
                SearchItem match;
                if (!byUrl.TryGetValue(url, out match)) continue;

                results.Add(match);
                if (results.Count >= max) break;
            }

            return results;
        }

        /// <summary>
        /// Toggles one item and writes the file immediately. Eager rather than batched into
        /// OnSuspending, which runs under a deadline that is not guaranteed to complete.
        /// </summary>
        public static async Task SetFavoriteAsync(SearchItem item, bool isFavorite)
        {
            if (item == null || string.IsNullOrEmpty(item.Url)) return;

            await EnsureLoadedAsync();

            bool changed;
            if (isFavorite)
            {
                changed = s_lookup.Add(item.Url);
                if (changed)
                {
                    // Newest first, so a game starting late in the alphabet still reaches the cap
                    // the Home section applies.
                    s_order.Insert(0, item.Url);
                }
            }
            else
            {
                changed = s_lookup.Remove(item.Url);
                if (changed)
                {
                    s_order.Remove(item.Url);
                }
            }

            item.IsFavorite = isFavorite;

            if (!changed) return;

            await SaveAsync();
            RaiseFavoritesChanged();
        }

        /// <summary>
        /// Drops every favorite from memory after the app-data wipe has already deleted the file.
        /// </summary>
        /// <remarks>
        /// Deliberately does not write: LocalStorageService.ResetAllAppDataAsync has just removed
        /// favorites.json along with the rest of LocalFolder, and saving here would recreate the
        /// file the reset just deleted. This exists at all because the static state above outlives
        /// that wipe - the same trap AvatarIconService.InvalidateCache covers for the cached avatar.
        /// Stays marked loaded so the now-empty set is not re-read from a file that is gone.
        /// </remarks>
        public static void ResetForAppDataWipe()
        {
            s_order.Clear();
            s_lookup.Clear();
            s_loaded = true;

            // The unreadable file, if there was one, went with the rest of LocalFolder.
            s_saveBlocked = false;

            RaiseFavoritesChanged();
        }

        private static async Task SaveAsync()
        {
            await s_saveGate.WaitAsync();
            try
            {
                if (s_saveBlocked)
                {
                    Debug.WriteLine("FavoritesService: not saving - the existing favorites file could not be read or moved aside");
                    return;
                }

                var payload = new FavoritesPayload { Urls = new List<string>(s_order) };
                var json = SerializeToJson(payload);

                var file = await LocalFolder.CreateFileAsync(
                    AppConstants.FavoritesFileName,
                    CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FavoritesService: Failed to save favorites - {ex.Message}");
            }
            finally
            {
                s_saveGate.Release();
            }
        }

        private static void RaiseFavoritesChanged()
        {
            var handler = FavoritesChanged;
            if (handler == null) return;

            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FavoritesService: FavoritesChanged handler threw - {ex.Message}");
            }
        }

        #region JSON Serialization

        [DataContract]
        internal sealed class FavoritesPayload
        {
            [DataMember]
            public List<string> Urls { get; set; }
        }

        private static string SerializeToJson<T>(T obj)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, obj);
                stream.Position = 0;
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static T DeserializeFromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default(T);
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        #endregion
    }
}
