using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public sealed class AvatarIconService
    {
        private AvatarIconService()
        {
        }

        private const int IconPixelSize = 48;

        private const string FilePrefix = "navavatar-";
        private const string FileExtension = ".png";

        private const int HashPrefixLength = 16;

        private const uint MaxAvatarBytes = 8 * 1024 * 1024;

        private const uint MaxSourcePixelDimension = 8192;

        private const ulong MaxScaledPixels = 4UL * 1024 * 1024;

        private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);

        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(20);

        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(CreateClient);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();

            var version = AppConstants.AppVersionDisplay;
            int space = version.IndexOf(' ');
            if (space > 0)
            {
                version = version.Substring(0, space);
            }

            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind/" + version))
            {
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind");
            }

            return client;
        }

        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        private const int ToastIconPixelSize = 96;

        private const string ToastFolderName = "toast-avatars";

        private static readonly TimeSpan ToastAvatarLifetime = TimeSpan.FromDays(3);

        private static readonly SemaphoreSlim s_toastGate = new SemaphoreSlim(1, 1);

        private const int TileIconPixelSize = 300;

        private const string TileFolderName = "tile-avatars";

        private static readonly SemaphoreSlim s_tileGate = new SemaphoreSlim(1, 1);

        private static string s_cachedUrl;
        private static Uri s_cachedUri;

        public static void InvalidateCache()
        {
            s_cachedUrl = null;
            s_cachedUri = null;
        }

        public static async Task<Uri> GetCircularAvatarUriAsync(string avatarUrl)
        {
            var sourceUri = WebLauncher.TryCreateFetchUri(avatarUrl);
            if (sourceUri == null)
            {
                return null;
            }

            await s_gate.WaitAsync();
            try
            {
                if (string.Equals(avatarUrl, s_cachedUrl, StringComparison.Ordinal) && s_cachedUri != null)
                {
                    return s_cachedUri;
                }

                var fileName = FilePrefix + StableHash(avatarUrl) + "-" + IconPixelSize + FileExtension;
                var folder = ApplicationData.Current.LocalFolder;
                var localUri = new Uri("ms-appdata:///local/" + fileName);

                var existing = await folder.TryGetItemAsync(fileName);
                if (existing == null)
                {
                    var pixels = await TryRenderCircularIconAsync(sourceUri, IconPixelSize, CancellationToken.None);
                    if (pixels == null)
                    {
                        await Task.Delay(TransientRetryDelay);
                        pixels = await TryRenderCircularIconAsync(sourceUri, IconPixelSize, CancellationToken.None);
                    }

                    if (pixels == null)
                    {
                        return null;
                    }

                    if (!await WritePngAsync(folder, fileName, pixels, IconPixelSize))
                    {
                        return null;
                    }

                    await PruneOtherAvatarsAsync(folder, fileName);
                }
                else
                {
                    await SweepLegacyAvatarsOnceAsync(folder, fileName);
                }

                s_cachedUrl = avatarUrl;
                s_cachedUri = localUri;
                return localUri;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: could not build nav avatar - {ex.Message}");
                return null;
            }
            finally
            {
                s_gate.Release();
            }
        }

        public static async Task<Uri> GetToastAvatarUriAsync(string avatarUrl, CancellationToken cancellationToken)
        {
            var sourceUri = WebLauncher.TryCreateFetchUri(avatarUrl);
            if (sourceUri == null)
            {
                return null;
            }

            await s_toastGate.WaitAsync(cancellationToken);
            try
            {
                var folder = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync(ToastFolderName, CreationCollisionOption.OpenIfExists);
                var fileName = StableHash(avatarUrl) + "-" + ToastIconPixelSize + FileExtension;

                var existing = await folder.TryGetItemAsync(fileName);
                if (existing == null)
                {
                    var pixels = await TryRenderCircularIconAsync(sourceUri, ToastIconPixelSize, cancellationToken);
                    if (pixels == null || !await WritePngAsync(folder, fileName, pixels, ToastIconPixelSize))
                    {
                        return null;
                    }

                    await PruneExpiredToastAvatarsAsync(folder, fileName);
                }

                return new Uri("ms-appdata:///local/" + ToastFolderName + "/" + fileName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: could not build toast avatar - {ex.Message}");
                return null;
            }
            finally
            {
                s_toastGate.Release();
            }
        }

        public static async Task<Uri> GetTileAvatarUriAsync(string avatarUrl, CancellationToken cancellationToken)
        {
            var sourceUri = WebLauncher.TryCreateFetchUri(avatarUrl);
            if (sourceUri == null)
            {
                return null;
            }

            await s_tileGate.WaitAsync(cancellationToken);
            try
            {
                var folder = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync(TileFolderName, CreationCollisionOption.OpenIfExists);
                var fileName = StableHash(avatarUrl) + "-" + TileIconPixelSize + FileExtension;

                var existing = await folder.TryGetItemAsync(fileName);
                if (existing == null)
                {
                    var pixels = await TryRenderCircularIconAsync(sourceUri, TileIconPixelSize, cancellationToken);
                    if (pixels == null || !await WritePngAsync(folder, fileName, pixels, TileIconPixelSize))
                    {
                        return null;
                    }
                }

                return new Uri("ms-appdata:///local/" + TileFolderName + "/" + fileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: could not build tile avatar - {ex.Message}");
                return null;
            }
            finally
            {
                s_tileGate.Release();
            }
        }

        public static async Task RetainTileAvatarsAsync(ICollection<Uri> inUse)
        {
            await s_tileGate.WaitAsync();
            try
            {
                var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(TileFolderName);
                var folder = item as StorageFolder;
                if (folder == null) return;

                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var uri in inUse)
                {
                    if (uri != null) keep.Add(System.IO.Path.GetFileName(uri.AbsolutePath));
                }

                foreach (var file in await folder.GetFilesAsync())
                {
                    if (keep.Contains(file.Name)) continue;

                    try
                    {
                        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AvatarIconService: could not delete tile avatar {file.Name} - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: tile avatar prune failed - {ex.Message}");
            }
            finally
            {
                s_tileGate.Release();
            }
        }

        private static async Task PruneExpiredToastAvatarsAsync(StorageFolder folder, string keepFileName)
        {
            try
            {
                var cutoff = DateTimeOffset.Now - ToastAvatarLifetime;
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    if (string.Equals(file.Name, keepFileName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (file.DateCreated > cutoff && !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AvatarIconService: could not delete toast avatar {file.Name} - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: toast avatar prune failed - {ex.Message}");
            }
        }

        private static async Task<byte[]> TryRenderCircularIconAsync(Uri sourceUri, int size, CancellationToken cancellationToken)
        {
            try
            {
                return await RenderCircularIconAsync(sourceUri, size, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: avatar fetch/decode failed - {ex.Message}");
                return null;
            }
        }

        private static async Task<InMemoryRandomAccessStream> DownloadCappedAsync(Uri sourceUri, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(DownloadTimeout);
                return await DownloadCappedCoreAsync(sourceUri, cts.Token);
            }
        }

        private static async Task<InMemoryRandomAccessStream> DownloadCappedCoreAsync(Uri sourceUri, CancellationToken cancellationToken)
        {
            using (var response = await s_client.Value
                .GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead)
                .AsTask(cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength.HasValue && declaredLength.Value > MaxAvatarBytes)
                {
                    Debug.WriteLine($"AvatarIconService: avatar declares {declaredLength.Value} bytes, over the cap");
                    return null;
                }

                var memory = new InMemoryRandomAccessStream();
                try
                {
                    using (var input = await response.Content.ReadAsInputStreamAsync().AsTask(cancellationToken))
                    {
                        var chunk = new Windows.Storage.Streams.Buffer(64 * 1024);
                        ulong total = 0;

                        while (true)
                        {
                            var read = await input.ReadAsync(chunk, chunk.Capacity, InputStreamOptions.Partial)
                                                  .AsTask(cancellationToken);
                            if (read.Length == 0) break;

                            total += read.Length;
                            if (total > MaxAvatarBytes)
                            {
                                Debug.WriteLine("AvatarIconService: avatar body ran past the cap");
                                memory.Dispose();
                                return null;
                            }

                            await memory.WriteAsync(read).AsTask(cancellationToken);
                        }

                        if (total == 0)
                        {
                            Debug.WriteLine("AvatarIconService: avatar response was empty");
                            memory.Dispose();
                            return null;
                        }
                    }

                    await memory.FlushAsync();
                    memory.Seek(0);
                    return memory;
                }
                catch
                {
                    memory.Dispose();
                    throw;
                }
            }
        }

        private static async Task<byte[]> RenderCircularIconAsync(Uri sourceUri, int size, CancellationToken cancellationToken)
        {
            var downloaded = await DownloadCappedAsync(sourceUri, cancellationToken);
            if (downloaded == null)
            {
                return null;
            }

            using (var stream = downloaded)
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
                {
                    return null;
                }

                if (decoder.PixelWidth > MaxSourcePixelDimension || decoder.PixelHeight > MaxSourcePixelDimension)
                {
                    Debug.WriteLine($"AvatarIconService: avatar is {decoder.PixelWidth}x{decoder.PixelHeight}, over the dimension cap");
                    return null;
                }

                double scale = (double)size / Math.Min(decoder.PixelWidth, decoder.PixelHeight);
                uint scaledWidth = (uint)Math.Max(size, Math.Round(decoder.PixelWidth * scale));
                uint scaledHeight = (uint)Math.Max(size, Math.Round(decoder.PixelHeight * scale));

                if ((ulong)scaledWidth * scaledHeight > MaxScaledPixels)
                {
                    Debug.WriteLine($"AvatarIconService: avatar scales to {scaledWidth}x{scaledHeight}, over the intermediate cap");
                    return null;
                }

                var transform = new BitmapTransform();
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
                transform.ScaledWidth = scaledWidth;
                transform.ScaledHeight = scaledHeight;
                transform.Bounds = new BitmapBounds
                {
                    X = (scaledWidth - (uint)size) / 2,
                    Y = (scaledHeight - (uint)size) / 2,
                    Width = (uint)size,
                    Height = (uint)size
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var pixels = pixelData.DetachPixelData();
                if (pixels == null || pixels.Length != size * size * 4)
                {
                    Debug.WriteLine($"AvatarIconService: unexpected decode size {pixels?.Length ?? 0} bytes");
                    return null;
                }

                ApplyCircleMask(pixels, size);
                return pixels;
            }
        }

        private static void ApplyCircleMask(byte[] bgra, int size)
        {
            double radius = size / 2.0;

            for (int y = 0; y < size; y++)
            {
                double dy = y + 0.5 - radius;
                for (int x = 0; x < size; x++)
                {
                    double dx = x + 0.5 - radius;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    double coverage = radius - distance + 0.5;
                    if (coverage >= 1.0) continue;

                    int alphaIndex = (y * size + x) * 4 + 3;
                    bgra[alphaIndex] = (byte)Math.Round(bgra[alphaIndex] * Math.Max(0.0, coverage), MidpointRounding.ToEven);
                }
            }
        }

        private static async Task<bool> WritePngAsync(StorageFolder folder, string fileName, byte[] pixels, int size)
        {
            var tempFile = await folder.CreateFileAsync(fileName + ".tmp", CreationCollisionOption.ReplaceExisting);

            try
            {
                using (var fileStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fileStream);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                                         (uint)size, (uint)size, 96, 96, pixels);
                    await encoder.FlushAsync();
                }

                await tempFile.RenameAsync(fileName, NameCollisionOption.ReplaceExisting);
            }
            catch
            {
                try
                {
                    await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"AvatarIconService: could not remove the temp avatar - {cleanupEx.Message}");
                }

                throw;
            }

            return true;
        }

        private static async Task<bool> PruneOtherAvatarsAsync(StorageFolder folder, string keepFileName)
        {
            try
            {
                var items = await folder.GetItemsAsync();
                foreach (var item in items)
                {
                    var file = item as StorageFile;
                    if (file == null) continue;
                    if (!file.Name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(file.Name, keepFileName, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AvatarIconService: could not delete stale avatar {file.Name} - {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: avatar prune failed - {ex.Message}");
                return false;
            }
        }

        private static async Task SweepLegacyAvatarsOnceAsync(StorageFolder folder, string keepFileName)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (values.ContainsKey(AppConstants.SettingAvatarLegacySweepDone)) return;

                if (await PruneOtherAvatarsAsync(folder, keepFileName))
                {
                    values[AppConstants.SettingAvatarLegacySweepDone] = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: legacy avatar sweep failed - {ex.Message}");
            }
        }

        private static string StableHash(string value)
        {
            var source = CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8);
            var digest = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256).HashData(source);
            return CryptographicBuffer.EncodeToHexString(digest).Substring(0, HashPrefixLength);
        }
    }
}
