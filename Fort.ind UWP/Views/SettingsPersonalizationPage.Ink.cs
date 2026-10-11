using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsPage
    {
        private void LoadInkControls()
        {
            InkMouseToggle.IsOn = InkSettingsService.InkWithMouse;
            InkTouchPanToggle.IsOn = InkSettingsService.TouchPansAfterPen;
            var mode = InkSettingsService.PenTextMode.ToString();
            InkPenTextModeCombo.SelectedItem = InkPenTextModeCombo.Items
                                                                  .OfType<ComboBoxItem>()
                                                                  .FirstOrDefault(item => string.Equals(item.Tag as string, mode, StringComparison.Ordinal));
            InkHandwritingFontToggle.IsOn = InkSettingsService.HandwritingFont;
            UpdateHandwritingFontEnabled();
        }

        private PenTextMode SelectedPenTextMode()
        {
            PenTextMode mode;
            var tag = (InkPenTextModeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            return tag != null && Enum.TryParse(tag, out mode) ? mode : PenTextMode.WriteOnBox;
        }

        private void UpdateHandwritingFontEnabled()
        {
            InkHandwritingFontToggle.IsEnabled = SelectedPenTextMode() != PenTextMode.Off;
        }

        private void InkHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(InkHeader, InkContent, InkChevronRotation, AppConstants.SettingSettingsInkExpanded);
        }

        private void InkMouseToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            InkSettingsService.InkWithMouse = InkMouseToggle.IsOn;
        }

        private void InkTouchPanToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            InkSettingsService.TouchPansAfterPen = InkTouchPanToggle.IsOn;
        }

        private void InkPenTextModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateHandwritingFontEnabled();

            if (_loadingSettings) return;
            InkSettingsService.PenTextMode = SelectedPenTextMode();
        }

        private void InkHandwritingFontToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            InkSettingsService.HandwritingFont = InkHandwritingFontToggle.IsOn;
        }
    }
}
