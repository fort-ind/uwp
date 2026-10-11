using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsStoragePage : Page, IShellContentPage, ISettingsSubpage
    {
        private bool _loadingSettings = true;

        private bool _authHandlerAttached = false;

        private string _pendingRevealSection;

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

        public SettingsStoragePage()
        {
            this.InitializeComponent();

            LoadSettingsControls();

            Loaded += SettingsStoragePage_Loaded;
            Unloaded += SettingsStoragePage_Unloaded;
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

        private void SettingsStoragePage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_authHandlerAttached)
                {
                    ProfileService.AuthStateChanged += OnAuthStateChanged;
                    _authHandlerAttached = true;
                }

                UpdateStorageInfo();
                RevealPendingSection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsStoragePage: Loaded failed", ex);
            }
        }

        private void SettingsStoragePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }
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
                            AppLog.Error("SettingsStoragePage: UpdateStorageInfo failed", ex);
                        }
                    });
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsStoragePage: Auth state change handler failed", ex);
            }
        }

        private void LoadSettingsControls()
        {
            _loadingSettings = true;
            try
            {
                LoadMediaCacheControls();
                LoadLowMemoryControls();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsStoragePage: LoadSettingsControls failed", ex);
            }
            finally
            {
                _loadingSettings = false;
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
                AppLog.Error("SettingsStoragePage: UpdateStorageInfo failed", ex);
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
                AppLog.Error("SettingsStoragePage: storage footprint failed", ex);

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
                AppLog.Error("SettingsStoragePage: media cache size failed", ex);

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
                AppLog.Error("SettingsStoragePage: media cache clear failed", ex);
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
                var itemLimit = SettingsComboTags.IntTagOf(item);
                if (!itemLimit.HasValue) continue;

                if (itemLimit.Value == limit) match = item;
                if (itemLimit.Value == AppConstants.DefaultMediaCacheLimitMegabytes) fallback = item;
            }

            MediaCacheLimitCombo.SelectedItem = match ?? fallback;
        }

        private void MediaCacheLimitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;

            var limit = SettingsComboTags.IntTagOf(MediaCacheLimitCombo.SelectedItem as ComboBoxItem);
            if (limit.HasValue)
            {
                MediaCacheService.LimitMegabytes = limit.Value;
            }
        }

        private void LoadLowMemoryControls()
        {
            var choice = MemoryService.LowMemoryChoice.ToString();
            LowMemoryModeCombo.SelectedItem = LowMemoryModeCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, choice, StringComparison.Ordinal));
            UpdateLowMemoryStatus(false);
        }

        private void LowMemoryModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;

            var item = LowMemoryModeCombo.SelectedItem as ComboBoxItem;
            LowMemoryModeChoice choice;
            if (item == null || !Enum.TryParse(item.Tag as string, out choice)) return;

            MemoryService.SetLowMemoryChoice(choice);
            UpdateLowMemoryStatus(true);
        }

        private void UpdateLowMemoryStatus(bool announce)
        {
            LowMemoryStatusText.Text = LocalizedStrings.Get(MemoryService.IsLowMemoryMode ? "LowMemoryStatusOn" : "LowMemoryStatusOff");
            if (announce) AutomationHelper.AnnounceLiveRegion(LowMemoryStatusText);
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
                AppLog.Error("SettingsStoragePage: size formatting failed", ex);
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
                AppLog.Error("SettingsStoragePage: Clear login info failed", ex);
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
                        await AppRestart.RequestAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsStoragePage: App reset flow failed", ex);
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

            if (section == AppConstants.SettingsSectionStorage)
            {
                SettingsSectionReveal.Reveal(PageScrollViewer, LocalCacheSection);
            }
        }
    }
}
