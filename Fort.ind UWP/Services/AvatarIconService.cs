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

        /// <summary>
        /// Drops the memoized icon URI. Call this whenever the backing PNG may have been deleted
        /// out from under the cache - the full app-data reset being the one path that does that.
        /// </summary>
        /// <remarks>
        /// Without it, signing back in after a reset with the same avatar returned the remembered
        /// ms-appdata URI for a file the reset had just deleted, and the nav item's BitmapIcon drew
        /// nothing at all - there is no failure signal on that pipeline to fall back from.
        /// Deliberately not taking s_gate: this is synchronous, and blocking a UI-thread caller on
        /// an in-flight download to clear two references would be a far worse trade than the
        /// vanishingly narrow race of a concurrent fetch repopulating them.
        /// </remarks>
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

                    // A new file is the only thing that can leave an old one behind, so this is
                    // where pruning belongs.
                    await PruneOtherAvatarsAsync(folder, fileName);
                }
                else
                {
                    // The common case - the same avatar as last launch - used to enumerate
                    // LocalFolder on every single start, because the memoized URI is per process.
                    // Only files that predate any pruning at all still need that, and one sweep
                    // clears them for good.
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

        /// <summary>
        /// Downloads the avatar into memory, giving up past <see cref="MaxAvatarBytes"/> or
        /// <see cref="DownloadTimeout"/>. Returns null (and disposes nothing the caller owns) when
        /// the response is empty or over the cap.
        /// </summary>
        /// <remarks>
        /// Streamed rather than GetBufferAsync, which read the entire body before the size check
        /// could run - so the cap bounded nothing. The declared Content-Length rejects an honest
        /// oversized response before any body is read; the running count catches a chunked or
        /// lying one. The timeout matters because Windows.Web.Http has none of its own and this
        /// runs under s_gate: a stalled instance would otherwise hold every later avatar request.
        /// </remarks>
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

        /// <summary>
        /// Downloads, centre-crops and circle-masks the avatar. Returns straight-alpha BGRA8
        /// pixels, <see cref="IconPixelSize"/> square, or null.
        /// </summary>
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

                // The transform scales and crops during the decode, so an 8 MB source is never
                // held whole. Straight alpha, not premultiplied: the circle mask below then only has
                // to scale the alpha channel, and it is what the PNG encoder stores anyway.
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

        /// <summary>
        /// Scales each pixel's alpha by how much of it lies inside the inscribed circle, which
        /// antialiases the edge by about one pixel.
        /// </summary>
        /// <remarks>
        /// This used to be a Win2D layer clip. At 48x48 that meant creating a Direct3D device and
        /// shipping a native graphics library to touch 2,304 pixels; the arithmetic is the same
        /// coverage estimate its antialiaser makes along an edge, done on the CPU.
        /// </remarks>
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

                    // 1 inside, 0 outside, a linear ramp across the pixel the edge passes through.
                    double coverage = radius - distance + 0.5;
                    if (coverage >= 1.0) continue;

                    int alphaIndex = (y * size + x) * 4 + 3;
                    if (coverage <= 0.0)
                    {
                        bgra[alphaIndex] = 0;
                    }
                    else
                    {
                        bgra[alphaIndex] = (byte)Math.Round(bgra[alphaIndex] * coverage, MidpointRounding.ToEven);
                    }
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
                // The file is created before the encode, so a throw anywhere in there strands a
                // zero-length .tmp in LocalFolder - one more per failed attempt, forever.
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

        /// <summary>
        /// Deletes every avatar file in <paramref name="folder"/> except the one just written.
        /// </summary>
        /// <remarks>
        /// The file name carries a hash of the source URL so that a changed avatar lands on a new
        /// path (XAML's image cache holds the old bitmap for a reused one). The cost of that is a
        /// fresh PNG per avatar the user has ever had, with nothing removing the old ones. Only
        /// files under the avatar prefix are touched, so the profile cache and favorites file
        /// sharing this folder are never in scope - and .tmp leftovers match the prefix too,
        /// so they get swept up here as well.
        /// </remarks>
        /// <returns>False if the folder could not be enumerated at all.</returns>
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

        /// <summary>
        /// Runs <see cref="PruneOtherAvatarsAsync"/> once per install, for avatar files written by
        /// builds that never pruned. Gated on a LocalSettings flag, which is an in-memory lookup,
        /// so the steady state costs no file-system call.
        /// </summary>
        /// <remarks>
        /// An app-data reset clears the flag along with LocalFolder, so the sweep runs once more
        /// against a folder that is already empty - harmless. The flag is only set when the
        /// enumeration worked, so a failure is retried next launch.
        /// </remarks>
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
