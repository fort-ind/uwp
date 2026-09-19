using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public sealed partial class SettingsPage : Page
    {
        private string _warningKey;
        private bool _warningIsDark;
        private bool _warningShown;

        private void LoadSettingsControls()
        {
            _loadingSettings = true;
            try
            {
                switch (AppearanceService.Theme)
                {
                    case AppConstants.ThemeLight: ThemeLightRadio.IsChecked = true; break;
                    case AppConstants.ThemeDark: ThemeDarkRadio.IsChecked = true; break;
                    default: ThemeSystemRadio.IsChecked = true; break;
                }

                TintCustomButton.ClearValue(Control.BackgroundProperty);
                TintCustomIcon.Visibility = Visibility.Visible;

                LoadAcrylicControls();

                UpdateTintSelection(AppearanceService.TintTag);

                var rememberedCustom = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (TintCustomIcon.Visibility == Visibility.Visible && !string.IsNullOrEmpty(rememberedCustom))
                {
                    ShowCustomSwatchColor(rememberedCustom);
                }

                TileBadgeToggle.IsOn = LiveTileService.BadgeEnabled;
                AutoUpdateCheckToggle.IsOn = UpdateService.AutomaticChecksEnabled;

                RestoreSettingsPanelStates();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: LoadSettingsControls failed - {ex.Message}");
            }
            finally
            {
                _loadingSettings = false;
            }
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
            foreach (var check in SwatchChecks.Values.Where(c => c != null))
            {
                check.Visibility = Visibility.Collapsed;
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

            var isDark = AppearanceService.IsEffectiveThemeDark();
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
                    Debug.WriteLine($"SettingsPage: custom swatch colour '{hex}' is not a colour");
                    return;
                }

                var c = AppearanceService.IsEffectiveThemeDark() ? parsed : ColorHelper.LightenForLightTheme(parsed);
                TintCustomButton.Background = new SolidColorBrush(c);
                TintCustomIcon.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: ShowCustomSwatchColor failed - {ex.Message}");
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
                AppearanceService.SetTheme(radio.Tag.ToString());
            }
        }

        private void TintColorButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var tag = btn.Tag?.ToString() ?? AppConstants.ThemeDefault;
                AppearanceService.SetTint(tag, true);
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
                Debug.WriteLine($"SettingsPage: Custom tint flow failed - {ex.Message}");
            }
        }

        private async Task ShowCustomTintDialogAsync()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            string previousTag = AppearanceService.TintTag;

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
                        (s, args) => AppearanceService.SetTint(ColorHelper.ColorToHex(args.NewColor), false);
                    picker.ColorChanged += previewHandler;

                    var result = await dialog.ShowAsync();
                    picker.ColorChanged -= previewHandler;

                    if (result == ContentDialogResult.Primary)
                    {
                        var hex = ColorHelper.ColorToHex(picker.Color);
                        localSettings.Values[AppConstants.SettingAppCustomTintColor] = hex;
                        AppearanceService.SetTint(hex, true);
                        UpdateTintSelection(hex);
                    }
                    else
                    {
                        AppearanceService.SetTint(previousTag, true);
                        UpdateTintSelection(previousTag);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SettingsPage: Custom tint dialog failed - {ex.Message}");
                    AppearanceService.SetTint(previousTag, true);
                    UpdateTintSelection(previousTag);
                }
            });
        }

        private void LoadAcrylicControls()
        {
            var minimum = AppConstants.MinimumAcrylicOpacity * 100.0;
            BodyAcrylicSlider.Minimum = minimum;
            PaneAcrylicSlider.Minimum = minimum;

            BodyAcrylicSlider.Value = AppearanceService.BodyAcrylicOpacity * 100.0;
            PaneAcrylicSlider.Value = AppearanceService.PaneAcrylicOpacity * 100.0;

            switch (AppearanceService.TintScope)
            {
                case AppConstants.TintScopeSidebar: TintScopeSidebarRadio.IsChecked = true; break;
                case AppConstants.TintScopeBoth: TintScopeBothRadio.IsChecked = true; break;
                default: TintScopeContentRadio.IsChecked = true; break;
            }

            UpdateAcrylicValueLabels();
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
                Debug.WriteLine($"SettingsPage: percent formatting failed - {ex.Message}");
                return string.Empty;
            }
        }

        private void UpdateAcrylicValueLabels()
        {
            BodyAcrylicValue.Text = FormatPercent(AppearanceService.BodyAcrylicOpacity);
            PaneAcrylicValue.Text = FormatPercent(AppearanceService.PaneAcrylicOpacity);
            UpdateAcrylicLegibilityWarnings();
        }

        private void UpdateAcrylicLegibilityWarnings()
        {
            try
            {
                var isDark = AppearanceService.IsEffectiveThemeDark();
                var floor = isDark ? AppConstants.AcrylicLegibilityFloorDark
                                   : AppConstants.AcrylicLegibilityFloorLight;

                var bodyLow = AppearanceService.BodyAcrylicOpacity < floor;
                var paneLow = AppearanceService.PaneAcrylicOpacity < floor;

                string key;
                if (bodyLow && paneLow) key = "AcrylicLegibilityWarningBothFormat";
                else if (paneLow) key = "AcrylicLegibilityWarningSidebarFormat";
                else key = "AcrylicLegibilityWarningContentFormat";

                var show = bodyLow || paneLow;

                if (show == _warningShown
                    && isDark == _warningIsDark
                    && string.Equals(key, _warningKey, StringComparison.Ordinal))
                {
                    return;
                }

                _warningShown = show;
                _warningIsDark = isDark;
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
                Debug.WriteLine($"SettingsPage: UpdateAcrylicLegibilityWarnings failed - {ex.Message}");
            }
        }

        private void BodyAcrylicSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                AppearanceService.SetAcrylicOpacities(e.NewValue / 100.0, AppearanceService.PaneAcrylicOpacity);
                UpdateAcrylicValueLabels();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: BodyAcrylicSlider_ValueChanged failed - {ex.Message}");
            }
        }

        private void PaneAcrylicSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                AppearanceService.SetAcrylicOpacities(AppearanceService.BodyAcrylicOpacity, e.NewValue / 100.0);
                UpdateAcrylicValueLabels();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: PaneAcrylicSlider_ValueChanged failed - {ex.Message}");
            }
        }

        private void AcrylicSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_loadingSettings) return;
            AppearanceService.FlushAcrylicPersist();
        }

        private void AcrylicSlider_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            AppearanceService.FlushAcrylicPersist();
        }

        private void TintScopeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                var radio = sender as RadioButton;
                if (radio == null) return;

                AppearanceService.SetTintScope(radio.Tag?.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: TintScopeRadio_Checked failed - {ex.Message}");
            }
        }
    }
}
