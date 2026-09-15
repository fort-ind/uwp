using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private const string DefaultCustomAccentSeed = "#0078D7";

        private Button[] _accentTagSwatches;

        /// <summary>
        /// Every accent swatch whose Tag is the choice it stands for - all but Custom.
        /// </summary>
        private Button[] AccentTagSwatches
        {
            get
            {
                if (_accentTagSwatches == null)
                {
                    _accentTagSwatches = new Button[] { AccentSystemButton, AccentMatchTintButton,
                                                        AccentBlueButton, AccentIrisButton, AccentOrchidButton, AccentRoseButton,
                                                        AccentRedButton, AccentOrangeButton, AccentGreenButton, AccentSeafoamButton,
                                                        AccentGrayButton };
                }
                return _accentTagSwatches;
            }
        }

        private Dictionary<Button, FontIcon> _accentSwatchChecks;

        private Dictionary<Button, FontIcon> AccentSwatchChecks
        {
            get
            {
                if (_accentSwatchChecks == null)
                {
                    _accentSwatchChecks = new Dictionary<Button, FontIcon>()
                    {
                        { AccentSystemButton, AccentSystemCheck },
                        { AccentMatchTintButton, AccentMatchTintCheck },
                        { AccentBlueButton, AccentBlueCheck },
                        { AccentIrisButton, AccentIrisCheck },
                        { AccentOrchidButton, AccentOrchidCheck },
                        { AccentRoseButton, AccentRoseCheck },
                        { AccentRedButton, AccentRedCheck },
                        { AccentOrangeButton, AccentOrangeCheck },
                        { AccentGreenButton, AccentGreenCheck },
                        { AccentSeafoamButton, AccentSeafoamCheck },
                        { AccentGrayButton, AccentGrayCheck },
                        { AccentCustomButton, AccentCustomCheck },
                    };
                }
                return _accentSwatchChecks;
            }
        }

        /// <summary>
        /// Border, checkmark and automation name for the saved accent, the colours of the two
        /// swatches that depend on other settings, and the restart notice.
        /// </summary>
        /// <remarks>
        /// Called at the end of UpdateTintSelection rather than from each of its callers: all of
        /// them - settings load, theme repaint, a tint pick - change something this reads. The
        /// rest/selected borders follow the theme, and the Match tint swatch and the restart
        /// notice both follow the tint.
        /// </remarks>
        private void UpdateAccentSelection()
        {
            try
            {
                var selectedTag = AccentColorService.SavedAccentTag;
                var isDark = IsEffectiveThemeDark();
                var restBrush = isDark ? s_restBrushDark : s_restBrushLight;

                PaintMatchTintSwatch();
                PaintCustomAccentSwatch(ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppCustomAccentColor] as string);

                foreach (var check in AccentSwatchChecks.Values)
                {
                    check.Visibility = Visibility.Collapsed;
                }

                Button sel = null;
                foreach (var btn in AccentTagSwatches)
                {
                    btn.BorderBrush = restBrush;
                    AutomationProperties.SetName(btn, BaseSwatchName(btn));
                    if (string.Equals(btn.Tag?.ToString(), selectedTag, StringComparison.OrdinalIgnoreCase)) sel = btn;
                }

                AccentCustomButton.BorderBrush = restBrush;
                AutomationProperties.SetName(AccentCustomButton, BaseSwatchName(AccentCustomButton));
                if (sel == null)
                {
                    // SavedAccentTag only ever returns Default, MatchTint or a parseable colour, so
                    // anything no tagged swatch claims is a custom colour.
                    sel = AccentCustomButton;
                    PaintCustomAccentSwatch(selectedTag);
                    AutomationProperties.SetName(
                        AccentCustomButton,
                        LocalizedStrings.Format("TintCustomSwatchWithColorFormat",
                                                BaseSwatchName(AccentCustomButton), selectedTag));
                }

                sel.BorderBrush = isDark ? s_selectedBrushDark : s_selectedBrushLight;
                ShowAccentSwatchCheck(sel);
                AutomationProperties.SetName(
                    sel, LocalizedStrings.Format("TintSwatchSelectedSuffixFormat", AutomationProperties.GetName(sel)));

                UpdateAccentRestartNotice();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateAccentSelection failed - {ex.Message}");
            }
        }

        private void ShowAccentSwatchCheck(Button swatch)
        {
            FontIcon check;
            if (!AccentSwatchChecks.TryGetValue(swatch, out check) || check == null) return;

            var brush = swatch.Background as SolidColorBrush;
            if (brush != null && swatch.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue)
            {
                check.Foreground = new SolidColorBrush(ColorHelper.ContrastingForeground(brush.Color));
            }
            else
            {
                // An unpainted swatch keeps the theme's button background and its paired foreground.
                check.ClearValue(IconElement.ForegroundProperty);
            }

            check.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Paints the Match tint swatch with the accent the current tint would give, so the user
        /// sees what they are choosing; unpainted when the tint is the default surface.
        /// </summary>
        private void PaintMatchTintSwatch()
        {
            var tint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();

            Color accent;
            var hex = ColorHelper.AccentForTint(tint);
            if (hex != null && ColorHelper.TryHexToColor(hex, out accent))
            {
                AccentMatchTintButton.Background = new SolidColorBrush(accent);
                AccentMatchTintIcon.Foreground = new SolidColorBrush(ColorHelper.ContrastingForeground(accent));
            }
            else
            {
                AccentMatchTintButton.ClearValue(Control.BackgroundProperty);
                AccentMatchTintIcon.ClearValue(IconElement.ForegroundProperty);
            }
        }

        private void PaintCustomAccentSwatch(string hex)
        {
            Color parsed;
            if (!ColorHelper.TryHexToColor(hex, out parsed))
            {
                AccentCustomButton.ClearValue(Control.BackgroundProperty);
                AccentCustomIcon.Visibility = Visibility.Visible;
                return;
            }

            AccentCustomButton.Background = new SolidColorBrush(parsed);
            AccentCustomIcon.Visibility = Visibility.Collapsed;
        }

        private void UpdateAccentRestartNotice()
        {
            AccentRestartNotice.Visibility = AccentColorService.IsRestartPending ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AccentColorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Button;
                if (btn == null) return;

                AccentColorService.SavedAccentTag = btn.Tag?.ToString() ?? AppConstants.ThemeDefault;
                UpdateAccentSelection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: AccentColorButton_Click failed - {ex.Message}");
            }
        }

        private async void CustomAccentButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await DialogService.RunExclusiveAsync(async () =>
                {
                    var contentTemplate = Resources["CustomAccentDialogContentTemplate"] as DataTemplate;
                    var content = contentTemplate?.LoadContent() as FrameworkElement;
                    if (content == null) return;

                    // x:Name inside a DataTemplate resolves against the stamped copy, not the page.
                    var picker = content.FindName("AccentPicker") as ColorPicker;
                    var warning = content.FindName("AccentContrastWarning") as FrameworkElement;
                    var warningText = content.FindName("AccentContrastWarningText") as TextBlock;
                    if (picker == null || warning == null || warningText == null) return;

                    var localSettings = ApplicationData.Current.LocalSettings;
                    Color seed;
                    if (!ColorHelper.TryHexToColor(localSettings.Values[AppConstants.SettingAppCustomAccentColor] as string, out seed)
                        && !ColorHelper.TryHexToColor(AccentColorService.ResolveSavedAccentHex(), out seed))
                    {
                        seed = ColorHelper.HexToColor(DefaultCustomAccentSeed);
                    }
                    picker.Color = seed;
                    UpdateContrastWarning(seed, warning, warningText, false);

                    var dialog = new ContentDialog()
                    {
                        Title = LocalizedStrings.Get("CustomAccentDialogTitle"),
                        Content = content,
                        PrimaryButtonText = LocalizedStrings.Get("CustomAccentDialogApply"),
                        CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                        DefaultButton = ContentDialogButton.Primary
                    };
                    DialogService.ApplyXamlRoot(dialog, this);

                    TypedEventHandler<ColorPicker, ColorChangedEventArgs> contrastHandler =
                        (s, args) => UpdateContrastWarning(args.NewColor, warning, warningText, true);
                    picker.ColorChanged += contrastHandler;

                    var result = await dialog.ShowAsync();
                    picker.ColorChanged -= contrastHandler;

                    if (result != ContentDialogResult.Primary) return;

                    var hex = ColorHelper.ColorToHex(picker.Color);
                    localSettings.Values[AppConstants.SettingAppCustomAccentColor] = hex;
                    AccentColorService.SavedAccentTag = hex;
                    UpdateAccentSelection();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Custom accent dialog failed - {ex.Message}");
            }
        }

        /// <summary>
        /// Shows, rewords or hides the picker's contrast warning. Only announced when its text
        /// actually changes: ColorChanged fires continuously while the user drags.
        /// </summary>
        private static void UpdateContrastWarning(Color color, FrameworkElement warning, TextBlock warningText, bool announce)
        {
            bool failsDark, failsLight;
            AccentColorService.CheckContrast(color, out failsDark, out failsLight);

            string message = null;
            if (failsDark && failsLight) message = LocalizedStrings.Get("AccentContrastWarningBoth");
            else if (failsDark) message = LocalizedStrings.Get("AccentContrastWarningDark");
            else if (failsLight) message = LocalizedStrings.Get("AccentContrastWarningLight");

            var visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            var changed = warning.Visibility != visibility
                          || (message != null && !string.Equals(warningText.Text, message, StringComparison.Ordinal));

            warning.Visibility = visibility;
            if (message != null) warningText.Text = message;

            if (announce && changed && message != null)
            {
                AutomationHelper.AnnounceStatus(warningText, message, "AccentContrastWarning");
            }
        }

        private async void AccentRestartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RequestAppRestartAsync();

                // RequestRestartAsync only returns when the restart did not happen. The user asked
                // for it, so unlike the reset flow this cannot just log and carry on.
                await DialogService.ShowMessageAsync(this,
                                                     LocalizedStrings.Get("AccentRestartFailedDialogTitle"),
                                                     LocalizedStrings.Get("AccentRestartFailedDialogBody"),
                                                     LocalizedStrings.Get("DialogOk"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: AccentRestartButton_Click failed - {ex.Message}");
            }
        }
    }
}
