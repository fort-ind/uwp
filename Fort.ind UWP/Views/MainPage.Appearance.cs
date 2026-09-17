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

        private string _themePaintKey;

        private void RepaintThemeDependentChrome()
        {
            var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
            if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;

            var key = (IsEffectiveThemeDark() ? "Dark|" : "Light|") + savedTint;
            if (string.Equals(key, _themePaintKey, StringComparison.Ordinal)) return;

            UpdateTitleBarColors();
            ApplyTintColor(savedTint);
            UpdateTintSelection(savedTint);

            UpdateAcrylicLegibilityWarnings();

            _themePaintKey = key;
        }

        private AcrylicBrush _surfaceBrush;

        private static readonly Color s_surfaceTintDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_surfaceTintLight = Colors.White;
        private static readonly Color s_surfaceFallbackLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private static readonly Color s_paneTintDark = Color.FromArgb(255, 0x1F, 0x1F, 0x1F);
        private static readonly Color s_paneTintLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private double _bodyAcrylicOpacity = AppConstants.DefaultBodyAcrylicOpacity;
        private double _paneAcrylicOpacity = AppConstants.DefaultPaneAcrylicOpacity;
        private string _tintScope = AppConstants.TintScopeDefault;

        private string _tintTag = AppConstants.ThemeDefault;

        private string _warningKey;
        private double _warningFloor = -1;
        private bool _warningShown;

        private readonly Debouncer _acrylicPersistDebouncer = new Debouncer();

        private void ApplyTintColor(string colorTag)
        {
            _themePaintKey = null;

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

        private void ApplySurfaceBrushes(string colorTag)
        {
            try
            {
                var isDark = IsEffectiveThemeDark();
                var isTinted = !(string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault);

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

        private static readonly string[] s_paneBrushKeys =
        {
            "NavigationViewExpandedPaneBackground",
            "NavigationViewDefaultPaneBackground",
        };

        private static List<KeyValuePair<bool, AcrylicBrush>> s_paneBrushes;

        private static List<KeyValuePair<bool, AcrylicBrush>> PaneAcrylicBrushes()
        {
            if (s_paneBrushes != null) return s_paneBrushes;

            var found = new List<KeyValuePair<bool, AcrylicBrush>>(s_paneBrushKeys.Length * 2);

            try
            {
                var themes = AccentColorService.FindOverrideDictionary(Application.Current.Resources).ThemeDictionaries;

                CollectPaneBrushes(themes, "Default", true, found);
                CollectPaneBrushes(themes, AppConstants.ThemeLight, false, found);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not reach the pane acrylic brushes – {ex.Message}");
            }

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

        private static bool IsUsableTintTag(string colorTag)
        {
            if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault) return true;

            Color ignored;
            return ColorHelper.TryHexToColor(colorTag, out ignored);
        }

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

            var brush = swatch.Background as SolidColorBrush;
            if (brush != null && swatch.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue)
            {
                check.Foreground = new SolidColorBrush(ColorHelper.ContrastingForeground(brush.Color));
            }
            else
            {
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

            UpdateAccentSelection();
        }

        private void ShowCustomSwatchColor(string hex)
        {
            try
            {
                Color parsed;
                if (!ColorHelper.TryHexToColor(hex, out parsed))
                {
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
            try
            {
                await ShowCustomTintDialogAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Custom tint flow failed – {ex.Message}");
            }
        }

        private async Task ShowCustomTintDialogAsync()
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

            var minimum = AppConstants.MinimumAcrylicOpacity * 100.0;
            BodyAcrylicSlider.Minimum = minimum;
            PaneAcrylicSlider.Minimum = minimum;

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

        private void UpdateAcrylicLegibilityWarnings()
        {
            try
            {
                var isDark = IsEffectiveThemeDark();
                var floor = isDark ? AppConstants.AcrylicLegibilityFloorDark
                                   : AppConstants.AcrylicLegibilityFloorLight;

                var bodyLow = _bodyAcrylicOpacity < floor;
                var paneLow = _paneAcrylicOpacity < floor;

                string key;
                if (bodyLow && paneLow) key = "AcrylicLegibilityWarningBothFormat";
                else if (paneLow) key = "AcrylicLegibilityWarningSidebarFormat";
                else key = "AcrylicLegibilityWarningContentFormat";

                var show = bodyLow || paneLow;

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

        private async void QueueAcrylicPersist()
        {
            try
            {
                var token = _acrylicPersistDebouncer.Restart();

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
