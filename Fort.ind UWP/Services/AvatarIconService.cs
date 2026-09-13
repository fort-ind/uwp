using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
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
                    var icon = await TryRenderCircularIconAsync(sourceUri);
                    if (icon == null)
                    {
                        await Task.Delay(TransientRetryDelay);
                        icon = await TryRenderCircularIconAsync(sourceUri);
                    }

                    if (icon == null)
                    {
                        return null;
                    }

                    using (icon)
                    {
                        if (!await WritePngAsync(folder, fileName, icon))
                        {
                            return null;
                        }
                    }
                }

                // Outside the write branch on purpose. Getting this far means the memoized URI did
                // not match, which happens at most once per avatar per process - cheap enough to
                // enumerate the folder for - and doing it here also sweeps up files that
                // accumulated before there was any pruning at all, which a write-only prune would
                // leave behind forever for anyone whose avatar never changes again.
                await PruneOtherAvatarsAsync(folder, fileName);

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

        private static async Task<CanvasRenderTarget> TryRenderCircularIconAsync(Uri sourceUri)
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

        private static async Task<CanvasRenderTarget> RenderCircularIconAsync(Uri sourceUri)
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

                // The decode stays on BitmapDecoder rather than CanvasBitmap.LoadAsync: the
                // transform scales and crops during the decode, so an 8 MB source never has to fit
                // in a GPU texture whole, and the EXIF behaviour above is the one already known to
                // be right. Premultiplied, because that is what CreateFromSoftwareBitmap accepts.
                using (var decoded = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage))
                {
                    if (decoded.PixelWidth != IconPixelSize || decoded.PixelHeight != IconPixelSize)
                    {
                        Debug.WriteLine($"AvatarIconService: unexpected decode size {decoded.PixelWidth}x{decoded.PixelHeight}");
                        return null;
                    }

                    return DrawCircularIcon(decoded);
                }
            }
        }

        /// <summary>
        /// Draws <paramref name="decoded"/> through an antialiased circular clip into a new
        /// transparent render target. The caller owns (and must dispose) the result.
        /// </summary>
        /// <remarks>
        /// GetSharedDevice re-creates the device itself if it has been lost, so a device-lost
        /// failure here only needs reporting: the retry in GetCircularAvatarUriAsync then runs
        /// against a fresh device.
        /// </remarks>
        private static CanvasRenderTarget DrawCircularIcon(SoftwareBitmap decoded)
        {
            var device = CanvasDevice.GetSharedDevice();
            CanvasRenderTarget target = null;

            try
            {
                // 96 DPI makes one DIP one pixel, so every coordinate below is in pixels.
                target = new CanvasRenderTarget(device, IconPixelSize, IconPixelSize, 96);

                using (var source = CanvasBitmap.CreateFromSoftwareBitmap(device, decoded))
                using (var session = target.CreateDrawingSession())
                {
                    // A render target starts with undefined content, not transparent.
                    session.Clear(Colors.Transparent);
                    session.Antialiasing = CanvasAntialiasing.Antialiased;

                    float radius = IconPixelSize / 2f;
                    using (var circle = CanvasGeometry.CreateCircle(device, radius, radius, radius))
                    using (session.CreateLayer(1f, circle))
                    {
                        session.DrawImage(source);
                    }
                }

                return target;
            }
            catch (Exception ex) when (device.IsDeviceLost(ex.HResult))
            {
                target?.Dispose();
                device.RaiseDeviceLost();
                Debug.WriteLine("AvatarIconService: graphics device lost while drawing the avatar");
                return null;
            }
            catch
            {
                target?.Dispose();
                throw;
            }
        }

        private static async Task<bool> WritePngAsync(StorageFolder folder, string fileName, CanvasRenderTarget icon)
        {
            var tempFile = await folder.CreateFileAsync(fileName + ".tmp", CreationCollisionOption.ReplaceExisting);

            try
            {
                using (var fileStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    await icon.SaveAsync(fileStream, CanvasBitmapFileFormat.Png);
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
        private static async Task PruneOtherAvatarsAsync(StorageFolder folder, string keepFileName)
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: avatar prune failed - {ex.Message}");
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
