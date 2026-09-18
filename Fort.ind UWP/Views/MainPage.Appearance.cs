using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void ApplySavedAppearance()
        {
            try
            {
                AppearanceService.Initialize();
                RootGrid.Background = AppearanceService.SurfaceBrush;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not apply the saved appearance - {ex.Message}");
            }
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                AppearanceService.RepaintThemeDependentChrome();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: OnActualThemeChanged failed - {ex.Message}");
            }
        }

        private void OnAppearanceChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateTitleBarColors();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: title bar repaint failed - {ex.Message}");
            }
        }
    }
}
