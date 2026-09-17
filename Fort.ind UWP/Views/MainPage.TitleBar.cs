using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void SetupTitleBar()
        {
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            Window.Current.SetTitleBar(AppTitleBar);

            ApplyTitleBarLayoutMetrics(coreTitleBar);

            UpdateTitleBarColors();
        }

        private void ApplyTitleBarLayoutMetrics(CoreApplicationViewTitleBar coreTitleBar)
        {
            if (coreTitleBar == null) return;

            if (coreTitleBar.Height > 0)
            {
                AppTitleBar.Height = coreTitleBar.Height;
            }

            TitleBarLeftInset.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
            TitleBarRightInset.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
        }

        private void OnTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                ApplyTitleBarLayoutMetrics(sender);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to apply title bar layout metrics - {ex.Message}");
            }
        }

        private static readonly AccessibilitySettings s_accessibilitySettings = new AccessibilitySettings();

        private void UpdateTitleBarColors()
        {
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;

            var isDark = IsEffectiveThemeDark();

            var fgColor = isDark ? Colors.White : Colors.Black;

            var inactiveFg = isDark ? Color.FromArgb(255, 0x99, 0x99, 0x99) : Color.FromArgb(255, 0x66, 0x66, 0x66);
            var hoverBg = isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            var pressedBg = isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0);

            var hoverFg = fgColor;
            var pressedFg = fgColor;

            Color accent;
            if (AccentColorService.ActiveAccentHex != null
                && !s_accessibilitySettings.HighContrast
                && ColorHelper.TryHexToColor(AccentColorService.ActiveAccentHex, out accent))
            {
                hoverBg = accent;
                pressedBg = ColorHelper.AccentShade(accent, -1);
                hoverFg = ColorHelper.ContrastingForeground(hoverBg);
                pressedFg = ColorHelper.ContrastingForeground(pressedBg);
            }

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedBackgroundColor = pressedBg;

            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = hoverFg;
            titleBar.ButtonPressedForegroundColor = pressedFg;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
        }

        private void UpdateLiveTile(bool userRequested = false)
        {
            try
            {
                if (userRequested)
                {
                    LiveTileService.TileCleared = false;
                }
                else if (LiveTileService.TileCleared)
                {
                    return;
                }

                List<NewsItem> newsItems = new List<NewsItem>()
                {
                    new NewsItem(LocalizedStrings.Get("TileNewsWhatsNewTitle"),
                                 LocalizedStrings.Get("TileNewsWhatsNewBody"),
                                 "welcome"),
                    new NewsItem(LocalizedStrings.Get("TileNewsGetStartedTitle"),
                                 LocalizedStrings.Get("TileNewsGetStartedBody"),
                                 "features")
                };

                LiveTileService.UpdateTileWithMultipleNews(newsItems);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateLiveTile failed – {ex.Message}");
            }
        }
    }
}
