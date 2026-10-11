using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsNotificationsPage : Page, IShellContentPage, ISettingsSubpage
    {
        private bool _loadingSettings = true;

        private string _pendingRevealSection;

        public SettingsNotificationsPage()
        {
            this.InitializeComponent();

            LoadSettingsControls();

            Loaded += SettingsNotificationsPage_Loaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.NavigationMode == NavigationMode.New) _pendingRevealSection = e.Parameter as string;
        }

        private void SettingsNotificationsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateTileNotificationsVisibility();
                UpdateBackgroundDeniedNotice(false);
                RevealPendingSection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: Loaded failed", ex);
            }
        }

        private void LoadSettingsControls()
        {
            _loadingSettings = true;
            try
            {
                SocialNotificationsToggle.IsOn = SocialNotificationService.Enabled;
                SocialBackgroundCheckToggle.IsOn = SocialNotificationService.BackgroundCheckEnabled;
                SocialBackgroundCheckToggle.IsEnabled = SocialNotificationsToggle.IsOn;
                UpdateBackgroundDeniedNotice(false);

                TileBadgeToggle.IsOn = LiveTileService.BadgeEnabled;

                UpdateTileNotificationsVisibility();
                TileNotificationsToggle.IsOn = SocialTileService.PreviewsEnabled;
                LockScreenSendersToggle.IsOn = SocialTileService.LockScreenShowsSenders;
                LockScreenSendersToggle.IsEnabled = TileNotificationsToggle.IsOn;
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: LoadSettingsControls failed", ex);
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private async void SocialNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            SocialBackgroundCheckToggle.IsEnabled = SocialNotificationsToggle.IsOn;

            if (_loadingSettings) return;

            try
            {
                SocialNotificationService.Enabled = SocialNotificationsToggle.IsOn;
                UpdateTileNotificationsVisibility();

                await SocialNotificationService.ReconcileAsync();
                UpdateBackgroundDeniedNotice(true);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: could not apply the notifications setting", ex);
            }
        }

        private async void SocialBackgroundCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;

            try
            {
                SocialNotificationService.BackgroundCheckEnabled = SocialBackgroundCheckToggle.IsOn;

                await SocialNotificationService.UpdateBackgroundCheckAsync();
                UpdateBackgroundDeniedNotice(true);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: could not apply the background check setting", ex);
            }
        }

        private void UpdateBackgroundDeniedNotice(bool announce)
        {
            var denied = SocialNotificationsToggle.IsOn && SocialBackgroundCheckToggle.IsOn &&
                         SocialNotificationService.BackgroundAccessDenied;
            var wasVisible = SocialNotificationsBackgroundDenied.IsShown();

            SocialNotificationsBackgroundDenied.Visibility = denied ? Visibility.Visible : Visibility.Collapsed;

            if (announce && denied && !wasVisible)
            {
                AutomationHelper.AnnounceLiveRegion(SocialNotificationsBackgroundDenied);
            }
        }

        private async void SocialNotificationsLockScreenLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:lockscreen"));
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: could not open lock screen settings", ex);
            }
        }

        private void TileBadgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LiveTileService.BadgeEnabled = TileBadgeToggle.IsOn;

            if (TileBadgeToggle.IsOn)
            {
                SocialNotificationService.ReapplyBadge();
            }
        }

        private void UpdateTileNotificationsVisibility()
        {
            var shown = SocialTileService.StartShowsLiveTiles && SocialNotificationService.Enabled;
            TileNotificationsPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TileNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            LockScreenSendersToggle.IsEnabled = TileNotificationsToggle.IsOn;

            if (_loadingSettings) return;
            SocialTileService.PreviewsEnabled = TileNotificationsToggle.IsOn;

            if (TileNotificationsToggle.IsOn)
            {
                SocialNotificationService.RefreshTileInBackground();
            }
            else
            {
                SocialTileService.Withdraw();
            }
        }

        private void LockScreenSendersToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            SocialTileService.LockScreenShowsSenders = LockScreenSendersToggle.IsOn;
            SocialNotificationService.RefreshTileInBackground();
        }

        private void RefreshTileButton_Click(object sender, RoutedEventArgs e)
        {
            var shell = MainPage.Current;
            if (shell == null) return;

            shell.RefreshLiveTile();
        }

        private void ClearTileButton_Click(object sender, RoutedEventArgs e)
        {
            LiveTileService.TileCleared = true;
            SocialTileService.ClearTile();
            LiveTileService.ClearBadge();
        }

        public void RevealSection(string section)
        {
            _pendingRevealSection = section;
            if (IsLoaded) RevealPendingSection();
        }

        private void RevealPendingSection()
        {
            var section = _pendingRevealSection;
            _pendingRevealSection = null;

            switch (section)
            {
                case AppConstants.SettingsSectionNotifications:
                    SettingsSectionReveal.Reveal(PageScrollViewer, NotificationsSection);
                    break;
                case AppConstants.SettingsSectionTile:
                    SettingsSectionReveal.Reveal(PageScrollViewer, TileSection);
                    break;
            }
        }
    }
}
