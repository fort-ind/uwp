using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
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
                TitleBarBackButton.Height = coreTitleBar.Height;
            }

            TitleBarLeftInset.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
            TitleBarRightInset.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
            TitleBarBackButton.Margin = new Thickness(coreTitleBar.SystemOverlayLeftInset, 0, 0, 0);
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

        private void OnTitleBarIsVisibleChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                var visibility = sender.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                AppTitleBar.Visibility = visibility;
                TitleBarBackButton.Visibility = visibility;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to follow title bar visibility - {ex.Message}");
            }
        }

        private static readonly AccessibilitySettings s_accessibilitySettings = new AccessibilitySettings();

        private void OnHighContrastChanged(AccessibilitySettings sender, object args)
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    UpdateTitleBarColors();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: title bar repaint after high contrast change failed - {ex.Message}");
                }
            });
        }

        private void UpdateTitleBarColors()
        {
            CaptionButtonColors.Apply(ApplicationView.GetForCurrentView().TitleBar,
                                      s_accessibilitySettings.HighContrast,
                                      AppearanceService.IsEffectiveThemeDark());
        }

        internal void RefreshLiveTile()
        {
            UpdateLiveTile(true);
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
