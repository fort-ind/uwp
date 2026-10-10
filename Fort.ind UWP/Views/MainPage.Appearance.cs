using System;
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
                AppLog.Error("MainPage: could not apply the saved appearance", ex);
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
                AppLog.Error("MainPage: OnActualThemeChanged failed", ex);
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
                AppLog.Error("MainPage: title bar repaint failed", ex);
            }
        }
    }
}
