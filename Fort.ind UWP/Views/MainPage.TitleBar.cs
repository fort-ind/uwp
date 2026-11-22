using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private List<FrameworkElement> _navViewTitleBarPads;

        private bool _titleBarHidden;

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

            ApplyNavViewTitleBarPadding(coreTitleBar);
        }

        private void ApplyNavViewTitleBarPadding(CoreApplicationViewTitleBar coreTitleBar)
        {
            if (coreTitleBar == null || !_navViewInitialized) return;

            if (_navViewTitleBarPads == null) _navViewTitleBarPads = FindNavViewTitleBarPads();

            var top = _titleBarHidden ? 0 : coreTitleBar.Height;
            var margin = new Thickness(0, top, 0, 0);
            foreach (var pad in _navViewTitleBarPads)
            {
                if (!pad.Margin.Equals(margin)) pad.Margin = margin;
            }
        }

        private List<FrameworkElement> FindNavViewTitleBarPads()
        {
            var pads = new List<FrameworkElement>();

            if (!ApiContracts.Has1809)
            {
                AddNavViewPart(pads, FindNavViewPart("TogglePaneTopPadding"), "TogglePaneTopPadding");
                AddNavViewPart(pads, FindNavViewPart("ContentPaneTopPadding"), "ContentPaneTopPadding");
            }

            if (FindNavViewPart("ContentTopPadding") == null) AddNavViewPart(pads, FindNavViewPart("ContentGrid"), "ContentGrid");

            return pads;
        }

        private FrameworkElement FindNavViewPart(string name)
        {
            if (VisualTreeHelper.GetChildrenCount(NavView) == 0) return null;

            var templateRoot = VisualTreeHelper.GetChild(NavView, 0) as FrameworkElement;
            return templateRoot == null ? null : templateRoot.FindName(name) as FrameworkElement;
        }

        private static void AddNavViewPart(List<FrameworkElement> pads, FrameworkElement part, string name)
        {
            if (part != null)
            {
                pads.Add(part);
            }
            else
            {
                AppLog.Warning("MainPage: the NavigationView template has no " + name + "; its title bar padding is not applied");
            }
        }

        private void OnTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                ApplyTitleBarLayoutMetrics(sender);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Failed to apply title bar layout metrics", ex);
            }
        }

        private void OnTitleBarIsVisibleChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                var visibility = sender.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                AppTitleBar.Visibility = visibility;
                TitleBarBackButton.Visibility = visibility;

                _titleBarHidden = !sender.IsVisible;
                ApplyNavViewTitleBarPadding(sender);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Failed to follow title bar visibility", ex);
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
                    AppLog.Error("MainPage: title bar repaint after high contrast change failed", ex);
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

                SocialTileService.ShowNewsTile(userRequested);
                SocialFeedTileService.RefreshInBackground();

                if (userRequested)
                {
                    SocialNotificationService.RefreshTileInBackground();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: UpdateLiveTile failed", ex);
            }
        }
    }
}
