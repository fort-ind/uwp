using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsPage : Page, IShellContentPage
    {
        private bool _loadingSettings = true;

        private bool _authHandlerAttached = false;

        private bool _appearanceHandlerAttached = false;

        private string _pendingRevealSection;

        private bool _updateCheckInProgress = false;

        private int _footprintGeneration = 0;

        private int _mediaCacheGeneration = 0;

        private bool _mediaCacheClearInProgress = false;

        private static readonly string[] s_sizeUnitKeys =
        {
            "StorageSizeBytesFormat",
            "StorageSizeKilobytesFormat",
            "StorageSizeMegabytesFormat",
            "StorageSizeGigabytesFormat"
        };

        private static Windows.Globalization.NumberFormatting.DecimalFormatter s_sizeFormatter;

        public SettingsPage()
        {
            this.InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            AboutVersionText.Text = LocalizedStrings.Format("AboutVersionFormat", AppConstants.AppVersionDisplay);

            LoadSettingsControls();

            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_authHandlerAttached)
                {
                    ProfileService.AuthStateChanged += OnAuthStateChanged;
                    _authHandlerAttached = true;
                }

                if (!_appearanceHandlerAttached)
                {
                    AppearanceService.Changed += OnAppearanceChanged;
                    _appearanceHandlerAttached = true;
                }

                UpdateStorageInfo();
                UpdateTileNotificationsVisibility();
                UpdateBackgroundDeniedNotice(false);

                RevealPendingSection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: Loaded failed - {ex.Message}");
            }
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }

            if (_appearanceHandlerAttached)
            {
                AppearanceService.Changed -= OnAppearanceChanged;
                _appearanceHandlerAttached = false;
            }

            AppearanceService.FlushAcrylicPersist();
        }

        private async void OnAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                    {
                        try
                        {
                            UpdateStorageInfo();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"SettingsPage: UpdateStorageInfo failed - {ex.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: Auth state change handler failed - {ex.Message}");
            }
        }

        private void OnAppearanceChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateTintSelection(AppearanceService.TintTag);
                UpdateAcrylicLegibilityWarnings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: appearance refresh failed - {ex.Message}");
            }
        }

        private void UpdateStorageInfo()
        {
            RefreshStorageFootprint();
            RefreshMediaCacheSize();

            try
            {
                StoragePathText.Text = LocalizedStrings.Format("StorageLocationFormat", LocalStorageService.DataPath);

                var user = ProfileService.CurrentUser;
                if (user != null)
                {
                    var host = string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host;
                    CacheDescriptionText.Text = LocalizedStrings.Get("StorageCacheSignedIn");
                    UserCountText.Text = LocalizedStrings.Format("StorageSignedInAsFormat", user.Username, host);
                    ClearLoginInfoButton.Visibility = Visibility.Visible;
                }
                else
                {
                    CacheDescriptionText.Text = LocalizedStrings.Get("StorageCacheSignedOut");
                    UserCountText.Text = "";
                    ClearLoginInfoButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: UpdateStorageInfo failed - {ex.Message}");
                StoragePathText.Text = "";
                CacheDescriptionText.Text = "";
                UserCountText.Text = "";
                ClearLoginInfoButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshStorageFootprint()
        {
            int generation = 0;

            try
            {
                generation = ++_footprintGeneration;

                if (string.IsNullOrEmpty(StorageFootprintText.Text))
                {
                    StorageFootprintText.Text = LocalizedStrings.Get("StorageFootprintMeasuring");
                }

                var bytes = await LocalStorageService.MeasureAppFootprintAsync();
                if (generation != _footprintGeneration) return;

                StorageFootprintText.Text = bytes.HasValue
                    ? LocalizedStrings.Format("StorageFootprintFormat", FormatByteSize(bytes.Value))
                    : LocalizedStrings.Get("StorageFootprintUnavailable");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: storage footprint failed - {ex.Message}");

                if (generation == _footprintGeneration)
                {
                    StorageFootprintText.Text = LocalizedStrings.Get("StorageFootprintUnavailable");
                }
            }
        }

        private async void RefreshMediaCacheSize()
        {
            int generation = 0;

            try
            {
                generation = ++_mediaCacheGeneration;

                if (string.IsNullOrEmpty(MediaCacheSizeText.Text))
                {
                    MediaCacheSizeText.Text = LocalizedStrings.Get("MediaCacheMeasuring");
                }

                var bytes = await MediaCacheService.MeasureAsync();
                if (generation != _mediaCacheGeneration) return;

                MediaCacheSizeText.Text = bytes.HasValue
                    ? LocalizedStrings.Format("MediaCacheSizeFormat", FormatByteSize(bytes.Value))
                    : LocalizedStrings.Get("MediaCacheUnavailable");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: media cache size failed - {ex.Message}");

                if (generation == _mediaCacheGeneration)
                {
                    MediaCacheSizeText.Text = LocalizedStrings.Get("MediaCacheUnavailable");
                }
            }
        }

        private async void ClearMediaCacheButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaCacheClearInProgress) return;
            _mediaCacheClearInProgress = true;

            try
            {
                ClearMediaCacheButton.IsEnabled = false;
                ShowMediaCacheStatus(LocalizedStrings.Get("MediaCacheClearing"));

                var freed = await MediaCacheService.ClearAsync();

                if (!freed.HasValue)
                {
                    ShowMediaCacheStatus(LocalizedStrings.Get("MediaCacheClearFailed"));
                }
                else if (freed.Value <= 0)
                {
                    ShowMediaCacheStatus(LocalizedStrings.Get("MediaCacheClearedNothing"));
                }
                else
                {
                    ShowMediaCacheStatus(LocalizedStrings.Format("MediaCacheClearedFormat", FormatByteSize(freed.Value)));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: media cache clear failed - {ex.Message}");
                ShowMediaCacheStatus(LocalizedStrings.Get("MediaCacheClearFailed"));
            }
            finally
            {
                ClearMediaCacheButton.IsEnabled = true;
                _mediaCacheClearInProgress = false;
                RefreshMediaCacheSize();
                RefreshStorageFootprint();
            }
        }

        private void ShowMediaCacheStatus(string message)
        {
            MediaCacheStatusText.Text = message;
            MediaCacheStatusText.Visibility = Visibility.Visible;

            AutomationHelper.AnnounceLiveRegion(MediaCacheStatusText);
        }

        private void LoadMediaCacheControls()
        {
            var limit = MediaCacheService.LimitMegabytes;
            ComboBoxItem fallback = null;
            ComboBoxItem match = null;
            foreach (var entry in MediaCacheLimitCombo.Items)
            {
                var item = entry as ComboBoxItem;
                var itemLimit = IntTagOf(item);
                if (!itemLimit.HasValue) continue;

                if (itemLimit.Value == limit) match = item;
                if (itemLimit.Value == AppConstants.DefaultMediaCacheLimitMegabytes) fallback = item;
            }

            MediaCacheLimitCombo.SelectedItem = match ?? fallback;
        }

        private void MediaCacheLimitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;

            var limit = IntTagOf(MediaCacheLimitCombo.SelectedItem as ComboBoxItem);
            if (limit.HasValue)
            {
                MediaCacheService.LimitMegabytes = limit.Value;
            }
        }

        private static string FormatByteSize(long bytes)
        {
            double value = bytes;
            int unit = 0;

            while (unit < s_sizeUnitKeys.Length - 1 && RoundForDisplay(value) >= 1024)
            {
                value /= 1024;
                unit++;
            }

            return LocalizedStrings.Format(s_sizeUnitKeys[unit], FormatSizeNumber(RoundForDisplay(value)));
        }

        private static double RoundForDisplay(double value)
        {
            return value >= 100
                ? Math.Round(value, MidpointRounding.AwayFromZero)
                : Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        private static string FormatSizeNumber(double value)
        {
            try
            {
                if (s_sizeFormatter == null)
                {
                    s_sizeFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter()
                    {
                        FractionDigits = 0,
                        IsGrouped = true,
                        NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder()
                        {
                            Increment = 0.1,
                            RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp
                        }
                    };
                }

                return s_sizeFormatter.Format(value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: size formatting failed - {ex.Message}");
                return value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            }
        }

        private async void ClearLoginInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirmed = await DialogService.ShowConfirmAsync(
                    this,
                    LocalizedStrings.Get("ClearLoginDialogTitle"),
                    LocalizedStrings.Get("ClearLoginDialogBody"),
                    LocalizedStrings.Get("ClearLoginDialogConfirm"),
                    LocalizedStrings.Get("DialogCancel"),
                    ContentDialogButton.Close);

                if (!confirmed) return;

                await ProfileService.LogoutAsync();
                UpdateStorageInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: Clear login info failed - {ex.Message}");
            }
        }

        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await DialogService.RunExclusiveAsync(async () =>
                {
                    var explained = await DialogService.ShowConfirmCoreAsync(
                        this,
                        LocalizedStrings.Get("ResetExplainDialogTitle"),
                        LocalizedStrings.Get("ResetExplainDialogBody"),
                        LocalizedStrings.Get("ResetExplainDialogConfirm"),
                        LocalizedStrings.Get("DialogCancel"),
                        ContentDialogButton.Close);

                    if (!explained) return;

                    var confirmed = await DialogService.ShowConfirmCoreAsync(
                        this,
                        LocalizedStrings.Get("ResetConfirmDialogTitle"),
                        LocalizedStrings.Get("ResetConfirmDialogBody"),
                        LocalizedStrings.Get("ResetConfirmDialogConfirm"),
                        LocalizedStrings.Get("DialogCancel"),
                        ContentDialogButton.Close);

                    if (!confirmed) return;

                    await ProfileService.ResetAppDataAsync();

                    AppearanceService.Reload();
                    LoadSettingsControls();
                    UpdateStorageInfo();

                    var restartNow = await DialogService.ShowConfirmCoreAsync(
                        this,
                        LocalizedStrings.Get("ResetDoneDialogTitle"),
                        LocalizedStrings.Get("ResetDoneDialogBody"),
                        LocalizedStrings.Get("ResetDoneDialogRestart"),
                        LocalizedStrings.Get("ResetDoneDialogLater"),
                        ContentDialogButton.Primary);

                    if (restartNow)
                    {
                        await RequestAppRestartAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: App reset flow failed - {ex.Message}");
            }
        }

        private static async Task RequestAppRestartAsync()
        {
            try
            {
                var failureReason = await Windows.ApplicationModel.Core.CoreApplication.RequestRestartAsync("");
                Debug.WriteLine($"SettingsPage: App restart request did not restart the app - {failureReason}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: App restart request threw - {ex.Message}");
            }
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

        private void LoadSocialNotificationControls()
        {
            SocialNotificationsToggle.IsOn = SocialNotificationService.Enabled;
            SocialBackgroundCheckToggle.IsOn = SocialNotificationService.BackgroundCheckEnabled;
            SocialBackgroundCheckToggle.IsEnabled = SocialNotificationsToggle.IsOn;
            UpdateBackgroundDeniedNotice(false);
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
                Debug.WriteLine($"SettingsPage: could not apply the notifications setting - {ex.Message}");
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
                Debug.WriteLine($"SettingsPage: could not apply the background check setting - {ex.Message}");
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
                Debug.WriteLine($"SettingsPage: could not open lock screen settings - {ex.Message}");
            }
        }

        private void UpdateTileNotificationsVisibility()
        {
            var shown = SocialTileService.StartShowsLiveTiles && SocialNotificationService.Enabled;
            TileNotificationsPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadTileNotificationControls()
        {
            UpdateTileNotificationsVisibility();

            TileNotificationsToggle.IsOn = SocialTileService.PreviewsEnabled;
            LockScreenSendersToggle.IsOn = SocialTileService.LockScreenShowsSenders;
            LockScreenSendersToggle.IsEnabled = TileNotificationsToggle.IsOn;
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

        private void TileBadgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LiveTileService.BadgeEnabled = TileBadgeToggle.IsOn;

            if (TileBadgeToggle.IsOn)
            {
                SocialNotificationService.ReapplyBadge();
            }
        }

        private void LoadProfileRefreshControls()
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
                var itemMinutes = IntTagOf(item);
                if (!itemMinutes.HasValue) continue;

                if (itemMinutes.Value == minutes) match = item;
                if (itemMinutes.Value == AppConstants.DefaultProfileRefreshMinutes) fallback = item;
            }

            ProfileRefreshIntervalCombo.SelectedItem = match ?? fallback;

            RemoteListNoticeToggle.IsOn = SocialFollowService.ShowRemoteListNotice;
        }

        private void RemoteListNoticeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            SocialFollowService.ShowRemoteListNotice = RemoteListNoticeToggle.IsOn;
        }

        private static int? IntTagOf(ComboBoxItem item)
        {
            int value;
            if (item == null || !int.TryParse(item.Tag as string, System.Globalization.NumberStyles.Integer,
                                              System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return null;
            }

            return value;
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

            var minutes = IntTagOf(ProfileRefreshIntervalCombo.SelectedItem as ComboBoxItem);
            if (minutes.HasValue)
            {
                ProfileService.AutoRefreshMinutes = minutes.Value;
                SocialNotificationService.UpdateBackgroundCheckInBackground();
            }
        }

        private void ProfileHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(ProfileHeader, ProfileContent, ProfileChevronRotation, AppConstants.SettingSettingsProfileExpanded);
        }

        private void NotificationsHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(NotificationsHeader, NotificationsContent, NotificationsChevronRotation, AppConstants.SettingSettingsNotificationsExpanded);
        }

        private void StorageHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(StorageHeader, StorageContent, StorageChevronRotation, AppConstants.SettingSettingsStorageExpanded);
        }

        private void TileHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(TileHeader, TileContent, TileChevronRotation, AppConstants.SettingSettingsTileExpanded);
        }

        private void WelcomeHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(WelcomeHeader, WelcomeContent, WelcomeChevronRotation, AppConstants.SettingSettingsWelcomeExpanded);
        }

        private void AboutHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AboutHeader, AboutContent, AboutChevronRotation, AppConstants.SettingSettingsAboutExpanded);
        }

        private void ToggleSettingsRow(ExpanderHeaderButton header, StackPanel content,
                                       RotateTransform chevronTransform, string settingKey = null)
        {
            var isExpanded = !content.IsShown();

            if (isExpanded)
            {
                content.Visibility = Visibility.Visible;
                chevronTransform.Angle = 90;
            }
            else
            {
                content.Visibility = Visibility.Collapsed;
                chevronTransform.Angle = 0;
            }

            header.IsExpanded = isExpanded;

            if (!string.IsNullOrEmpty(settingKey))
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[settingKey] = isExpanded;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SettingsPage: Failed to save panel state - {ex.Message}");
                }
            }
        }

        internal void RevealSection(string section)
        {
            _pendingRevealSection = section;

            if (IsLoaded)
            {
                RevealPendingSection();
            }
        }

        private void RevealPendingSection()
        {
            var section = _pendingRevealSection;
            _pendingRevealSection = null;
            if (string.IsNullOrEmpty(section)) return;

            try
            {
                var row = SettingsRowFor(section);
                if (row == null)
                {
                    Debug.WriteLine($"SettingsPage: no section named '{section}' to reveal");
                    return;
                }

                if (!row.Content.IsShown())
                {
                    ToggleSettingsRow(row.Header, row.Content, row.Chevron, row.SettingKey);
                }

                row.Header.Focus(FocusState.Programmatic);

                BringIntoViewOptions options = new BringIntoViewOptions();
                options.VerticalAlignmentRatio = 0;
                options.AnimationDesired = true;
                row.Header.StartBringIntoView(options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: Failed to reveal section {section} - {ex.Message}");
            }
        }

        private sealed class SettingsRow
        {
            public SettingsRow(ExpanderHeaderButton header, StackPanel content, RotateTransform chevron, string settingKey)
            {
                Header = header;
                Content = content;
                Chevron = chevron;
                SettingKey = settingKey;
            }

            public ExpanderHeaderButton Header { get; private set; }
            public StackPanel Content { get; private set; }
            public RotateTransform Chevron { get; private set; }
            public string SettingKey { get; private set; }
        }

        private SettingsRow SettingsRowFor(string section)
        {
            switch (section)
            {
                case AppConstants.SettingsSectionAppearance:
                    return new SettingsRow(AppearanceHeader, AppearanceContent, AppearanceChevronRotation, AppConstants.SettingSettingsAppearanceExpanded);
                case AppConstants.SettingsSectionTransparency:
                    return new SettingsRow(TransparencyHeader, TransparencyContent, TransparencyChevronRotation, AppConstants.SettingSettingsTransparencyExpanded);
                case AppConstants.SettingsSectionProfile:
                    return new SettingsRow(ProfileHeader, ProfileContent, ProfileChevronRotation, AppConstants.SettingSettingsProfileExpanded);
                case AppConstants.SettingsSectionNotifications:
                    return new SettingsRow(NotificationsHeader, NotificationsContent, NotificationsChevronRotation, AppConstants.SettingSettingsNotificationsExpanded);
                case AppConstants.SettingsSectionStorage:
                    return new SettingsRow(StorageHeader, StorageContent, StorageChevronRotation, AppConstants.SettingSettingsStorageExpanded);
                case AppConstants.SettingsSectionTile:
                    return new SettingsRow(TileHeader, TileContent, TileChevronRotation, AppConstants.SettingSettingsTileExpanded);
                case AppConstants.SettingsSectionWelcome:
                    return new SettingsRow(WelcomeHeader, WelcomeContent, WelcomeChevronRotation, AppConstants.SettingSettingsWelcomeExpanded);
                case AppConstants.SettingsSectionAbout:
                    return new SettingsRow(AboutHeader, AboutContent, AboutChevronRotation, AppConstants.SettingSettingsAboutExpanded);
                default:
                    return null;
            }
        }

        private void RestoreSettingsPanelStates()
        {
            try
            {
                RestorePanelState(AppConstants.SettingSettingsAppearanceExpanded, AppearanceHeader, AppearanceContent, AppearanceChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsTransparencyExpanded, TransparencyHeader, TransparencyContent, TransparencyChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsProfileExpanded, ProfileHeader, ProfileContent, ProfileChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsNotificationsExpanded, NotificationsHeader, NotificationsContent, NotificationsChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsStorageExpanded, StorageHeader, StorageContent, StorageChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsTileExpanded, TileHeader, TileContent, TileChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsWelcomeExpanded, WelcomeHeader, WelcomeContent, WelcomeChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsAboutExpanded, AboutHeader, AboutContent, AboutChevronRotation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: Failed to restore panel states - {ex.Message}");
            }
        }

        private void RestorePanelState(string settingKey, ExpanderHeaderButton header,
                                       StackPanel content, RotateTransform chevronTransform)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                var isExpanded = false;
                if (localSettings.Values.ContainsKey(settingKey))
                {
                    isExpanded = Convert.ToBoolean(localSettings.Values[settingKey]);
                }

                if (isExpanded)
                {
                    content.Visibility = Visibility.Visible;
                    chevronTransform.Angle = 90;
                }
                else
                {
                    content.Visibility = Visibility.Collapsed;
                    chevronTransform.Angle = 0;
                }

                header.IsExpanded = isExpanded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage: Failed to restore {settingKey} - {ex.Message}");
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
                Debug.WriteLine($"SettingsPage: update check failed - {ex.Message}");
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
                Debug.WriteLine($"SettingsPage: could not open the release page - {ex.Message}");
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
                Debug.WriteLine($"SettingsPage: Reset welcome failed - {ex.Message}");
            }
        }
    }
}
