using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.UI;

namespace Fort.ind_UWP
{
    public static class ColorHelper
    {
        private static readonly Dictionary<string, string> s_lightTintMap = new Dictionary<string, string>()
        {
            { "#1E3A5F", "#C8E0F5" },
            { "#2D1B69", "#DDD0F5" },
            { "#0F3D2E", "#C5E8D5" },
            { "#3D1515", "#F5CECE" },
            { "#1A1A2E", "#D0D0EA" },
            { "#0E3A3A", "#C5E8E8" },
            { "#3D2A0F", "#F5E3C0" },
            { "#3D1533", "#F5CEE9" },
            { "#2E3D0F", "#DEEBC0" },
            { "#232323", "#DCDCDC" }
        };

        public static string TryGetLightPreset(string darkHex)
        {
            if (darkHex == null) return null;

            string lightHex;
            return s_lightTintMap.TryGetValue(darkHex, out lightHex) ? lightHex : null;
        }

        // The tint presets are dark surface colours and fail contrast as accents. Each maps to the
        // brighter chip colour its swatch already shows in Settings, so "match tint" gives the
        // user exactly the colour they clicked on.
        private static readonly Dictionary<string, string> s_tintAccentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#1E3A5F", "#1D6FA5" },
            { "#2D1B69", "#6041B0" },
            { "#0F3D2E", "#1B7A4E" },
            { "#3D1515", "#B03232" },
            { "#1A1A2E", "#404080" },
            { "#0E3A3A", "#1B8A8A" },
            { "#3D2A0F", "#B07D1B" },
            { "#3D1533", "#B03291" },
            { "#2E3D0F", "#6E8A1B" },
            { "#232323", "#5A5A5A" }
        };

        /// <summary>
        /// The accent that pairs with a background tint tag, or null when the tag is the default
        /// surface (or not a colour) and there is nothing to match.
        /// </summary>
        public static string AccentForTint(string tintTag)
        {
            if (string.IsNullOrEmpty(tintTag)) return null;

            string preset;
            if (s_tintAccentMap.TryGetValue(tintTag, out preset)) return preset;

            Color tint;
            if (!TryHexToColor(tintTag, out tint)) return null;

            // A custom tint keeps its hue and saturation and is lifted into the lightness band
            // the presets above occupy.
            double h, s, l;
            ToHsl(tint, out h, out s, out l);
            return ColorToHex(FromHsl(h, s, Math.Max(0.40, Math.Min(0.55, l))));
        }

        /// <summary>
        /// One step of the accent palette: 1-3 for SystemAccentColorLight1-3, -1 to -3 for
        /// Dark1-3, 0 for the accent itself.
        /// </summary>
        /// <remarks>
        /// The shell's own shade algorithm is not public (doc dump chunk_029, "Accent color
        /// palette", says only that one exists). The step sizes below are measured from a palette
        /// the shell actually generated - HKCU\...\Explorer\Accent\AccentPalette for #2475D1:
        /// Light3 #AFE5F7, Light2 #86C4EC, Light1 #3A8BDD, Dark1 #1C5CB4, Dark2 #123C90, Dark3
        /// #061866. Lightness moves that fraction of the way to white or black, and saturation
        /// rises toward the extremes by the same ratio it did there, which matches that palette's
        /// lightness and saturation at every step (Light2 comes out #86B6EC). Not modelled: the
        /// shell also rotates the hue (toward cyan for lights, violet for darks, on blue), and one
        /// sample says nothing about which way it turns for other hues.
        /// </remarks>
        public static Color AccentShade(Color accent, int step)
        {
            if (step == 0) return accent;

            double h, s, l;
            ToHsl(accent, out h, out s, out l);

            var index = Math.Min(3, Math.Abs(step));
            if (step > 0)
            {
                l += (1 - l) * s_lightStepFraction[index];
                s *= s_lightSaturationRatio[index];
            }
            else
            {
                l *= 1 - s_darkStepFraction[index];
                s *= s_darkSaturationRatio[index];
            }

            // A ratio, not an offset, so a grey accent (s = 0) stays grey instead of picking up
            // the red of hue 0.
            return FromHsl(h, Math.Min(1, s), l);
        }

