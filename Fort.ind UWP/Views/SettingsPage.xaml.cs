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
            LiveTileService.ClearTile();
            LiveTileService.ClearBadge();
        }

        private void TileBadgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LiveTileService.BadgeEnabled = TileBadgeToggle.IsOn;
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
            var isExpanded = content.Visibility == Visibility.Collapsed;

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

        private void RestoreSettingsPanelStates()
        {
            try
            {
                RestorePanelState(AppConstants.SettingSettingsAppearanceExpanded, AppearanceHeader, AppearanceContent, AppearanceChevronRotation);
                RestorePanelState(AppConstants.SettingSettingsTransparencyExpanded, TransparencyHeader, TransparencyContent, TransparencyChevronRotation);
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
