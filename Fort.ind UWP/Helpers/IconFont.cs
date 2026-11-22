using System;
using System.Collections.Concurrent;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;

namespace Fort.ind_UWP
{
    internal static class IconFont
    {
        private const string FamilyName = "Segoe MDL2 Assets";

        private static readonly ConcurrentDictionary<int, bool> s_hasGlyph = new ConcurrentDictionary<int, bool>();

        public static string Pick(string preferred, string fallback)
        {
            if (string.IsNullOrEmpty(preferred)) return fallback;
            return s_hasGlyph.GetOrAdd(preferred[0], HasGlyph) ? preferred : fallback;
        }

        private static bool HasGlyph(int codepoint)
        {
            try
            {
                using (var system = CanvasFontSet.GetSystemFontSet())
                using (var matches = system.GetMatchingFonts(FamilyName, FontWeights.Normal, FontStretch.Normal, FontStyle.Normal))
                {
                    if (matches.Fonts.Count == 0) return false;

                    using (var face = matches.Fonts[0])
                    {
                        return face.HasCharacter((uint)codepoint);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("IconFont: could not read the icon font", ex);
                return false;
            }
        }
    }
}