        private static readonly double[] s_lightStepFraction = { 0, 0.13, 0.47, 0.67 };
        private static readonly double[] s_darkStepFraction = { 0, 0.15, 0.34, 0.56 };
        private static readonly double[] s_lightSaturationRatio = { 1, 1, 1.03, 1.15 };
        private static readonly double[] s_darkSaturationRatio = { 1, 1.03, 1.10, 1.25 };

        private static void ToHsl(Color c, out double h, out double s, out double l)
        {
            var r = c.R / 255.0;
            var g = c.G / 255.0;
            var b = c.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;

            l = (max + min) / 2;
            if (delta == 0)
            {
                h = 0;
                s = 0;
                return;
            }

            s = delta / (1 - Math.Abs(2 * l - 1));

            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);

            if (h < 0) h += 360;
        }

        private static Color FromHsl(double h, double s, double l)
        {
            var chroma = (1 - Math.Abs(2 * l - 1)) * s;
            var x = chroma * (1 - Math.Abs((h / 60) % 2 - 1));
            var m = l - chroma / 2;

            double r, g, b;
            if (h < 60) { r = chroma; g = x; b = 0; }
            else if (h < 120) { r = x; g = chroma; b = 0; }
            else if (h < 180) { r = 0; g = chroma; b = x; }
            else if (h < 240) { r = 0; g = x; b = chroma; }
            else if (h < 300) { r = x; g = 0; b = chroma; }
            else { r = chroma; g = 0; b = x; }

            return Color.FromArgb(255, ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static byte ToByte(double channel)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(channel * 255, MidpointRounding.ToEven)));
        }

        public static Color ForLightTheme(string darkHex)
        {
            var preset = TryGetLightPreset(darkHex);
            return preset != null ? HexToColor(preset) : LightenForLightTheme(HexToColor(darkHex));
        }

        
        /// <remarks>
        /// Keep using this for the built-in palette, where the input is a literal in this file and
        /// a failure really is a bug. For anything read back from LocalSettings use
        /// <see cref="TryHexToColor"/>: the old version had four distinct ways to throw on a value
        /// a user could have corrupted, and the caller's catch then swallowed it.
        /// </remarks>
        public static Color HexToColor(string hex)
        {
            Color color;
            if (!TryHexToColor(hex, out color))
            {
                throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
            }

            return color;
        }

        /// <summary>
        /// Parses a #RRGGBB string, returning false rather than throwing on malformed input.
        /// </summary>
        public static bool TryHexToColor(string hex, out Color color)
        {
            color = Colors.Transparent;

            if (string.IsNullOrWhiteSpace(hex)) return false;

            var trimmed = hex.Trim().TrimStart('#');
            if (trimmed.Length != 6) return false;

            byte r, g, b;
            if (!TryParseHexByte(trimmed, 0, out r)) return false;
            if (!TryParseHexByte(trimmed, 2, out g)) return false;
            if (!TryParseHexByte(trimmed, 4, out b)) return false;

            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        private static bool TryParseHexByte(string value, int start, out byte result)
        {
            return byte.TryParse(value.Substring(start, 2),
                                 NumberStyles.HexNumber,
                                 CultureInfo.InvariantCulture,
                                 out result);
        }

        public static string ColorToHex(Color c)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static Color LightenForLightTheme(Color c)
        {
            const double keep = 0.22;
            return Color.FromArgb(255,
                                  (byte)Math.Round(255 - (255 - (int)c.R) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.G) * keep, MidpointRounding.ToEven),
                                  (byte)Math.Round(255 - (255 - (int)c.B) * keep, MidpointRounding.ToEven));
        }

        /// <summary>
        /// Black or white, whichever reads better on <paramref name="background"/>. Used for the
        /// checkmark drawn on the selected tint swatch, which sits on twelve different chip
        /// colours - a fixed foreground fails half of them.
        /// </summary>
        public static Color ContrastingForeground(Color background)
        {
            // The 0.179 threshold is the luminance where contrast against black and against white
            // are equal, so it maximises whichever we pick.
            return RelativeLuminance(background) > 0.179 ? Colors.Black : Colors.White;
        }

        /// <summary>
        /// WCAG contrast ratio between two colours, from 1 (identical) to 21 (black on white).
        /// </summary>
        public static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            return 0.2126 * LinearizeChannel(c.R)
                   + 0.7152 * LinearizeChannel(c.G)
                   + 0.0722 * LinearizeChannel(c.B);
        }

        private static double LinearizeChannel(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}
