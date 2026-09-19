using System;

namespace Fort.ind_UWP
{
    public static class Blurhash
    {
        private const string Base83Alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

        public static byte[] DecodeToBgra(string hash, int width, int height)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length < 6 || width <= 0 || height <= 0) return null;

            int sizeFlag;
            if (!TryDecode83(hash, 0, 1, out sizeFlag)) return null;

            int componentsX = (sizeFlag % 9) + 1;
            int componentsY = (sizeFlag / 9) + 1;
            int componentCount = componentsX * componentsY;
            if (hash.Length != 4 + 2 * componentCount) return null;

            int quantisedMaximum;
            if (!TryDecode83(hash, 1, 1, out quantisedMaximum)) return null;
            double maximumValue = (quantisedMaximum + 1) / 166.0;

            var colors = new double[componentCount * 3];

            int dc;
            if (!TryDecode83(hash, 2, 4, out dc)) return null;
            colors[0] = SrgbToLinear(dc >> 16);
            colors[1] = SrgbToLinear((dc >> 8) & 255);
            colors[2] = SrgbToLinear(dc & 255);

            for (int i = 1; i < componentCount; i++)
            {
                int ac;
                if (!TryDecode83(hash, 4 + i * 2, 2, out ac)) return null;

                colors[i * 3] = SignedSquare((ac / (19 * 19) - 9) / 9.0) * maximumValue;
                colors[i * 3 + 1] = SignedSquare((ac / 19 % 19 - 9) / 9.0) * maximumValue;
                colors[i * 3 + 2] = SignedSquare((ac % 19 - 9) / 9.0) * maximumValue;
            }

            var cosinesX = new double[width * componentsX];
            for (int x = 0; x < width; x++)
            {
                for (int i = 0; i < componentsX; i++)
                {
                    cosinesX[x * componentsX + i] = Math.Cos(Math.PI * x * i / width);
                }
            }

            var cosinesY = new double[height * componentsY];
            for (int y = 0; y < height; y++)
            {
                for (int j = 0; j < componentsY; j++)
                {
                    cosinesY[y * componentsY + j] = Math.Cos(Math.PI * y * j / height);
                }
            }

            var pixels = new byte[width * height * 4];
            int offset = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double r = 0, g = 0, b = 0;

                    for (int j = 0; j < componentsY; j++)
                    {
                        for (int i = 0; i < componentsX; i++)
                        {
                            double basis = cosinesX[x * componentsX + i] * cosinesY[y * componentsY + j];
                            int c = (i + j * componentsX) * 3;
                            r += colors[c] * basis;
                            g += colors[c + 1] * basis;
                            b += colors[c + 2] * basis;
                        }
                    }

                    pixels[offset] = LinearToSrgb(b);
                    pixels[offset + 1] = LinearToSrgb(g);
                    pixels[offset + 2] = LinearToSrgb(r);
                    pixels[offset + 3] = 255;
                    offset += 4;
                }
            }

            return pixels;
        }

        private static bool TryDecode83(string hash, int start, int length, out int value)
        {
            value = 0;
            for (int k = start; k < start + length; k++)
            {
                int digit = Base83Alphabet.IndexOf(hash[k]);
                if (digit < 0) return false;
                value = value * 83 + digit;
            }
            return true;
        }

        private static double SrgbToLinear(int channel)
        {
            double v = channel / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static byte LinearToSrgb(double value)
        {
            double v = Math.Max(0, Math.Min(1, value));
            double srgb = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Round(srgb * 255, MidpointRounding.AwayFromZero);
        }

        private static double SignedSquare(double value)
        {
            return Math.Sign(value) * value * value;
        }
    }
}
