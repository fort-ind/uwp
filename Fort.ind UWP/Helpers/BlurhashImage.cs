using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public static class BlurhashImage
    {
        public static byte[] Decode(string blurhash, int width, int height)
        {
            if (string.IsNullOrEmpty(blurhash)) return null;

            try
            {
                return Blurhash.DecodeToBgra(blurhash, width, height);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BlurhashImage: decode failed - {ex.Message}");
                return null;
            }
        }

        public static WriteableBitmap CreateBitmap(string blurhash, int width, int height)
        {
            return CreateBitmap(Decode(blurhash, width, height), width, height);
        }

        public static WriteableBitmap CreateBitmap(byte[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length != width * height * 4) return null;

            try
            {
                WriteableBitmap bitmap = new WriteableBitmap(width, height);
                using (var stream = bitmap.PixelBuffer.AsStream())
                {
                    stream.Write(pixels, 0, pixels.Length);
                }
                bitmap.Invalidate();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BlurhashImage: placeholder failed - {ex.Message}");
                return null;
            }
        }

        public static Brush CreateBrush(string blurhash, int width, int height, Stretch stretch)
        {
            return CreateBrush(Decode(blurhash, width, height), width, height, stretch);
        }

        public static Brush CreateBrush(byte[] pixels, int width, int height, Stretch stretch)
        {
            var bitmap = CreateBitmap(pixels, width, height);
            return bitmap == null ? null : new ImageBrush { ImageSource = bitmap, Stretch = stretch };
        }

        public static Color? AverageColor(byte[] pixels, int width, int height, double fromRow, double toRow)
        {
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length != width * height * 4) return null;

            var first = Math.Max(0, Math.Min(height - 1, (int)Math.Floor(fromRow * height)));
            var last = Math.Max(first + 1, Math.Min(height, (int)Math.Ceiling(toRow * height)));

            long b = 0, g = 0, r = 0;
            var count = 0;
            for (var y = first; y < last; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 4;
                    b += pixels[offset];
                    g += pixels[offset + 1];
                    r += pixels[offset + 2];
                    count++;
                }
            }

            if (count == 0) return null;

            return Color.FromArgb(255, (byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
    }
}
