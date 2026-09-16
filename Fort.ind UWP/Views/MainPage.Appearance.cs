using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void LoadAppearanceSettings()
        {
            _loadingSettings = true;
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // ?? on top of ContainsKey: the key can be present with a null value, and ToString()
                // on that throws - out of the MainPage constructor, which fails the Navigate that
                // created the page and takes the app down through OnNavigationFailed.
                string theme = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTheme))
                {
                    theme = localSettings.Values[AppConstants.SettingAppTheme]?.ToString() ?? AppConstants.ThemeDefault;
                }
                switch (theme)
                {
                    case AppConstants.ThemeLight: ThemeLightRadio.IsChecked = true; break;
                    case AppConstants.ThemeDark: ThemeDarkRadio.IsChecked = true; break;
                    default: ThemeSystemRadio.IsChecked = true; break;
                }
                ApplyTheme(theme);

                string tintTag = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTintColor))
                {
                    tintTag = localSettings.Values[AppConstants.SettingAppTintColor]?.ToString() ?? AppConstants.ThemeDefault;
                }
                TintCustomButton.ClearValue(Control.BackgroundProperty);
                TintCustomIcon.Visibility = Visibility.Visible;

                // Before ApplyTintColor, which paints with them. An absent key has to reapply
                // the default rather than keep whatever the fields already hold: a reset clears
                // LocalSettings wholesale and then calls this method again, so "leave it alone"
                // would report restored defaults while the surfaces stayed as the user left them.
                LoadAcrylicSettings(localSettings);

                ApplyTintColor(tintTag);
                UpdateTintSelection(tintTag);

                var rememberedCustom = localSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (TintCustomIcon.Visibility == Visibility.Visible && !string.IsNullOrEmpty(rememberedCustom))
                {
                    ShowCustomSwatchColor(rememberedCustom);
                }

                TileBadgeToggle.IsOn = LiveTileService.BadgeEnabled;

                RestoreSettingsPanelStates();
            }
            catch (Exception ex)
            {
                // This runs from the MainPage constructor, where an escaping exception fails the
                // Navigate that created the page and takes the whole app down through
                // OnNavigationFailed - a settings value that will not read is not worth that. The
                // app comes up with whatever appearance was already applied instead.
                Debug.WriteLine($"MainPage: LoadAppearanceSettings failed - {ex.Message}");
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void ApplyTheme(string theme)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null) return;
            switch (theme)
            {
                case AppConstants.ThemeLight: rootFrame.RequestedTheme = ElementTheme.Light; break;
                case AppConstants.ThemeDark: rootFrame.RequestedTheme = ElementTheme.Dark; break;
                default: rootFrame.RequestedTheme = ElementTheme.Default; break;
            }
            if (_loadingSettings)
            {
                // LoadAppearanceSettings applies the tint itself straight after this.
                UpdateTitleBarColors();
                return;
            }

            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme] = theme;
            RepaintThemeDependentChrome();
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                RepaintThemeDependentChrome();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: OnActualThemeChanged failed – {ex.Message}");
            }
        }

        /// <summary>
        /// The (effective theme, tint) pair the chrome was last fully repainted for, or null when
        /// something has painted the tint since and the pair can no longer be trusted.
        /// </summary>
        private string _themePaintKey;

        /// <summary>
        /// Title bar, acrylic tint and swatches for the current effective theme and saved tint -
        /// skipped when they are already painted for exactly that pair.
        /// </summary>
        /// <remarks>
        /// Both ApplyTheme and OnActualThemeChanged repaint, and setting RequestedTheme raises
        /// ActualThemeChanged, so an explicit Light/Dark switch used to do the whole repaint twice.
        /// Neither call can simply be dropped. ActualThemeChanged does not fire when the effective
        /// theme stays the same (Dark to System on a dark PC), so ApplyTheme must paint; and for
        /// System, ActualTheme can lag RequestedTheme, so ApplyTheme may paint the old theme and
        /// the event is what corrects it. Keying on what was actually painted keeps both and makes
        /// whichever runs second a no-op only when it would repaint the same thing.
        ///
        /// ApplyTintColor clears the key, so a swatch click, the custom-colour preview or a
        /// settings reload - none of which go through here - can never leave it vouching for a
        /// paint that has since been replaced.
        /// </remarks>
        private void RepaintThemeDependentChrome()
        {
            var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
            if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;

            var key = (IsEffectiveThemeDark() ? "Dark|" : "Light|") + savedTint;
            if (string.Equals(key, _themePaintKey, StringComparison.Ordinal)) return;

            UpdateTitleBarColors();
            ApplyTintColor(savedTint);
            UpdateTintSelection(savedTint);

            // The legibility floor is per-theme (70% dark, 50% light), so a theme switch can put
            // an unchanged slider on the other side of it.
            UpdateAcrylicLegibilityWarnings();

            _themePaintKey = key;
        }

        private AcrylicBrush _surfaceBrush;

        private static readonly Color s_surfaceTintDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_surfaceTintLight = Colors.White;
        private static readonly Color s_surfaceFallbackLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        // The untinted nav pane, mirroring what App.xaml declares its two pane brushes with:
        // SystemChromeMediumColor (#1F1F1F) in dark, SystemChromeMediumLowColor (#F2F2F2) in
        // light. Literals here for the same reason the window acrylic pair above is - a
        // ResourceDictionary indexer does not search ThemeDictionaries, so the declared values
        // cannot be read back out. Keep them in step with App.xaml.
        private static readonly Color s_paneTintDark = Color.FromArgb(255, 0x1F, 0x1F, 0x1F);
        private static readonly Color s_paneTintLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private double _bodyAcrylicOpacity = AppConstants.DefaultBodyAcrylicOpacity;
        private double _paneAcrylicOpacity = AppConstants.DefaultPaneAcrylicOpacity;
        private string _tintScope = AppConstants.TintScopeDefault;

        /// <summary>
        /// The normalised tint tag the surfaces are currently painted with.
        /// </summary>
        /// <remarks>
        /// Kept here rather than re-read from LocalSettings by the sliders: they repaint on every
        /// tick of a drag, and a settings read plus a hex parse per tick is work this already knows
        /// the answer to. ApplyTintColor is the only writer, and it writes the value it has already
        /// validated.
        /// </remarks>
        private string _tintTag = AppConstants.ThemeDefault;

        // What UpdateAcrylicLegibilityWarnings last painted, so a drag that changes nothing on
        // screen does not re-resolve the resource string.
        private string _warningKey;
        private double _warningFloor = -1;
        private bool _warningShown;

        private readonly Debouncer _acrylicPersistDebouncer = new Debouncer();

        private void ApplyTintColor(string colorTag)
        {
            _themePaintKey = null;

            // Normalise an unusable tag up front, before anything can persist it. The catch below
            // used to swallow the parse failure and the write at the bottom then stored the bad tag
            // anyway - so one corrupt value made every subsequent launch fail in exactly the same
            // way, silently, with the window coming up untinted and no way to notice why.
            if (!IsUsableTintTag(colorTag))
            {
                Debug.WriteLine($"MainPage: tint tag '{colorTag}' is not a colour; using the default surface");
                colorTag = AppConstants.ThemeDefault;
            }

            _tintTag = colorTag;
            ApplySurfaceBrushes(colorTag);

            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor] = colorTag;
            }
        }

        /// <summary>
        /// Repaints the window body and the nav pane for the given tint tag, the saved tint scope
        /// and the two saved opacities. The caller must have normalised <paramref name="colorTag"/>
        /// already (see <see cref="IsUsableTintTag"/>).
        /// </summary>
        /// <remarks>
        /// Separate from ApplyTintColor so the sliders and the scope radios can repaint without
        /// going near the tint tag's persistence.
        /// </remarks>
        private void ApplySurfaceBrushes(string colorTag)
        {
            try
            {
                var isDark = IsEffectiveThemeDark();
                var isTinted = !(string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault);

                // The scope says which surfaces the colour reaches; the other one falls back to
                // its plain chrome surface rather than to no acrylic at all.
                var tintBody = isTinted && _tintScope != AppConstants.TintScopeSidebar;
                var tintPane = isTinted && _tintScope != AppConstants.TintScopeContent;

                Color bodyTint;
                Color bodyFallback;
                if (tintBody)
                {
                    bodyTint = isDark ? ColorHelper.HexToColor(colorTag) : ColorHelper.ForLightTheme(colorTag);
                    bodyFallback = bodyTint;
                }
                else
                {
                    bodyTint = isDark ? s_surfaceTintDark : s_surfaceTintLight;
                    bodyFallback = isDark ? s_surfaceTintDark : s_surfaceFallbackLight;
                }

                if (_surfaceBrush == null)
                {
                    _surfaceBrush = new AcrylicBrush()
                    {
                        BackgroundSource = AcrylicBackgroundSource.HostBackdrop
                    };
                }
                _surfaceBrush.TintColor = bodyTint;
                _surfaceBrush.TintOpacity = _bodyAcrylicOpacity;
                _surfaceBrush.FallbackColor = bodyFallback;

                if (!ReferenceEquals(RootGrid.Background, _surfaceBrush))
                {
                    RootGrid.Background = _surfaceBrush;
                }

                ApplyPaneBrushes(colorTag, tintPane);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ApplySurfaceBrushes failed – {ex.Message}");
            }
        }

        /// <remarks>
        /// Both themes' brushes are repainted, not just the active one, so a later theme switch
        /// already finds the inactive dictionary correct - the framework re-resolves the
        /// {ThemeResource} to the other instance and never comes back through here for it.
        /// </remarks>
        private void ApplyPaneBrushes(string colorTag, bool tinted)
        {
            var darkTint = tinted ? ColorHelper.HexToColor(colorTag) : s_paneTintDark;
            var lightTint = tinted ? ColorHelper.ForLightTheme(colorTag) : s_paneTintLight;

            foreach (var pair in PaneAcrylicBrushes())
            {
                var tint = pair.Key ? darkTint : lightTint;
                pair.Value.TintColor = tint;
                pair.Value.FallbackColor = tint;
                pair.Value.TintOpacity = _paneAcrylicOpacity;
            }
        }

        // Expanded pane mode uses the first; every other mode - the compact rail and the overlay
        // pane a narrow window opens - uses the second. generic.xaml applies the Expanded one from
        // a VisualState setter, which is also why the brush *instances* are mutated here rather
        // than RootSplitView.PaneBackground being assigned: the next state change would overwrite
        // an assignment, but nothing reassigns a brush's own dependency properties.
        private static readonly string[] s_paneBrushKeys =
        {
            "NavigationViewExpandedPaneBackground",
            "NavigationViewDefaultPaneBackground",
        };

        /// <summary>
        /// Every pane acrylic brush App.xaml declares, paired with true for the dark theme.
        /// </summary>
        /// <remarks>
        /// ThemeDictionaries is indexed by name on purpose: a ResourceDictionary's own indexer
        /// does not search them, so there is no way to reach these through the flat dictionary
        /// AccentColorService writes the accent shades into.
        ///
        /// HighContrast is deliberately not visited. Its entries are SolidColorBrushes - acrylic
        /// there makes the framework fall back to a fixed FallbackColor and ignore the user's
        /// chosen scheme - so the transparency sliders simply do not apply in high contrast. The
        /// cast below would drop them anyway.
        /// </remarks>
        private static List<KeyValuePair<bool, AcrylicBrush>> s_paneBrushes;

        private static List<KeyValuePair<bool, AcrylicBrush>> PaneAcrylicBrushes()
        {
            // Resolved once: these instances live in App.xaml's dictionary for the life of the
            // process, and a slider drag asks for them on every tick.
            if (s_paneBrushes != null) return s_paneBrushes;

            var found = new List<KeyValuePair<bool, AcrylicBrush>>(s_paneBrushKeys.Length * 2);

            try
            {
                var themes = AccentColorService.FindOverrideDictionary(Application.Current.Resources).ThemeDictionaries;

                // "Default" is this app's dark dictionary; App.xaml never declares a "Dark" one.
                CollectPaneBrushes(themes, "Default", true, found);
                CollectPaneBrushes(themes, AppConstants.ThemeLight, false, found);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not reach the pane acrylic brushes – {ex.Message}");
            }

            // A failed or empty resolution is not cached, so a later call can still succeed
            // rather than the pane being stuck untouchable for the rest of the process.
            if (found.Count > 0) s_paneBrushes = found;

            return found;
        }

        private static void CollectPaneBrushes(IDictionary<object, object> themes, string themeKey, bool isDark,
                                               List<KeyValuePair<bool, AcrylicBrush>> into)
        {
            object entry;
            if (!themes.TryGetValue(themeKey, out entry)) return;

            var dictionary = entry as ResourceDictionary;
            if (dictionary == null) return;

            foreach (var key in s_paneBrushKeys)
            {
                object value;
                if (!dictionary.TryGetValue(key, out value)) continue;

                var acrylic = value as AcrylicBrush;
                if (acrylic != null)
                {
                    into.Add(new KeyValuePair<bool, AcrylicBrush>(isDark, acrylic));
                }
            }
        }

        /// <summary>
        /// True for the sentinel "Default" and for any tag that really parses as a colour.
        /// </summary>
        private static bool IsUsableTintTag(string colorTag)
        {
            if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault) return true;

            Color ignored;
            return ColorHelper.TryHexToColor(colorTag, out ignored);
        }

        /// <remarks>
        /// An explicit Light/Dark choice is read from RequestedTheme, which is exact the moment
        /// ApplyTheme sets it. "System" is read from the frame's ActualTheme (16299+, so fine on
        /// the 1809 floor), never Application.RequestedTheme: that one is fixed at startup, so
        /// switching Windows between light and dark while the app ran left the title bar buttons
        /// and the acrylic tint painted for the old theme. If ActualTheme has not caught up yet
        /// when ApplyTheme calls this, ActualThemeChanged follows and OnActualThemeChanged repaints.
        /// </remarks>
        private static bool IsEffectiveThemeDark()
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                return Application.Current.RequestedTheme == ApplicationTheme.Dark;
            }

            if (rootFrame.RequestedTheme != ElementTheme.Default)
            {
                return rootFrame.RequestedTheme == ElementTheme.Dark;
            }

            return rootFrame.ActualTheme == ElementTheme.Dark;
        }

        private Dictionary<Button, string> _swatchBaseNames;

        private string BaseSwatchName(Button btn)
        {
            if (_swatchBaseNames == null)
            {
                _swatchBaseNames = new Dictionary<Button, string>();
            }

            string name;
            if (!_swatchBaseNames.TryGetValue(btn, out name))
            {
                name = Windows.UI.Xaml.Automation.AutomationProperties.GetName(btn) ?? "";
                _swatchBaseNames[btn] = name;
            }

            return name;
        }

        private Button[] _tintPresetSwatches;

        private Button[] TintPresetSwatches
        {
            get
            {
                if (_tintPresetSwatches == null)
                {
                    _tintPresetSwatches = new Button[] { TintDefaultButton, TintBlueButton, TintPurpleButton, TintGreenButton,
                                                         TintRedButton, TintSlateButton, TintTealButton, TintBronzeButton,
                                                         TintRoseButton, TintOliveButton, TintGraphiteButton };
                }
                return _tintPresetSwatches;
            }
        }

        private static readonly SolidColorBrush s_restBrushDark = new SolidColorBrush(Colors.Transparent);
        private static readonly SolidColorBrush s_restBrushLight = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0));

        private static readonly SolidColorBrush s_selectedBrushDark = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush s_selectedBrushLight = new SolidColorBrush(Colors.Black);

        private Dictionary<Button, Brush> _swatchChipsDark;
        private Dictionary<Button, Brush> _swatchChipsLight;

        private void UpdateSwatchChipColors(bool isDark)
        {
            if (_swatchChipsDark == null)
            {
                _swatchChipsDark = new Dictionary<Button, Brush>();
                _swatchChipsLight = new Dictionary<Button, Brush>();
                foreach (var btn in TintPresetSwatches)
                {
                    var tag = btn.Tag?.ToString() ?? "";
                    var lightHex = ColorHelper.TryGetLightPreset(tag);
                    if (lightHex == null) continue;
                    _swatchChipsDark[btn] = btn.Background;
                    _swatchChipsLight[btn] = new SolidColorBrush(ColorHelper.HexToColor(lightHex));
                }
            }

            var chips = isDark ? _swatchChipsDark : _swatchChipsLight;
            foreach (var pair in chips)
            {
                pair.Key.Background = pair.Value;
            }
        }

        private Dictionary<Button, FontIcon> _swatchChecks;

        // The selected swatch used to be marked by its border colour alone, which is the one thing
        // the accessibility checklist says must not carry information by itself. The checkmark is
        // the second, non-colour cue.
        private Dictionary<Button, FontIcon> SwatchChecks
        {
            get
            {
                if (_swatchChecks == null)
                {
                    _swatchChecks = new Dictionary<Button, FontIcon>()
                    {
                        { TintDefaultButton, TintDefaultCheck },
                        { TintBlueButton, TintBlueCheck },
                        { TintPurpleButton, TintPurpleCheck },
                        { TintGreenButton, TintGreenCheck },
                        { TintRedButton, TintRedCheck },
                        { TintSlateButton, TintSlateCheck },
                        { TintTealButton, TintTealCheck },
                        { TintBronzeButton, TintBronzeCheck },
                        { TintRoseButton, TintRoseCheck },
                        { TintOliveButton, TintOliveCheck },
                        { TintGraphiteButton, TintGraphiteCheck },
                        { TintCustomButton, TintCustomCheck },
                    };
                }
                return _swatchChecks;
            }
        }

        private void HideSwatchChecks()
        {
            foreach (var check in SwatchChecks.Values)
            {
                if (check != null) check.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowSwatchCheck(Button swatch)
        {
            FontIcon check;
            if (swatch == null || !SwatchChecks.TryGetValue(swatch, out check) || check == null) return;

            // Called after UpdateSwatchChipColors, so Background is the current theme's chip
            // colour and the check can be contrasted against what is actually painted.
            var brush = swatch.Background as SolidColorBrush;
            if (brush != null)
            {
                check.Foreground = new SolidColorBrush(ColorHelper.ContrastingForeground(brush.Color));
            }
            else
            {
                // The Default chip keeps the theme's own button background, whose paired
                // foreground already contrasts with it.
                check.ClearValue(IconElement.ForegroundProperty);
            }

            check.Visibility = Visibility.Visible;
        }

        private void UpdateTintSelection(string selectedTag)
        {
            selectedTag = string.IsNullOrEmpty(selectedTag) ? AppConstants.ThemeDefault : selectedTag;

            var isDark = IsEffectiveThemeDark();
            var restBrush = isDark ? s_restBrushDark : s_restBrushLight;
            UpdateSwatchChipColors(isDark);
            HideSwatchChecks();

            Button sel = null;
            foreach (var btn in TintPresetSwatches)
            {
                btn.BorderBrush = restBrush;
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(btn, BaseSwatchName(btn));
                var tag = btn.Tag?.ToString() ?? "";
                if (string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)) sel = btn;
            }

            TintCustomButton.BorderBrush = restBrush;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(TintCustomButton, BaseSwatchName(TintCustomButton));
            if (sel == null)
            {
                sel = TintCustomButton;
                ShowCustomSwatchColor(selectedTag);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    TintCustomButton,
                    LocalizedStrings.Format("TintCustomSwatchWithColorFormat",
                                            BaseSwatchName(TintCustomButton), selectedTag));
            }
            else if (TintCustomIcon.Visibility == Visibility.Collapsed)
            {
                var remembered = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (!string.IsNullOrEmpty(remembered))
                {
                    ShowCustomSwatchColor(remembered);
                }
            }

            if (sel != null)
            {
                sel.BorderBrush = isDark ? s_selectedBrushDark : s_selectedBrushLight;
                ShowSwatchCheck(sel);
                var selBaseName = Windows.UI.Xaml.Automation.AutomationProperties.GetName(sel);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    sel, LocalizedStrings.Format("TintSwatchSelectedSuffixFormat", selBaseName));
            }

            // Everything that repaints the tint swatches also changes what the accent row shows:
            // the theme its borders follow, and the tint that Match tint and the restart notice
            // depend on (MainPage.Accent.cs).
            UpdateAccentSelection();
        }

        private void ShowCustomSwatchColor(string hex)
        {
            try
            {
                Color parsed;
                if (!ColorHelper.TryHexToColor(hex, out parsed))
                {
                    // Leave the swatch showing its "pick a colour" glyph rather than painting it
                    // with something arbitrary.
                    Debug.WriteLine($"MainPage: custom swatch colour '{hex}' is not a colour");
                    return;
                }

                var c = IsEffectiveThemeDark() ? parsed : ColorHelper.LightenForLightTheme(parsed);
                TintCustomButton.Background = new SolidColorBrush(c);
                TintCustomIcon.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ShowCustomSwatchColor failed – {ex.Message}");
            }
        }

        private void AppearanceHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AppearanceHeader, AppearanceContent, AppearanceChevronRotation, AppConstants.SettingSettingsAppearanceExpanded);
        }

        private void TransparencyHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(TransparencyHeader, TransparencyContent, TransparencyChevronRotation, AppConstants.SettingSettingsTransparencyExpanded);
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            var radio = sender as RadioButton;
            if (radio != null)
            {
                ApplyTheme(radio.Tag.ToString());
            }
        }

        private void TintColorButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var tag = btn.Tag?.ToString() ?? "Default";
                ApplyTintColor(tag);
                UpdateTintSelection(tag);
            }
        }

        private async void CustomTintButton_Click(object sender, RoutedEventArgs e)
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            string previousTag = localSettings.Values[AppConstants.SettingAppTintColor]?.ToString()
                                 ?? AppConstants.ThemeDefault;

            await DialogService.RunExclusiveAsync(async () =>
            {
                try
                {
                    string seed = previousTag;
                    if (seed == AppConstants.ThemeDefault || ColorHelper.TryGetLightPreset(seed) != null)
                    {
                        seed = localSettings.Values[AppConstants.SettingAppCustomTintColor]?.ToString() ?? "#1E3A5F";
                    }

                    Color seedColor;
                    if (!ColorHelper.TryHexToColor(seed, out seedColor))
                    {
                        seedColor = ColorHelper.HexToColor("#1E3A5F");
                    }

                    ColorPicker picker = new ColorPicker()
                    {
                        IsAlphaEnabled = false,
                        IsHexInputVisible = true,
                        IsColorChannelTextInputVisible = true,
                        Color = seedColor
                    };

                    ContentDialog dialog = new ContentDialog()
                    {
                        Title = LocalizedStrings.Get("CustomTintDialogTitle"),
                        Content = picker,
                        PrimaryButtonText = LocalizedStrings.Get("CustomTintDialogApply"),
                        CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                        DefaultButton = ContentDialogButton.Primary
                    };
                    DialogService.ApplyXamlRoot(dialog, this);

                    TypedEventHandler<ColorPicker, ColorChangedEventArgs> previewHandler =
                        (s, args) => ApplyTintColorPreview(ColorHelper.ColorToHex(args.NewColor));
                    picker.ColorChanged += previewHandler;

                    var result = await dialog.ShowAsync();
                    picker.ColorChanged -= previewHandler;

                    if (result == ContentDialogResult.Primary)
                    {
                        var hex = ColorHelper.ColorToHex(picker.Color);
                        localSettings.Values[AppConstants.SettingAppCustomTintColor] = hex;
                        ApplyTintColor(hex);
                        UpdateTintSelection(hex);
                    }
                    else
                    {
                        ApplyTintColor(previousTag);
                        UpdateTintSelection(previousTag);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Custom tint dialog failed – {ex.Message}");
                    ApplyTintColor(previousTag);
                    UpdateTintSelection(previousTag);
                }
            });
        }

        private void ApplyTintColorPreview(string hex)
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                ApplyTintColor(hex);
            }
            finally
            {
                _loadingSettings = wasLoading;
            }
        }

        private void LoadAcrylicSettings(ApplicationDataContainer localSettings)
        {
            _bodyAcrylicOpacity = ReadOpacity(localSettings, AppConstants.SettingAppBodyAcrylicOpacity,
                                              AppConstants.DefaultBodyAcrylicOpacity);
            _paneAcrylicOpacity = ReadOpacity(localSettings, AppConstants.SettingAppPaneAcrylicOpacity,
                                              AppConstants.DefaultPaneAcrylicOpacity);

            _tintScope = AppConstants.TintScopeDefault;
            if (localSettings.Values.ContainsKey(AppConstants.SettingAppTintScope))
            {
                var saved = localSettings.Values[AppConstants.SettingAppTintScope]?.ToString();
                if (IsUsableTintScope(saved)) _tintScope = saved;
            }

            // Minimum before Value, and from the constant rather than the markup, so the floor has
            // one source of truth. Assigning Value first would let the control coerce it up to the
            // old minimum and silently disagree with the field the brushes are painted from.
            var minimum = AppConstants.MinimumAcrylicOpacity * 100.0;
            BodyAcrylicSlider.Minimum = minimum;
            PaneAcrylicSlider.Minimum = minimum;

            // _loadingSettings is set for the whole of LoadAppearanceSettings, so neither of these
            // reaches its handler - nothing is persisted and nothing is painted twice. The caller
            // paints once, through ApplyTintColor, after this returns.
            BodyAcrylicSlider.Value = _bodyAcrylicOpacity * 100.0;
            PaneAcrylicSlider.Value = _paneAcrylicOpacity * 100.0;

            switch (_tintScope)
            {
                case AppConstants.TintScopeSidebar: TintScopeSidebarRadio.IsChecked = true; break;
                case AppConstants.TintScopeBoth: TintScopeBothRadio.IsChecked = true; break;
                default: TintScopeContentRadio.IsChecked = true; break;
            }

            UpdateAcrylicValueLabels();
        }

        private static bool IsUsableTintScope(string scope)
        {
            return scope == AppConstants.TintScopeContent
                   || scope == AppConstants.TintScopeSidebar
                   || scope == AppConstants.TintScopeBoth;
        }

        /// <remarks>
        /// Convert.ToDouble rather than a (double) cast, for the reason every other read here uses
        /// Convert.ToBoolean: the cast throws on anything that is not a boxed double, and this runs
        /// on the path out of the MainPage constructor. Out-of-range values are clamped rather than
        /// rejected - TintOpacity is documented as 0 to 1.0 and coerces silently, so a stored 5
        /// would have shown a slider at 500%.
        /// </remarks>
        private static double ReadOpacity(ApplicationDataContainer localSettings, string key, double fallback)
        {
            try
            {
                if (!localSettings.Values.ContainsKey(key)) return fallback;

                var raw = localSettings.Values[key];
                if (raw == null) return fallback;

                var value = Convert.ToDouble(raw);
                if (double.IsNaN(value)) return fallback;

                return Math.Max(AppConstants.MinimumAcrylicOpacity, Math.Min(1.0, value));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not read {key} – {ex.Message}");
                return fallback;
            }
        }

        private static Windows.Globalization.NumberFormatting.PercentFormatter s_percentFormatter;

        /// <remarks>
        /// A formatter rather than a "{0}%" resource, for the reason dates go through
        /// DateTimeFormatter: percent placement and the space before the sign are not universal.
        /// Built lazily, so nothing activates a WinRT formatter at type load.
        /// </remarks>
        private static string FormatPercent(double fraction)
        {
            try
            {
                if (s_percentFormatter == null)
                {
                    s_percentFormatter = new Windows.Globalization.NumberFormatting.PercentFormatter()
                    {
                        FractionDigits = 0
                    };
                }

                return s_percentFormatter.Format(fraction);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: percent formatting failed – {ex.Message}");
                return string.Empty;
            }
        }

        private void UpdateAcrylicValueLabels()
        {
            BodyAcrylicValue.Text = FormatPercent(_bodyAcrylicOpacity);
            PaneAcrylicValue.Text = FormatPercent(_paneAcrylicOpacity);
            UpdateAcrylicLegibilityWarnings();
        }

        /// <summary>
        /// Shows the legibility caution naming whichever slider sits below the tint opacity the
        /// current theme needs.
        /// </summary>
        /// <remarks>
        /// A threshold, not a measurement: acrylic samples the desktop wallpaper, which the app
        /// cannot see, so ColorHelper.ContrastRatio has nothing to compare against. The two floors
        /// are the doc dump's (chunk_030, "Legibility considerations"): "In dark mode, tint opacity
        /// can be 70%, while light mode acrylic will meet contrast ratios at 50%." The floor moves
        /// with the theme, which is why RepaintThemeDependentChrome calls this as well.
        ///
        /// Opacity, never Visibility, and the text stays put when the warning is hidden: this runs
        /// on every tick of a slider drag, and a collapsing row would change this section's height
        /// under the cursor - which RepositionThemeTransition would then animate for every section
        /// below it. AccessibilityView is what actually hides it, so nothing reads text that is
        /// not on screen.
        /// </remarks>
        private void UpdateAcrylicLegibilityWarnings()
        {
            try
            {
                var isDark = IsEffectiveThemeDark();
                var floor = isDark ? AppConstants.AcrylicLegibilityFloorDark
                                   : AppConstants.AcrylicLegibilityFloorLight;

                var bodyLow = _bodyAcrylicOpacity < floor;
                var paneLow = _paneAcrylicOpacity < floor;

                // Set unconditionally, including when neither slider is low: an empty TextBlock
                // has no height, so leaving it blank would collapse the row this is here to
                // reserve and reintroduce the jump on the first crossing. The Content wording is
                // the placeholder - it is invisible and out of the automation tree.
                string key;
                if (bodyLow && paneLow) key = "AcrylicLegibilityWarningBothFormat";
                else if (paneLow) key = "AcrylicLegibilityWarningSidebarFormat";
                else key = "AcrylicLegibilityWarningContentFormat";

                var show = bodyLow || paneLow;

                // This runs on every tick of a slider drag, and LocalizedStrings.Get is not
                // memoized - it opens the resource loader per call - so repainting identical text
                // hundreds of times per drag is a real cost for no change on screen.
                if (show == _warningShown
                    && floor == _warningFloor
                    && string.Equals(key, _warningKey, StringComparison.Ordinal))
                {
                    return;
                }

                _warningShown = show;
                _warningFloor = floor;
                _warningKey = key;

                AcrylicWarningText.Text = LocalizedStrings.Format(key, FormatPercent(floor));
                AcrylicWarning.Opacity = show ? 1 : 0;

                // Set on the TextBlock, not just its parent Grid: AccessibilityView is documented
                // per element and does not prune a subtree (that is why frameworks that want the
                // cascading behaviour, like MAUI, ship a separate ExcludedWithChildren). Raw on
                // the Grid alone left the child TextBlock in the content view, so a screen reader
                // could still read a warning nobody can see. The FontIcon is already Raw in markup.
                var view = show ? Windows.UI.Xaml.Automation.Peers.AccessibilityView.Content
                                : Windows.UI.Xaml.Automation.Peers.AccessibilityView.Raw;
                Windows.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(AcrylicWarning, view);
                Windows.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(AcrylicWarningText, view);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateAcrylicLegibilityWarnings failed – {ex.Message}");
            }
        }

        private void BodyAcrylicSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                _bodyAcrylicOpacity = e.NewValue / 100.0;
                ApplySurfaceBrushes(_tintTag);
                UpdateAcrylicValueLabels();
                QueueAcrylicPersist();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: BodyAcrylicSlider_ValueChanged failed – {ex.Message}");
            }
        }

        private void PaneAcrylicSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                _paneAcrylicOpacity = e.NewValue / 100.0;
                ApplySurfaceBrushes(_tintTag);
                UpdateAcrylicValueLabels();
                QueueAcrylicPersist();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: PaneAcrylicSlider_ValueChanged failed – {ex.Message}");
            }
        }

        /// <summary>
        /// Saves both opacities once a drag settles.
        /// </summary>
        /// <remarks>
        /// The brushes are repainted on every ValueChanged - a dependency property set on one
        /// reused brush, which is cheap - but a drag raises hundreds of them and each write to
        /// LocalSettings hits disk. One debouncer covers both sliders and its flush writes both
        /// values: dragging the second slider cancels the first one's pending flush, so a
        /// per-slider payload would have dropped that value on the floor.
        /// </remarks>
        private async void QueueAcrylicPersist()
        {
            try
            {
                var token = _acrylicPersistDebouncer.Restart();

                // The token is deliberately NOT passed to Task.Delay. Handing it over makes the
                // delay throw TaskCanceledException the moment the next tick calls Restart, and a
                // single slider drag raises hundreds of ticks - hundreds of first-chance exceptions
                // in the debugger, for a cancellation that is the normal case rather than a fault.
                // The check below is what actually stops the stale flush, and it is safe on a token
                // whose source Restart has already disposed: IsCancellationRequested is one of the
                // few members that does not throw after Dispose (unlike Token, Cancel, CancelAfter).
                // The cost is a timer that runs to completion and then does nothing.
                await Task.Delay(AppConstants.AcrylicPersistDebounceMilliseconds);
                if (token.IsCancellationRequested) return;

                var values = ApplicationData.Current.LocalSettings.Values;
                values[AppConstants.SettingAppBodyAcrylicOpacity] = _bodyAcrylicOpacity;
                values[AppConstants.SettingAppPaneAcrylicOpacity] = _paneAcrylicOpacity;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not save the acrylic opacities – {ex.Message}");
            }
        }

        /// <summary>
        /// Writes both opacities now and drops any pending debounced write.
        /// </summary>
        /// <remarks>
        /// The debounce is right for the middle of a drag and wrong at the end of one. Closing or
        /// terminating the app inside the 400ms window left the continuation un-run and the value
        /// lost, which is exactly what this codebase legislates against: state that survives
        /// termination is written at the moment it changes, not batched. Releasing the thumb and
        /// leaving the slider are both "the moment it changes", so they write straight through.
        /// </remarks>
        private void FlushAcrylicPersist()
        {
            try
            {
                _acrylicPersistDebouncer.Cancel();

                var values = ApplicationData.Current.LocalSettings.Values;
                values[AppConstants.SettingAppBodyAcrylicOpacity] = _bodyAcrylicOpacity;
                values[AppConstants.SettingAppPaneAcrylicOpacity] = _paneAcrylicOpacity;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not flush the acrylic opacities – {ex.Message}");
            }
        }

        // Pointer capture ends a mouse or touch drag; LostFocus covers the keyboard, where arrow
        // keys change the value and no pointer is involved. Both are cheap enough to run
        // unconditionally - the write is two values, and Cancel makes the pending one a no-op.
        private void AcrylicSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_loadingSettings) return;
            FlushAcrylicPersist();
        }

        private void AcrylicSlider_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            FlushAcrylicPersist();
        }

        private void TintScopeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                var radio = sender as RadioButton;
                if (radio == null) return;

                var scope = radio.Tag?.ToString();
                if (!IsUsableTintScope(scope)) return;

                _tintScope = scope;

                // Saved at once, not debounced: this is a discrete choice, not a drag.
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintScope] = scope;
                ApplySurfaceBrushes(_tintTag);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: TintScopeRadio_Checked failed – {ex.Message}");
            }
        }
    }
}
