using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsAboutPage : Page, IShellContentPage, ISettingsSubpage
    {
        private bool _loadingSettings = true;

        private bool _updateCheckInProgress = false;

        private string _pendingRevealSection;

        public SettingsAboutPage()
        {
            this.InitializeComponent();

            AboutVersionText.Text = LocalizedStrings.Format("AboutVersionFormat", AppConstants.AppVersionDisplay);

            _loadingSettings = true;
            try
            {
                AutoUpdateCheckToggle.IsOn = UpdateService.AutomaticChecksEnabled;
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAboutPage: could not load the update setting", ex);
            }
            finally
            {
                _loadingSettings = false;
            }

            Loaded += SettingsAboutPage_Loaded;
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

        private void SettingsAboutPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                RestoreDiagnosticsSection();
                RevealPendingSection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAboutPage: Loaded failed", ex);
            }
        }

        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateCheckInProgress) return;
            _updateCheckInProgress = true;

            try
            {
                CheckForUpdatesButton.IsEnabled = false;
                UpdateDownloadLink.Visibility = Visibility.Collapsed;
                ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusChecking"));

                var result = await UpdateService.CheckNowAsync();

                switch (result.Outcome)
                {
                    case UpdateCheckOutcome.UpdateAvailable:
                        ShowUpdateStatus(LocalizedStrings.Format("UpdateStatusAvailableFormat",
                                                                 UpdateService.FormatForDisplay(result.LatestVersion)));
                        UpdateDownloadLink.Visibility = Visibility.Visible;
                        break;
                    case UpdateCheckOutcome.UpToDate:
                        ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusUpToDate"));
                        break;
                    default:
                        ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusFailed"));
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAboutPage: update check failed", ex);
                ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusFailed"));
            }
            finally
            {
                CheckForUpdatesButton.IsEnabled = true;
                _updateCheckInProgress = false;
            }
        }

        private void ShowUpdateStatus(string message)
        {
            UpdateStatusText.Text = message;
            UpdateStatusText.Visibility = Visibility.Visible;

            AutomationHelper.AnnounceLiveRegion(UpdateStatusText);
        }

        private async void UpdateDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WebLauncher.LaunchAsync(UpdateService.LatestReleasePageUrl);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAboutPage: could not open the release page", ex);
            }
        }

        private void AutoUpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            UpdateService.AutomaticChecksEnabled = AutoUpdateCheckToggle.IsOn;
        }

        private async void ResetWelcomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shell = MainPage.Current;
                if (shell == null) return;

                await shell.ReplayWelcomeDialogAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAboutPage: Reset welcome failed", ex);
            }
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
                case AppConstants.SettingsSectionAbout:
                    SettingsSectionReveal.Reveal(PageScrollViewer, UpdatesSection);
                    break;
                case AppConstants.SettingsSectionWelcome:
                    SettingsSectionReveal.Reveal(PageScrollViewer, WelcomeSection);
                    break;
            }
        }
    }
}
