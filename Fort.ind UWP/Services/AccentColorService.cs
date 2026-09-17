using System;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public static class AccentColorService
    {
        public const double MinimumTextContrast = 4.5;

        public const double MinimumFillContrast = 3.0;

        private const double ContrastTolerance = 0.01;

        public const int DarkThemeTextShade = 2;
        public const int LightThemeTextShade = -1;

        private static readonly Color s_darkSurface = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_lightSurface = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private static readonly string[] s_shadeKeys =
        {
            "SystemAccentColorDark3", "SystemAccentColorDark2", "SystemAccentColorDark1",
            "SystemAccentColor",
            "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3"
        };

        private static string s_activeAccentHex;
        private static bool s_applied;

        public static string ActiveAccentHex
        {
            get { return s_activeAccentHex; }
        }

        public static string SavedAccentTag
        {
            get
            {
                try
                {
                    var tag = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppAccentColor]?.ToString();
                    return IsUsableTag(tag) ? tag : AppConstants.ThemeDefault;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AccentColorService: could not read the saved accent - {ex.Message}");
                    return AppConstants.ThemeDefault;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppAccentColor] =
                    IsUsableTag(value) ? value : AppConstants.ThemeDefault;
            }
        }

        public static bool IsRestartPending
        {
            get
            {
                return !string.Equals(ResolveSavedAccentHex(), s_activeAccentHex, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void ApplySavedAccent()
        {
            if (s_applied) return;

            ResourceDictionary resources = null;
            try
            {
                s_applied = true;

                var hex = ResolveSavedAccentHex();
                Color accent;
                if (hex == null || !ColorHelper.TryHexToColor(hex, out accent)) return;

                resources = FindOverrideDictionary(Application.Current.Resources);

                for (var i = 0; i < s_shadeKeys.Length; i++)
                {
                    resources[s_shadeKeys[i]] = ColorHelper.AccentShade(accent, i - 3);
                }

                s_activeAccentHex = hex;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AccentColorService: could not apply the saved accent - {ex.Message}");
                if (resources != null)
                {
                    foreach (var key in s_shadeKeys)
                    {
                        try { resources.Remove(key); } catch { }
                    }
                }
                s_activeAccentHex = null;
            }
        }

        internal static ResourceDictionary FindOverrideDictionary(ResourceDictionary root)
        {
            var merged = root.MergedDictionaries;
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                if (merged[i].Source == null) return merged[i];
            }

            var created = new ResourceDictionary();
            merged.Add(created);
            return created;
        }

        public static string ResolveSavedAccentHex()
        {
            var tag = SavedAccentTag;
            if (tag == AppConstants.ThemeDefault) return null;

            if (tag == AppConstants.AccentMatchTint)
            {
                string tint = null;
                try
                {
                    tint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AccentColorService: could not read the tint to match - {ex.Message}");
                }

                return ColorHelper.AccentForTint(tint);
            }

            Color custom;
            return ColorHelper.TryHexToColor(tag, out custom) ? ColorHelper.ColorToHex(custom) : null;
        }

        public static void CheckContrast(Color accent, out bool failsDark, out bool failsLight)
        {
            var whiteOnAccent = Fails(ColorHelper.ContrastRatio(accent, Colors.White), MinimumTextContrast);

            failsDark = whiteOnAccent
                        || Fails(ColorHelper.ContrastRatio(ColorHelper.AccentShade(accent, DarkThemeTextShade), s_darkSurface), MinimumTextContrast)
                        || Fails(ColorHelper.ContrastRatio(accent, s_darkSurface), MinimumFillContrast);

            failsLight = whiteOnAccent
                         || Fails(ColorHelper.ContrastRatio(ColorHelper.AccentShade(accent, LightThemeTextShade), s_lightSurface), MinimumTextContrast)
                         || Fails(ColorHelper.ContrastRatio(accent, s_lightSurface), MinimumFillContrast);
        }

        private static bool Fails(double ratio, double minimum)
        {
            return ratio < minimum - ContrastTolerance;
        }

        private static bool IsUsableTag(string tag)
        {
            if (tag == AppConstants.ThemeDefault || tag == AppConstants.AccentMatchTint) return true;

            Color ignored;
            return ColorHelper.TryHexToColor(tag, out ignored);
        }
    }
}
