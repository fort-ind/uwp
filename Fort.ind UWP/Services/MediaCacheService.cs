using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class MediaCacheService
    {
        private const string WebCacheFolderName = "INetCache";

        private const long BytesPerMegabyte = 1024L * 1024L;

        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        private static int s_trimStarted;

        public static int LimitMegabytes
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingMediaCacheLimitMegabytes];
                    return stored == null ? AppConstants.DefaultMediaCacheLimitMegabytes : Math.Max(0, Convert.ToInt32(stored));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaCacheService: could not read the cache limit - {ex.Message}");
                    return AppConstants.DefaultMediaCacheLimitMegabytes;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingMediaCacheLimitMegabytes] = Math.Max(0, value);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaCacheService: could not save the cache limit - {ex.Message}");
                }
            }
        }

        public static Task<long?> MeasureAsync()
        {
            string[] folders;
            try
            {
                folders = ResolveFolders();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaCacheService: could not resolve the cache folders - {ex.Message}");
                return Task.FromResult<long?>(null);
            }

            return Task.Run(() => (long?)folders.Sum(folder => LocalStorageService.MeasureFolder(folder) ?? 0));
        }

        public static async Task<long?> ClearAsync()
        {
            await s_gate.WaitAsync();
            try
            {
                var before = await MeasureAsync();

                string webCache = null;
                try
                {
                    webCache = ResolveWebCacheFolder();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaCacheService: could not resolve the web cache - {ex.Message}");
                }

                if (webCache != null)
                {
                    await Task.Run(() => DeleteWebCacheFiles(webCache));
                }

                await AvatarIconService.ClearToastAvatarsAsync();
                await SocialTileService.ClearAvatarsAsync();
                SocialNotificationService.RefreshTileInBackground();

                var after = await MeasureAsync();
                if (!before.HasValue || !after.HasValue) return null;

                return Math.Max(0, before.Value - after.Value);
            }
            finally
            {
                s_gate.Release();
            }
        }

        public static async void TrimIfOverLimitInBackground()
        {
            try
            {
                if (Interlocked.Exchange(ref s_trimStarted, 1) != 0) return;

                await Task.Delay(TimeSpan.FromSeconds(AppConstants.MediaCacheCheckDelaySeconds));

                var limit = LimitMegabytes;
                if (limit <= 0) return;

                var size = await MeasureAsync();
                if (!size.HasValue || size.Value <= limit * BytesPerMegabyte) return;

                var freed = await ClearAsync();
                Debug.WriteLine($"MediaCacheService: cache was {size.Value} bytes, over {limit} MB; freed {freed} bytes");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaCacheService: automatic trim failed - {ex.Message}");
            }
        }

        private static string ResolveWebCacheFolder()
        {
            var packageDataRoot = Path.GetDirectoryName(ApplicationData.Current.LocalFolder.Path);
            return Path.Combine(packageDataRoot, LocalStorageService.PackageWebCacheFolderName, WebCacheFolderName);
        }

        private static string[] ResolveFolders()
        {
            var local = ApplicationData.Current.LocalFolder.Path;
            return new[]
            {
                ResolveWebCacheFolder(),
                Path.Combine(local, AvatarIconService.ToastFolderName),
                Path.Combine(local, AvatarIconService.TileFolderName)
            };
        }

        private static void DeleteWebCacheFiles(string webCache)
        {
            if (!Directory.Exists(webCache)) return;

            int deleted = 0;
            int skipped = 0;

            foreach (var container in new DirectoryInfo(webCache).EnumerateDirectories().Where(container => !IsReparsePoint(container)))
            {
                FileInfo[] files;
                try
                {
                    files = container.GetFiles("*", SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    Debug.WriteLine($"MediaCacheService: could not list {container.Name} - {ex.Message}");
                    continue;
                }

                foreach (var file in files.Where(file => !IsReparsePoint(file)))
                {
                    try
                    {
                        file.Delete();
                        deleted++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        skipped++;
                    }
                }
            }

            Debug.WriteLine($"MediaCacheService: deleted {deleted} cached files, skipped {skipped} in use");
        }

        private static bool IsReparsePoint(FileSystemInfo entry)
        {
            return (entry.Attributes & System.IO.FileAttributes.ReparsePoint) != 0;
        }
    }
}
