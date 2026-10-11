using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsAccountPage : Page, IShellContentPage, ISettingsSubpage, IUnsavedChangesPage
    {
        private bool _loadingSettings = true;

        private string _pendingRevealSection;

        public SettingsAccountPage()
        {
            this.InitializeComponent();

            LoadSettingsControls();

            Loaded += SettingsAccountPage_Loaded;
            Unloaded += SettingsAccountPage_Unloaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        public bool HasUnsavedChanges
        {
            get { return ProfileEditor.HasUnsavedChanges; }
        }

        public Task<bool> ConfirmLeaveAsync()
        {
            return ProfileEditor.ConfirmLeaveAsync();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.NavigationMode == NavigationMode.New) _pendingRevealSection = e.Parameter as string;
        }

        private void SettingsAccountPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AttachPrivacy();
                LoadModeration();
                ProfileEditor.RefreshFromServer();
                RevealPendingSection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: Loaded failed", ex);
            }
        }

        private void SettingsAccountPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachPrivacy();
        }

        private void LoadSettingsControls()
        {
            _loadingSettings = true;
            try
            {
                var enabled = ProfileService.AutoRefreshEnabled;
                ProfileAutoRefreshToggle.IsOn = enabled;
                ProfileRefreshIntervalCombo.IsEnabled = enabled;

                var minutes = ProfileService.AutoRefreshMinutes;
                ComboBoxItem fallback = null;
                ComboBoxItem match = null;
                foreach (var entry in ProfileRefreshIntervalCombo.Items)
                {
                    var item = entry as ComboBoxItem;
                    var itemMinutes = SettingsComboTags.IntTagOf(item);
                    if (!itemMinutes.HasValue) continue;

                    if (itemMinutes.Value == minutes) match = item;
                    if (itemMinutes.Value == AppConstants.DefaultProfileRefreshMinutes) fallback = item;
                }

                ProfileRefreshIntervalCombo.SelectedItem = match ?? fallback;

                RemoteListNoticeToggle.IsOn = SocialFollowService.ShowRemoteListNotice;
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: LoadSettingsControls failed", ex);
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void ProfileAutoRefreshToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ProfileRefreshIntervalCombo.IsEnabled = ProfileAutoRefreshToggle.IsOn;

            if (_loadingSettings) return;
            ProfileService.AutoRefreshEnabled = ProfileAutoRefreshToggle.IsOn;
            SocialNotificationService.UpdateBackgroundCheckInBackground();
        }

        private void ProfileRefreshIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;

            var minutes = SettingsComboTags.IntTagOf(ProfileRefreshIntervalCombo.SelectedItem as ComboBoxItem);
            if (minutes.HasValue)
            {
                ProfileService.AutoRefreshMinutes = minutes.Value;
                SocialNotificationService.UpdateBackgroundCheckInBackground();
            }
        }

        private void RemoteListNoticeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            SocialFollowService.ShowRemoteListNotice = RemoteListNoticeToggle.IsOn;
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
                case AppConstants.SettingsSectionProfile:
                    SettingsSectionReveal.Reveal(PageScrollViewer, ProfileSection);
                    break;
                case AppConstants.SettingsSectionPrivacy:
                    SettingsSectionReveal.Reveal(PageScrollViewer, PrivacySection);
                    break;
                case AppConstants.SettingsSectionMutedBlocked:
                    SettingsSectionReveal.Reveal(PageScrollViewer, MutedBlockedSection);
                    break;
            }
        }
    }
}
