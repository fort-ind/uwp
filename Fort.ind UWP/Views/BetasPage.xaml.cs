using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
                MultipleViewsToggle.IsOn = LabsService.MultipleViewsSaved;
                PinGamesToggle.IsOn = LabsService.PinGamesEnabled;
                SocialNotificationsToggle.IsOn = LabsService.SocialNotificationsEnabled;
                UpdateRestartNotice(false);
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

        private void MultipleViewsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingLabs) return;

            try
            {
                LabsService.SaveMultipleViews(MultipleViewsToggle.IsOn);
                UpdateRestartNotice(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BetasPage: could not save the multiple windows lab - {ex.Message}");
            }
        }

        private void PinGamesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingLabs) return;

            try
            {
                LabsService.SavePinGames(PinGamesToggle.IsOn);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BetasPage: could not save the pin games lab - {ex.Message}");
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

        private void UpdateRestartNotice(bool announce)
        {
            var pending = LabsService.IsRestartPending;
            var wasVisible = LabsRestartNotice.Visibility == Visibility.Visible;

            LabsRestartNotice.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;

            if (announce && pending && !wasVisible)
            {
                AutomationHelper.AnnounceLiveRegion(LabsRestartNoticeText);
            }
        }

        private async void LabsRestartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RequestAppRestartAsync();

                await DialogService.ShowMessageAsync(this,
                                                     LocalizedStrings.Get("LabsRestartFailedDialogTitle"),
                                                     LocalizedStrings.Get("LabsRestartFailedDialogBody"),
                                                     LocalizedStrings.Get("DialogOk"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BetasPage: LabsRestartButton_Click failed - {ex.Message}");
            }
        }

        private static async Task RequestAppRestartAsync()
        {
            try
            {
                var failureReason = await Windows.ApplicationModel.Core.CoreApplication.RequestRestartAsync("");
                Debug.WriteLine($"BetasPage: App restart request did not restart the app - {failureReason}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BetasPage: App restart request threw - {ex.Message}");
            }
        }
    }
}
