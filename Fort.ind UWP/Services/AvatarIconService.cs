using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
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

        private const uint MaxAvatarBytes = 8 * 1024 * 1024;

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

        private static string s_cachedUrl;
        private static Uri s_cachedUri;

        public static void InvalidateCache()
        {
            s_cachedUrl = null;
            s_cachedUri = null;
        }

        public static async Task<Uri> GetCircularAvatarUriAsync(string avatarUrl)
        {
            var sourceUri = WebLauncher.TryCreateWebUri(avatarUrl);
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
                    var pixels = await TryRenderCircularIconAsync(sourceUri);
                    if (pixels == null)
                    {
                        await Task.Delay(TransientRetryDelay);
                        pixels = await TryRenderCircularIconAsync(sourceUri);
                    }

                    if (pixels == null)
                    {
                        return null;
                    }

                    if (!await WritePngAsync(folder, fileName, pixels))
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

        private static async Task<byte[]> TryRenderCircularIconAsync(Uri sourceUri)
        {
            try
            {
                return await RenderCircularIconAsync(sourceUri);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: avatar fetch/decode failed - {ex.Message}");
                return null;
            }
        }

        private static async Task<InMemoryRandomAccessStream> DownloadCappedAsync(Uri sourceUri)
        {
            using (var cts = new CancellationTokenSource(DownloadTimeout))
            using (var response = await s_client.Value
                .GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead)
                .AsTask(cts.Token))
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
                    using (var input = await response.Content.ReadAsInputStreamAsync().AsTask(cts.Token))
                    {
                        var chunk = new Windows.Storage.Streams.Buffer(64 * 1024);
                        ulong total = 0;

                        while (true)
                        {
                            var read = await input.ReadAsync(chunk, chunk.Capacity, InputStreamOptions.Partial)
                                                  .AsTask(cts.Token);
                            if (read.Length == 0) break;

                            total += read.Length;
                            if (total > MaxAvatarBytes)
                            {
                                Debug.WriteLine("AvatarIconService: avatar body ran past the cap");
                                memory.Dispose();
                                return null;
                            }

                            await memory.WriteAsync(read).AsTask(cts.Token);
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

        private static async Task<byte[]> RenderCircularIconAsync(Uri sourceUri)
        {
            var downloaded = await DownloadCappedAsync(sourceUri);
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

                double scale = (double)IconPixelSize / Math.Min(decoder.PixelWidth, decoder.PixelHeight);
                uint scaledWidth = (uint)Math.Max(IconPixelSize, Math.Round(decoder.PixelWidth * scale));
                uint scaledHeight = (uint)Math.Max(IconPixelSize, Math.Round(decoder.PixelHeight * scale));

                var transform = new BitmapTransform();
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
                transform.ScaledWidth = scaledWidth;
                transform.ScaledHeight = scaledHeight;
                transform.Bounds = new BitmapBounds
                {
                    X = (scaledWidth - IconPixelSize) / 2,
                    Y = (scaledHeight - IconPixelSize) / 2,
                    Width = IconPixelSize,
                    Height = IconPixelSize
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var pixels = pixelData.DetachPixelData();
                if (pixels == null || pixels.Length != IconPixelSize * IconPixelSize * 4)
                {
                    Debug.WriteLine($"AvatarIconService: unexpected decode size {pixels?.Length ?? 0} bytes");
                    return null;
                }

                ApplyCircleMask(pixels, IconPixelSize);
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

        private static async Task<bool> WritePngAsync(StorageFolder folder, string fileName, byte[] pixels)
        {
            var tempFile = await folder.CreateFileAsync(fileName + ".tmp", CreationCollisionOption.ReplaceExisting);

            try
            {
                using (var fileStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fileStream);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                                         (uint)IconPixelSize, (uint)IconPixelSize, 96, 96, pixels);
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
            uint hash = 2166136261;
            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash ^ value[i]) * 16777619;
            }
            return hash.ToString("x8");
        }
    }
}
