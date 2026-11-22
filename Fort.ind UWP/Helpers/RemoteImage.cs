using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    internal static class RemoteImage
    {
        private const uint MaxImageBytes = 32 * 1024 * 1024;

        private const int MaxWebpSide = 8192;

        private const long MaxWebpPixels = 16L * 1024 * 1024;

        private const int WebpPoolMegabytes = 16;

        private const int DownloadChunkBytes = 64 * 1024;

        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

        private static readonly Lazy<bool> s_systemDecodesWebp = new Lazy<bool>(DetectSystemWebp);

        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(CreateClient);

        private static readonly Lazy<SixLabors.ImageSharp.Configuration> s_webpConfiguration =
            new Lazy<SixLabors.ImageSharp.Configuration>(CreateWebpConfiguration);

        private static readonly SemaphoreSlim s_decodeGate = new SemaphoreSlim(2, 2);

        private static readonly ConditionalWeakTable<BitmapImage, Uri> s_softwareSources =
            new ConditionalWeakTable<BitmapImage, Uri>();

        public static bool SystemDecodesWebp
        {
            get { return s_systemDecodesWebp.Value; }
        }

        public static void SetUri(BitmapImage bitmap, Uri uri)
        {
            SetUri(bitmap, uri, null);
        }

        public static void SetUri(BitmapImage bitmap, Uri uri, Action opened)
        {
            if (bitmap == null) return;

            s_softwareSources.Remove(bitmap);

            if (!NeedsSoftwarePath(uri))
            {
                if (opened != null) RunOnceOpened(bitmap, opened);
                bitmap.UriSource = uri;
                return;
            }

            s_softwareSources.Add(bitmap, uri);
            var ignored = LoadAsync(bitmap, uri, opened);
        }

        public static Uri SourceOf(BitmapImage bitmap)
        {
            if (bitmap == null) return null;

            Uri uri;
            return s_softwareSources.TryGetValue(bitmap, out uri) ? uri : bitmap.UriSource;
        }

        public static async Task<IRandomAccessStream> ToSystemDecodableAsync(IRandomAccessStream stream)
        {
            if (stream == null || SystemDecodesWebp || stream.Size < 12 || stream.Size > MaxImageBytes) return stream;

            var bytes = new byte[stream.Size];
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }

            stream.Seek(0);
            if (!IsWebp(bytes)) return stream;

            var converted = await Task.Run(() => DecodeWebpGatedAsync(bytes, 0, 0));
            return converted ?? stream;
        }

        public static void ReleaseMemory()
        {
            if (s_webpConfiguration.IsValueCreated) s_webpConfiguration.Value.MemoryAllocator.ReleaseRetainedResources();
        }

        private static bool NeedsSoftwarePath(Uri uri)
        {
            return uri != null
                   && uri.IsAbsoluteUri
                   && string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                   && !SystemDecodesWebp;
        }

        private static void RunOnceOpened(BitmapImage bitmap, Action opened)
        {
            RoutedEventHandler handler = null;
            handler = (sender, e) =>
            {
                bitmap.ImageOpened -= handler;
                opened();
            };
            bitmap.ImageOpened += handler;
        }

        private static bool IsSoftwareSource(BitmapImage bitmap, Uri uri)
        {
            Uri current;
            return s_softwareSources.TryGetValue(bitmap, out current) && current == uri;
        }

        private static async Task LoadAsync(BitmapImage bitmap, Uri uri, Action opened)
        {
            try
            {
                var bytes = await DownloadAsync(uri);
                if (!IsSoftwareSource(bitmap, uri)) return;

                if (bytes == null)
                {
                    FallBack(bitmap, uri);
                    return;
                }

                IRandomAccessStream stream;
                if (IsWebp(bytes))
                {
                    int width;
                    int height;
                    TargetPixels(bitmap, out width, out height);
                    stream = await Task.Run(() => DecodeWebpGatedAsync(bytes, width, height));
                }
                else
                {
                    stream = new MemoryStream(bytes, false).AsRandomAccessStream();
                }

                if (stream == null)
                {
                    FallBack(bitmap, uri);
                    return;
                }

                using (stream)
                {
                    if (!IsSoftwareSource(bitmap, uri)) return;
                    await bitmap.SetSourceAsync(stream);
                }

                if (opened != null && IsSoftwareSource(bitmap, uri)) opened();
            }
            catch (Exception ex)
            {
                AppLog.Error("RemoteImage: could not show a picture", ex);
                FallBack(bitmap, uri);
            }
        }

        private static void FallBack(BitmapImage bitmap, Uri uri)
        {
            try
            {
                if (IsSoftwareSource(bitmap, uri)) bitmap.UriSource = uri;
            }
            catch (Exception ex)
            {
                AppLog.Error("RemoteImage: could not hand a picture back to the platform", ex);
            }
        }

        private static void TargetPixels(BitmapImage bitmap, out int width, out int height)
        {
            width = bitmap.DecodePixelWidth;
            height = bitmap.DecodePixelHeight;
            if (bitmap.DecodePixelType != DecodePixelType.Logical || (width <= 0 && height <= 0)) return;

            var scale = 1.0;
            try
            {
                scale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            }
            catch (Exception ex)
            {
                AppLog.Error("RemoteImage: could not read the display scale", ex);
            }

            width = (int)Math.Ceiling(width * scale);
            height = (int)Math.Ceiling(height * scale);
        }

        private static bool IsWebp(byte[] bytes)
        {
            return bytes.Length >= 12
                   && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                   && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P';
        }

        private static async Task<IRandomAccessStream> DecodeWebpGatedAsync(byte[] bytes, int width, int height)
        {
            await s_decodeGate.WaitAsync();
            try
            {
                return DecodeWebp(bytes, width, height);
            }
            finally
            {
                s_decodeGate.Release();
            }
        }

        private static IRandomAccessStream DecodeWebp(byte[] bytes, int width, int height)
        {
            var configuration = s_webpConfiguration.Value;
            var decoder = new WebpDecoder { IgnoreMetadata = true };

            using (var input = new MemoryStream(bytes, false))
            {
                var info = decoder.Identify(configuration, input, CancellationToken.None);
                if (info == null || info.Width <= 0 || info.Height <= 0
                    || info.Width > MaxWebpSide || info.Height > MaxWebpSide
                    || (long)info.Width * info.Height > MaxWebpPixels)
                {
                    Debug.WriteLine("RemoteImage: WebP picture is empty or over the size cap");
                    return null;
                }

                input.Position = 0;
                using (var image = decoder.Decode<Rgba32>(configuration, input, CancellationToken.None))
                {
                    var firstFrame = image.Frames.Count > 1 ? image.Frames.CloneFrame(0) : null;
                    try
                    {
                        var still = firstFrame ?? image;
                        ShrinkTo(still, width, height);

                        var output = new MemoryStream();
                        still.Save(output, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed, IgnoreMetadata = true });
                        output.Position = 0;
                        return output.AsRandomAccessStream();
                    }
                    finally
                    {
                        if (firstFrame != null) firstFrame.Dispose();
                    }
                }
            }
        }

        private static void ShrinkTo(Image<Rgba32> image, int width, int height)
        {
            var scale = 1.0;
            if (width > 0 && height > 0) scale = Math.Max((double)width / image.Width, (double)height / image.Height);
            else if (width > 0) scale = (double)width / image.Width;
            else if (height > 0) scale = (double)height / image.Height;

            if (scale >= 1) return;

            var newWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
            var newHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
            image.Mutate(context => context.Resize(newWidth, newHeight));
        }

        private static async Task<byte[]> DownloadAsync(Uri uri)
        {
            using (var timeout = new CancellationTokenSource(DownloadTimeout))
            {
                try
                {
                    using (var response = await s_client.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).AsTask(timeout.Token))
                    {
                        if (!response.IsSuccessStatusCode) return null;

                        var length = response.Content.Headers.ContentLength;
                        if (length.HasValue && length.Value > MaxImageBytes) return null;

                        using (var input = await response.Content.ReadAsInputStreamAsync().AsTask(timeout.Token))
                        using (var memory = new MemoryStream())
                        {
                            var chunk = new Windows.Storage.Streams.Buffer(DownloadChunkBytes);
                            while (true)
                            {
                                var read = await input.ReadAsync(chunk, chunk.Capacity, InputStreamOptions.Partial).AsTask(timeout.Token);
                                if (read.Length == 0) break;
                                if (memory.Length + read.Length > MaxImageBytes) return null;

                                var data = read.ToArray();
                                await memory.WriteAsync(data, 0, data.Length, timeout.Token);
                            }

                            return memory.Length == 0 ? null : memory.ToArray();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("RemoteImage: download timed out");
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("RemoteImage: download failed - " + ex.Message);
                    return null;
                }
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();

            var version = AppConstants.AppVersionDisplay;
            int space = version.IndexOf(' ');
            if (space > 0) version = version.Substring(0, space);

            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind/" + version))
            {
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind");
            }

            return client;
        }

        private static SixLabors.ImageSharp.Configuration CreateWebpConfiguration()
        {
            var configuration = SixLabors.ImageSharp.Configuration.Default.Clone();
            configuration.MaxDegreeOfParallelism = 1;
            configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = WebpPoolMegabytes });
            return configuration;
        }

        private static bool DetectSystemWebp()
        {
            try
            {
                return BitmapDecoder.GetDecoderInformationEnumerator()
                                    .Any(info => info.MimeTypes.Any(mime => string.Equals(mime, "image/webp", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                AppLog.Error("RemoteImage: could not list the system's image decoders", ex);
                return true;
            }
        }
    }
}
