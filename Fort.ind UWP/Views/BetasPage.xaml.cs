using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class BetasPage : Page, IShellContentPage
    {
        private bool _loadingLabs = false;

        public BetasPage()
        {
            this.InitializeComponent();

            Loaded += BetasPage_Loaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        private void BetasPage_Loaded(object sender, RoutedEventArgs e)
        {
            _loadingLabs = true;
            try
            {
                SocialNotificationsToggle.IsOn = LabsService.SocialNotificationsEnabled;
                UpdateBackgroundDeniedNotice(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BetasPage: could not load the labs state - {ex.Message}");
            }
            finally
            {
                _loadingLabs = false;
            }
        }

        private async void SocialNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingLabs) return;

            try
            {
                LabsService.SaveSocialNotifications(SocialNotificationsToggle.IsOn);
                await SocialNotificationService.ReconcileAsync();
                UpdateBackgroundDeniedNotice(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BetasPage: could not apply the fort.social notifications lab - {ex.Message}");
            }
        }

        private void UpdateBackgroundDeniedNotice(bool announce)
        {
            var denied = SocialNotificationsToggle.IsOn && SocialNotificationService.BackgroundAccessDenied;
            var wasVisible = SocialNotificationsBackgroundDenied.Visibility == Visibility.Visible;

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
                Debug.WriteLine($"BetasPage: could not open lock screen settings - {ex.Message}");
            }
        }
    }
}
