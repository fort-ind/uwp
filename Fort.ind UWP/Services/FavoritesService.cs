using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public class FavoritesService
    {
        private static readonly StorageFolder LocalFolder = ApplicationData.Current.LocalFolder;

        private static readonly List<string> s_order = new List<string>();

        private static readonly HashSet<string> s_lookup = new HashSet<string>(StringComparer.Ordinal);

        private static volatile bool s_loaded;

        private static readonly SemaphoreSlim s_loadGate = new SemaphoreSlim(1, 1);

        private static readonly SemaphoreSlim s_saveGate = new SemaphoreSlim(1, 1);

        public static event EventHandler FavoritesChanged;

        public static async Task EnsureLoadedAsync()
        {
            if (s_loaded) return;

            await s_loadGate.WaitAsync();
            StorageFile file = null;
            try
            {
                if (s_loaded) return;

                file = await LocalFolder.TryGetItemAsync(AppConstants.FavoritesFileName) as StorageFile;
                if (file != null)
                {
                    var json = await FileIO.ReadTextAsync(file);
                    var payload = DeserializeFromJson<FavoritesPayload>(json);
                    if (payload != null && payload.Urls != null)
                    {
                        s_order.AddRange(payload.Urls.Where(u => !string.IsNullOrEmpty(u))
                                                     .Distinct(StringComparer.Ordinal));
                        s_lookup.UnionWith(s_order);
                    }
                }

                s_loaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FavoritesService: Failed to load favorites - {ex.Message}");
                s_order.Clear();
                s_lookup.Clear();

                if (file != null)
                {
                    s_saveBlocked = !await TryMoveAsideAsync(file);
                }

                s_loaded = true;
            }
            finally
            {
                s_loadGate.Release();
            }
        }

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

        public static void Apply(IEnumerable<SearchItem> items)
        {
            if (items == null) return;

            foreach (var item in items.Where(i => i != null))
            {
                item.IsFavorite = !string.IsNullOrEmpty(item.Url) && s_lookup.Contains(item.Url);
            }
        }

        public static List<SearchItem> GetFavorites(IEnumerable<SearchItem> source, int max)
        {
            var results = new List<SearchItem>();
            if (source == null || max <= 0) return results;

            var byUrl = source.Where(i => i != null && !string.IsNullOrEmpty(i.Url))
                              .GroupBy(i => i.Url, StringComparer.Ordinal)
                              .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var url in s_order)
            {
                SearchItem match;
                if (!byUrl.TryGetValue(url, out match)) continue;

                results.Add(match);
                if (results.Count >= max) break;
            }

            return results;
        }

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

        public static void ResetForAppDataWipe()
        {
            s_order.Clear();
            s_lookup.Clear();
            s_loaded = true;

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
