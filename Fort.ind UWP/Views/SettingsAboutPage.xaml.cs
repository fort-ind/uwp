using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
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

            RestoreSectionStates();

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
                ShowLastChecked();
                ShowInitialUpdateSummary();
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
                UpdateProgressRing.Visibility = Visibility.Visible;
                UpdateProgressRing.IsActive = true;
                ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusChecking"));
                UpdateSummaryText.Text = LocalizedStrings.Get("UpdateStatusChecking");

                var result = await UpdateService.CheckNowAsync();

                switch (result.Outcome)
                {
                    case UpdateCheckOutcome.UpdateAvailable:
                        var available = LocalizedStrings.Format("UpdateStatusAvailableFormat",
                                                                UpdateService.FormatForDisplay(result.LatestVersion));
                        ShowUpdateStatus(available);
                        UpdateSummaryText.Text = available;
                        UpdateDownloadLink.Visibility = Visibility.Visible;
                        break;
                    case UpdateCheckOutcome.UpToDate:
                        ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusUpToDate"));
                        var checkedAt = FormatLastChecked();
                        UpdateSummaryText.Text = checkedAt.Length == 0
                            ? LocalizedStrings.Get("UpdateSummaryUpToDate")
                            : LocalizedStrings.Format("UpdateSummaryUpToDateFormat", checkedAt);
                        break;
                    default:
                        ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusFailed"));
                        UpdateSummaryText.Text = LocalizedStrings.Get("UpdateSummaryFailed");
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAboutPage: update check failed", ex);
                ShowUpdateStatus(LocalizedStrings.Get("UpdateStatusFailed"));
                UpdateSummaryText.Text = LocalizedStrings.Get("UpdateSummaryFailed");
            }
            finally
            {
                UpdateProgressRing.IsActive = false;
                UpdateProgressRing.Visibility = Visibility.Collapsed;
                ShowLastChecked();
                CheckForUpdatesButton.IsEnabled = true;
                _updateCheckInProgress = false;
            }
        }

        private static string FormatLastChecked()
        {
            var lastChecked = UpdateService.LastChecked;
            return lastChecked.HasValue ? RelativeTime.Full(lastChecked.Value) : "";
        }

        private void ShowLastChecked()
        {
            var text = FormatLastChecked();
            if (text.Length == 0) return;

            UpdateLastCheckedText.Text = LocalizedStrings.Format("UpdateLastCheckedFormat", text);
            UpdateLastCheckedText.Visibility = Visibility.Visible;
        }

        private void ShowInitialUpdateSummary()
        {
            var text = UpdateLastCheckedText.Text;
            UpdateSummaryText.Text = text.Length == 0
                ? LocalizedStrings.Get("UpdateSummaryDefault")
                : text;
        }

        private void UpdatesHeader_Click(object sender, RoutedEventArgs e)
        {
            ToggleSection(UpdatesHeader, UpdatesContent, UpdatesChevronRotation, AppConstants.SettingAboutUpdatesExpanded);
        }

        private void WelcomeHeader_Click(object sender, RoutedEventArgs e)
        {
            ToggleSection(WelcomeHeader, WelcomeContent, WelcomeChevronRotation, AppConstants.SettingAboutWelcomeExpanded);
        }

        private static void ToggleSection(ExpanderHeaderButton header, StackPanel content,
                                          RotateTransform chevron, string settingKey)
        {
            var expanded = !content.IsShown();
            SetSectionExpanded(header, content, chevron, expanded);
            AppSettings.Write(settingKey, expanded);
        }

        private static void SetSectionExpanded(ExpanderHeaderButton header, StackPanel content,
                                               RotateTransform chevron, bool expanded)
        {
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            chevron.Angle = expanded ? 90 : 0;
            header.IsExpanded = expanded;
        }

        private void RestoreSectionStates()
        {
            SetSectionExpanded(UpdatesHeader, UpdatesContent, UpdatesChevronRotation,
                               AppSettings.ReadBool(AppConstants.SettingAboutUpdatesExpanded, false));
            SetSectionExpanded(WelcomeHeader, WelcomeContent, WelcomeChevronRotation,
                               AppSettings.ReadBool(AppConstants.SettingAboutWelcomeExpanded, false));
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
                    if (!UpdatesContent.IsShown()) ToggleSection(UpdatesHeader, UpdatesContent, UpdatesChevronRotation, AppConstants.SettingAboutUpdatesExpanded);
                    SettingsSectionReveal.Reveal(PageScrollViewer, UpdatesSection);
                    break;
                case AppConstants.SettingsSectionWelcome:
                    if (!WelcomeContent.IsShown()) ToggleSection(WelcomeHeader, WelcomeContent, WelcomeChevronRotation, AppConstants.SettingAboutWelcomeExpanded);
                    SettingsSectionReveal.Reveal(PageScrollViewer, WelcomeSection);
                    break;
            }
        }
    }
}
