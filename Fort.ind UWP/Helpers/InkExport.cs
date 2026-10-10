using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace Fort.ind_UWP
{
    public static class InkExport
    {
        private const float JpegQuality = 0.92f;

        public static async Task<SoftwareBitmap> DecodePhotoAsync(IRandomAccessStream stream, uint maximumSide)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            var scale = longest > maximumSide ? (double)maximumSide / longest : 1.0;

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                                                        BitmapAlphaMode.Premultiplied,
                                                        transform,
                                                        ExifOrientationMode.RespectExifOrientation,
                                                        ColorManagementMode.DoNotColorManage);
        }

        public static async Task<byte[]> RenderAsync(IReadOnlyList<InkStroke> strokes,
                                                     Size canvasSize,
                                                     int pixelWidth,
                                                     int pixelHeight,
                                                     Color background,
                                                     SoftwareBitmap photo,
                                                     bool jpeg)
        {
            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, pixelWidth, pixelHeight, 96))
            {
                using (var session = target.CreateDrawingSession())
                {
                    session.Clear(background);

                    if (photo != null)
                    {
                        using (var bitmap = CanvasBitmap.CreateFromSoftwareBitmap(device, photo))
                        {
                            session.DrawImage(bitmap, new Rect(0, 0, pixelWidth, pixelHeight));
                        }
                    }

                    session.Transform = Matrix3x2.CreateScale((float)(pixelWidth / canvasSize.Width),
                                                              (float)(pixelHeight / canvasSize.Height));
                    if (strokes.Count > 0) session.DrawInk(strokes);
                }

                using (var output = new InMemoryRandomAccessStream())
                {
                    if (jpeg) await target.SaveAsync(output, CanvasBitmapFileFormat.Jpeg, JpegQuality);
                    else await target.SaveAsync(output, CanvasBitmapFileFormat.Png);

                    var bytes = new byte[output.Size];
                    output.Seek(0);
                    await output.ReadAsync(bytes.AsBuffer(), (uint)output.Size, InputStreamOptions.None);
                    return bytes;
                }
            }
        }
    }
}
