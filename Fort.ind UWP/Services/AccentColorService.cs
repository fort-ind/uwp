using System;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The app's accent colour: the Windows accent by default, or one the user chose in Settings.
    /// </summary>
    /// <remarks>
    /// Applied once per process, at launch, and never live. Every accent brush in generic.xaml
    /// (SystemControlHighlightAccentBrush, SystemControlForegroundAccentBrush, the Reveal and
    /// acrylic accent brushes...) is a brush whose colour is resolved from SystemAccentColor when
    /// it is built. Replacing the colour resource afterwards leaves every brush already built on
    /// the old colour, with no error - the silent failure CLAUDE.md describes for tint brushes.
    /// So a new choice is saved immediately and Settings offers a restart to pick it up.
    /// </remarks>
    public static class AccentColorService
    {
        /// <summary>
        /// The text minimum the picker warns below (doc dump chunk_049, "Accessible text
        /// requirements": 4.5:1). Warns, still allows the colour.
        /// </summary>
        public const double MinimumTextContrast = 4.5;

        /// <summary>
        /// The accent itself is also a fill (accent buttons, toggles, selection) drawn against the
        /// page; 3:1 is the doc dump's large-element minimum (chunk_048, "Color contrast ratio").
        /// </summary>
        public const double MinimumFillContrast = 3.0;

        // The Windows 10 palette is designed to 4.5:1 against white, and after 8-bit rounding
        // several of its colours land a hair under - the default #0078D7 is 4.499. Without this,
        // the picker would warn about Windows' own accent.
        private const double ContrastTolerance = 0.01;

        // App.xaml draws accent-coloured text with these shades: SystemAccentColorLight2 in dark
        // theme, SystemAccentColorDark1 in light. Keep the two in step.
        public const int DarkThemeTextShade = 2;
        public const int LightThemeTextShade = -1;

        // The surfaces an accent is read against: the dark acrylic tint, and the light window
        // behind white cards (the darker of the two light surfaces, so the stricter check).
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

        /// <summary>
        /// The custom accent this process is painted with, or null for the Windows accent.
        /// </summary>
        public static string ActiveAccentHex
        {
            get { return s_activeAccentHex; }
        }

        /// <summary>
        /// The saved choice: "Default", <see cref="AppConstants.AccentMatchTint"/>, or #RRGGBB.
        /// </summary>
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

        /// <summary>
        /// True when the saved choice resolves to a different colour than this process is painted
        /// with. Follows the tint too: under "match tint", changing the tint changes the accent.
        /// </summary>
        public static bool IsRestartPending
        {
            get
            {
                return !string.Equals(ResolveSavedAccentHex(), s_activeAccentHex, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Writes the saved accent and its six shades into the application resources. Call at the
        /// top of OnLaunched / a cold OnActivated, before the first Frame is created.
        /// </summary>
        /// <remarks>
        /// Not from the App constructor: Application.Resources throws E_UNEXPECTED there. The
        /// dictionary is fetched inside the try for the same reason - that throw, taken while
        /// evaluating an argument, escaped App's constructor and fail-fast the process.
        /// </remarks>
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
                // Resources left partly written would mix two accents; the Windows accent is the
                // safer result, and it is what a failed read would have produced anyway.
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

        /// <summary>
        /// The app's own dictionary nested in XamlControlsResources.MergedDictionaries - the one
        /// App.xaml's ControlCornerRadius overrides already live in.
        /// </summary>
        /// <remarks>
        /// Not Application.Resources itself, although that is where the doc dump puts the accent
        /// override (chunk_029, "Overriding the accent color"): here that dictionary is
        /// XamlControlsResources, which loads through Source, and inserting into it throws
        /// "Local values are not allowed in resource dictionary with Source set". WinUI 2's own
        /// setup docs send app overrides to its MergedDictionaries instead. The last dictionary
        /// with no Source is App.xaml's; if that ever goes away, an empty one is appended.
        ///
        /// internal rather than private because MainPage.Appearance.cs needs the same dictionary
        /// to reach the pane acrylic brushes inside its ThemeDictionaries.
        /// </remarks>
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

        /// <summary>
        /// The colour the saved choice stands for right now, or null for the Windows accent.
        /// </summary>
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

                // Null for the default surface: nothing to match, so the Windows accent.
                return ColorHelper.AccentForTint(tint);
            }

            // Normalised through a parse, so " #0078d7" and "#0078D7" compare equal in
            // IsRestartPending. SavedAccentTag has already rejected anything that will not parse.
            Color custom;
            return ColorHelper.TryHexToColor(tag, out custom) ? ColorHelper.ColorToHex(custom) : null;
        }

        /// <summary>
        /// Whether <paramref name="accent"/> would be hard to read in the dark and the light theme,
        /// judged on what is actually drawn rather than the raw colour.
        /// </summary>
        /// <remarks>
        /// Per theme: the accent-coloured text shade against that theme's surface, and the accent
        /// as a fill against it. In both: the white label an accent button draws on the accent
        /// (AccentButtonForeground is SystemChromeWhiteColor in both themes in generic.xaml).
        /// </remarks>
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
