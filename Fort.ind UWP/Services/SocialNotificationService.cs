using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Background;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class SocialNotificationService
    {
        private const int MaximumToastTagLength = 64;

        private static readonly TimeSpan BackgroundBudget = TimeSpan.FromSeconds(25);

        private static readonly TimeSpan MinimumReconnectDelay = TimeSpan.FromSeconds(10);

        private static readonly TimeSpan MaximumReconnectDelay = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan CountRefreshDelay = TimeSpan.FromSeconds(1);

        private static readonly object s_lock = new object();

        private static readonly SemaphoreSlim s_reconcileGate = new SemaphoreSlim(1, 1);

        private static readonly SemaphoreSlim s_checkGate = new SemaphoreSlim(1, 1);

        private static readonly Debouncer s_countDebounce = new Debouncer();

        private static bool s_initialized;

        private static volatile bool s_active;

        private static volatile bool s_suspended;

        private static volatile int s_unreadCount = -1;

        private static SocialStream s_stream;

        private static int s_streamGeneration;

        private static TimeSpan s_reconnectDelay = MinimumReconnectDelay;

        private static volatile bool s_needsSignInAgain;

        public static event EventHandler<SocialNotification> NotificationArrived;

        public static event EventHandler NeedsSignInAgainChanged;

        public static bool NeedsSignInAgain
        {
            get { return s_needsSignInAgain; }
        }

        public static bool BackgroundAccessDenied { get; private set; }

        public static bool Enabled
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialNotificationsEnabled];
                    return stored == null || Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialNotificationService: could not read the notifications setting - {ex.Message}");
                    return true;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialNotificationsEnabled] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialNotificationService: could not save the notifications setting - {ex.Message}");
                }
            }
        }

        public static bool BackgroundCheckEnabled
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialBackgroundCheck];
                    return stored == null || Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialNotificationService: could not read the background check setting - {ex.Message}");
                    return true;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialBackgroundCheck] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialNotificationService: could not save the background check setting - {ex.Message}");
                }
            }
        }

        public static bool OwnsBadge
        {
            get { return Enabled && ReadActiveAccount() != null; }
        }

        public static bool SignInAgainDismissed
        {
            get
            {
                var user = ProfileService.CurrentUser;
                return user != null &&
                       string.Equals(ReadStoredString(AppConstants.SettingSocialSignInAgainDismissed), AccountOf(user),
                                     StringComparison.Ordinal);
            }
        }

        public static void DismissSignInAgain()
        {
            var user = ProfileService.CurrentUser;
            if (user == null) return;

            WriteStoredString(AppConstants.SettingSocialSignInAgainDismissed, AccountOf(user));
        }

        public static void Initialize()
        {
            lock (s_lock)
            {
                if (s_initialized) return;
                s_initialized = true;
            }

            ProfileService.AuthStateChanged += OnAuthStateChanged;
        }

        private static void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            ReconcileInBackground();
        }

        public static async void ReconcileInBackground()
        {
            try
            {
                await ReconcileAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: reconcile failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static async Task ReconcileAsync()
        {
            await s_reconcileGate.WaitAsync();
            try
            {
                var user = ProfileService.CurrentUser;
                var wanted = Enabled && user != null;
                var wasActive = s_active;
                var previousAccount = ReadActiveAccount();
                s_active = wanted;

                if (wanted)
                {
                    var account = AccountOf(user);
                    WriteStoredString(AppConstants.SettingSocialNotificationsAccount, account);

                    if (previousAccount != null && !string.Equals(previousAccount, account, StringComparison.Ordinal))
                    {
                        LeaveAccount(true);
                    }

                    await ApplyBackgroundCheckAsync();

                    if (!s_suspended)
                    {
                        StartStream();
                    }

                    await ProbeNotificationsPermissionAsync();
                    await RefreshUnreadCountAsync(CancellationToken.None);
                    return;
                }

                WriteStoredString(AppConstants.SettingSocialNotificationsAccount, null);
                UnregisterBackgroundCheck();
                LeaveAccount(wasActive || previousAccount != null || ReadWatermark().HasValue);
            }
            finally
            {
                s_reconcileGate.Release();
            }
        }

        public static async Task UpdateBackgroundCheckAsync()
        {
            await s_reconcileGate.WaitAsync();
            try
            {
                if (s_active)
                {
                    await ApplyBackgroundCheckAsync();
                }
            }
            finally
            {
                s_reconcileGate.Release();
            }
        }

        public static async void UpdateBackgroundCheckInBackground()
        {
            try
            {
                await UpdateBackgroundCheckAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: background check update failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static async Task ApplyBackgroundCheckAsync()
        {
            if (BackgroundCheckEnabled)
            {
                await EnsureBackgroundCheckAsync();
            }
            else
            {
                UnregisterBackgroundCheck();
            }
        }

        private static void LeaveAccount(bool clearState)
        {
            StopStream();
            SetNeedsSignInAgain(false);
            SocialTileService.Withdraw();

            if (clearState)
            {
                ClearState();
            }
        }

        public static void OnSuspending()
        {
            s_suspended = true;
            StopStream();
        }

        public static void OnResuming()
        {
            s_suspended = false;
            if (s_active)
            {
                StartStream();
            }
        }

        public static void ReapplyBadge()
        {
            var count = s_unreadCount;
            if (OwnsBadge && count >= 0)
            {
                LiveTileService.UpdateBadge(count);
            }
        }

        public static async void RefreshTileInBackground()
        {
            try
            {
                var account = ReadActiveAccount();
                if (!s_active || account == null) return;

                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                await RefreshTileAsync(token, account, s_unreadCount, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: tile refresh failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNotification>>> FetchNotificationsAsync(
            string untilId, bool markAsRead, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetNotificationsAsync(token, untilId, AppConstants.SocialFeedPageSize,
                                                                      markAsRead, cancellationToken);
            ObservePermission(result.Status);
            if (result.Status == SocialApiStatus.Ok && markAsRead)
            {
                ApplyUnreadCount(0);
            }

            return result;
        }

        public static async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken)
        {
            var known = s_unreadCount;
            if (s_active && known >= 0) return known;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var me = await SocialApiService.GetMeAsync(token, cancellationToken);
            if (me.Status != SocialApiStatus.Ok) return -1;

            await ProfileService.OfferRefreshedProfileAsync(token, me.Value.Profile);
            return me.Value.UnreadCount;
        }

        public static void OfferUnreadCount(string accountId, int? count)
        {
            if (!count.HasValue || !s_active || !IsActiveAccount(accountId)) return;

            var previous = s_unreadCount;
            ApplyUnreadCount(count.Value);

            if (count.Value > 0 && count.Value != previous)
            {
                RefreshTileInBackground();
            }
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchMentionsAsync(
            string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetMentionsAsync(token, untilId, AppConstants.SocialFeedPageSize, cancellationToken);
        }

        public static async Task MarkAllReadAsync()
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.GetNotificationsAsync(token, null, 1, true, CancellationToken.None);
                ObservePermission(result.Status);
                if (result.Status == SocialApiStatus.Ok)
                {
                    ApplyUnreadCount(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: mark read failed - {ex.Message}");
            }
        }

        private static async Task ProbeNotificationsPermissionAsync()
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                var probe = await SocialApiService.GetNotificationsAsync(token, null, 1, false, CancellationToken.None);
                ObservePermission(probe.Status);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: permission probe failed - {ex.Message}");
            }
        }

        private static void ObservePermission(SocialApiStatus notificationsStatus)
        {
            if (notificationsStatus == SocialApiStatus.Ok)
            {
                SetNeedsSignInAgain(false);
                WriteStoredString(AppConstants.SettingSocialSignInAgainDismissed, null);
            }
            else if (notificationsStatus == SocialApiStatus.PermissionDenied)
            {
                SetNeedsSignInAgain(true);
            }
        }

        private static void SetNeedsSignInAgain(bool value)
        {
            lock (s_lock)
            {
                if (s_needsSignInAgain == value) return;
                s_needsSignInAgain = value;
            }

            NeedsSignInAgainChanged?.Invoke(null, EventArgs.Empty);
        }

        #region Background check

        public static bool IsBackgroundCheck(IBackgroundTaskInstance instance)
        {
            return instance != null && instance.Task != null &&
                   string.Equals(instance.Task.Name, AppConstants.SocialCheckTaskName, StringComparison.Ordinal);
        }

        public static async Task RunBackgroundCheckAsync(IBackgroundTaskInstance instance)
        {
            var deferral = instance.GetDeferral();
            using (var cts = new CancellationTokenSource(BackgroundBudget))
            {
                BackgroundTaskCanceledEventHandler onCanceled = (sender, reason) =>
                {
                    Debug.WriteLine($"SocialNotificationService: background check cancelled - {reason}");
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        Debug.WriteLine("SocialNotificationService: the check had already finished when it was cancelled");
                    }
                };
                instance.Canceled += onCanceled;

                try
                {
                    if (Enabled && BackgroundCheckEnabled && ReadActiveAccount() != null)
                    {
                        await RunCheckAsync(cts.Token);
                    }
                    else
                    {
                        UnregisterBackgroundCheck();
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("SocialNotificationService: background check ran out of time");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialNotificationService: background check failed - {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    instance.Canceled -= onCanceled;
                    deferral.Complete();
                }
            }
        }

        private static async Task EnsureBackgroundCheckAsync()
        {
            try
            {
                RemoveAccessAfterUpdate();

                var status = await BackgroundExecutionManager.RequestAccessAsync();
                BackgroundAccessDenied = status == BackgroundAccessStatus.DeniedByUser ||
                                         status == BackgroundAccessStatus.DeniedBySystemPolicy;
                if (BackgroundAccessDenied)
                {
                    Debug.WriteLine($"SocialNotificationService: background access is {status}");
                    return;
                }

                var minutes = BackgroundCheckMinutes;
                if (FindBackgroundCheck() != null)
                {
                    if (ReadRegisteredMinutes() == minutes) return;
                    UnregisterBackgroundCheck();
                }

                BackgroundTaskBuilder builder = new BackgroundTaskBuilder();
                builder.Name = AppConstants.SocialCheckTaskName;
                builder.IsNetworkRequested = true;
                builder.SetTrigger(new TimeTrigger(minutes, false));
                builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
                builder.Register();

                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialCheckRegisteredMinutes] = minutes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not register the background check - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void RemoveAccessAfterUpdate()
        {
            var version = Package.Current.Id.Version;
            var stamp = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

            var values = ApplicationData.Current.LocalSettings.Values;
            if (string.Equals(values[AppConstants.SettingBackgroundAccessVersion] as string, stamp, StringComparison.Ordinal))
            {
                return;
            }

            BackgroundExecutionManager.RemoveAccess();
            values[AppConstants.SettingBackgroundAccessVersion] = stamp;
        }

        private static uint BackgroundCheckMinutes
        {
            get
            {
                var minutes = ProfileService.AutoRefreshEnabled ? ProfileService.AutoRefreshMinutes : 0;
                return (uint)Math.Max(AppConstants.SocialCheckMinimumMinutes, minutes);
            }
        }

        private static uint ReadRegisteredMinutes()
        {
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialCheckRegisteredMinutes];
                return stored == null
                       ? (uint)AppConstants.SocialCheckMinimumMinutes
                       : Convert.ToUInt32(stored, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not read the check interval - {ex.Message}");
                return 0;
            }
        }

        private static IBackgroundTaskRegistration FindBackgroundCheck()
        {
            foreach (var registration in BackgroundTaskRegistration.AllTasks.Values)
            {
                if (string.Equals(registration.Name, AppConstants.SocialCheckTaskName, StringComparison.Ordinal))
                {
                    return registration;
                }
            }

            return null;
        }

        private static void UnregisterBackgroundCheck()
        {
            try
            {
                foreach (var registration in BackgroundTaskRegistration.AllTasks.Values.ToList())
                {
                    if (string.Equals(registration.Name, AppConstants.SocialCheckTaskName, StringComparison.Ordinal))
                    {
                        registration.Unregister(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not unregister the background check - {ex.Message}");
            }
        }

        private static async Task RunCheckAsync(CancellationToken cancellationToken)
        {
            if (!await s_checkGate.WaitAsync(0)) return;

            try
            {
                var account = ReadActiveAccount();
                if (account == null) return;

                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                var me = await SocialApiService.GetMeAsync(token, cancellationToken);
                if (me.Status != SocialApiStatus.Ok || !IsActiveAccount(account)) return;

                var count = me.Value.UnreadCount;
                ApplyUnreadCount(count);
                await ProfileService.OfferRefreshedProfileAsync(token, me.Value.Profile);

                if (!ReadWatermark().HasValue)
                {
                    await EstablishBaselineAsync(token, account, cancellationToken);
                    await RefreshTileAsync(token, account, count, cancellationToken);
                    return;
                }

                if (count == 0) return;

                var unread = await SocialApiService.GetNotificationsAsync(token, null,
                                                                          Math.Min(count, AppConstants.SocialFeedPageSize),
                                                                          false, cancellationToken);
                if (!IsActiveAccount(account)) return;

                ObservePermission(unread.Status);
                if (unread.Status != SocialApiStatus.Ok) return;

                await ToastUnclaimedAsync(unread.Value, account, cancellationToken);
                if (!IsActiveAccount(account)) return;

                await SocialTileService.ShowAsync(unread.Value, count, cancellationToken);
            }
            finally
            {
                s_checkGate.Release();
            }
        }

        private static async Task ToastUnclaimedAsync(IReadOnlyList<SocialNotification> unread, string account,
                                                      CancellationToken cancellationToken)
        {
            var fresh = UnclaimedForToast(unread);
            if (fresh.Count == 0) return;

            if (fresh.Count <= AppConstants.SocialToastIndividualLimit)
            {
                foreach (var notification in fresh)
                {
                    if (!IsActiveAccount(account)) return;

                    if (TryClaimForToast(notification))
                    {
                        await ShowToastAsync(notification, cancellationToken);
                    }
                }
            }
            else if (ClaimForToast(fresh) > 0)
            {
                ShowSummaryToast(fresh.Count);
            }
        }

        private static async Task RefreshTileAsync(string token, string account, int count, CancellationToken cancellationToken)
        {
            if (count <= 0 || !SocialTileService.WantsPreviews) return;

            var newest = await SocialApiService.GetNotificationsAsync(token, null,
                                                                      Math.Min(count, AppConstants.SocialTilePreviewLimit),
                                                                      false, cancellationToken);
            if (!IsActiveAccount(account)) return;

            ObservePermission(newest.Status);
            if (newest.Status != SocialApiStatus.Ok) return;

            await SocialTileService.ShowAsync(newest.Value, count, cancellationToken);
        }

        private static async Task EstablishBaselineAsync(string token, string account, CancellationToken cancellationToken)
        {
            var newest = await SocialApiService.GetNotificationsAsync(token, null, 1, false, cancellationToken);
            if (!IsActiveAccount(account)) return;

            ObservePermission(newest.Status);
            if (newest.Status != SocialApiStatus.Ok) return;

            lock (s_lock)
            {
                if (ReadWatermark().HasValue || !IsActiveAccount(account)) return;
                WriteWatermark(newest.Value.Count > 0 ? newest.Value[0].CreatedAt : DateTimeOffset.MinValue);
            }
        }

        #endregion

        #region Toasts, badge and read state

        private static async Task RefreshUnreadCountAsync(CancellationToken cancellationToken)
        {
            try
            {
                var account = ReadActiveAccount();
                if (account == null) return;

                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                var me = await SocialApiService.GetMeAsync(token, cancellationToken);
                if (me.Status == SocialApiStatus.Ok && s_active && IsActiveAccount(account))
                {
                    ApplyUnreadCount(me.Value.UnreadCount);
                    await ProfileService.OfferRefreshedProfileAsync(token, me.Value.Profile);
                    await RefreshTileAsync(token, account, me.Value.UnreadCount, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: unread count refresh failed - {ex.Message}");
            }
        }

        private static async void ScheduleCountRefresh()
        {
            try
            {
                CancellationToken token;
                lock (s_lock)
                {
                    token = s_countDebounce.Restart();
                }

                await Task.Delay(CountRefreshDelay, token);
                await RefreshUnreadCountAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialNotificationService: count refresh superseded by a newer one");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: scheduled count refresh failed - {ex.Message}");
            }
        }

        private static void ApplyUnreadCount(int count)
        {
            s_unreadCount = count;

            if (OwnsBadge)
            {
                LiveTileService.UpdateBadge(count);
            }

            if (count == 0)
            {
                LiveTileService.RemoveToastGroup(AppConstants.SocialToastGroup);
                SocialTileService.Withdraw();
            }
        }

        private static void ClearState()
        {
            s_unreadCount = -1;

            lock (s_lock)
            {
                s_countDebounce.Cancel();
                ApplicationData.Current.LocalSettings.Values.Remove(AppConstants.SettingSocialToastedThrough);
                ApplicationData.Current.LocalSettings.Values.Remove(AppConstants.SettingSocialToastedIds);
            }

            LiveTileService.ClearBadge();
            LiveTileService.RemoveToastGroup(AppConstants.SocialToastGroup);
        }

        private static bool TryClaimForToast(SocialNotification notification)
        {
            return ClaimForToast(new[] { notification }) > 0;
        }

        private static int ClaimForToast(IReadOnlyList<SocialNotification> notifications)
        {
            lock (s_lock)
            {
                var floor = ReadWatermark();
                var toasted = ReadToasted();

                var claimed = notifications.Where(n => IsUnclaimed(n, floor, toasted)).ToList();
                if (claimed.Count == 0) return 0;

                foreach (var notification in claimed)
                {
                    toasted[ToastKeyFor(notification)] = notification.CreatedAt.UtcTicks;
                }

                var newFloor = floor ?? claimed.Min(n => n.CreatedAt);
                while (toasted.Count > AppConstants.SocialToastedIdLimit)
                {
                    var oldest = toasted.OrderBy(entry => entry.Value).First();
                    toasted.Remove(oldest.Key);

                    var oldestAt = new DateTimeOffset(oldest.Value, TimeSpan.Zero);
                    if (oldestAt > newFloor) newFloor = oldestAt;
                }

                if (newFloor != floor)
                {
                    WriteWatermark(newFloor);
                }
                WriteToasted(toasted);

                return claimed.Count;
            }
        }

        private static List<SocialNotification> UnclaimedForToast(IEnumerable<SocialNotification> notifications)
        {
            lock (s_lock)
            {
                var floor = ReadWatermark();
                var toasted = ReadToasted();

                return notifications.Where(n => IsUnclaimed(n, floor, toasted))
                                    .OrderBy(n => n.CreatedAt)
                                    .ToList();
            }
        }

        private static bool IsUnclaimed(SocialNotification notification, DateTimeOffset? floor, Dictionary<string, long> toasted)
        {
            if (floor.HasValue && notification.CreatedAt <= floor.Value) return false;

            return !toasted.ContainsKey(ToastKeyFor(notification));
        }

        private static string ToastKeyFor(SocialNotification notification)
        {
            return notification.Id.Length > MaximumToastTagLength
                   ? notification.Id.Substring(0, MaximumToastTagLength)
                   : notification.Id;
        }

        private static Dictionary<string, long> ReadToasted()
        {
            var toasted = new Dictionary<string, long>(StringComparer.Ordinal);
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialToastedIds] as ApplicationDataCompositeValue;
                if (stored == null) return toasted;

                foreach (var entry in stored)
                {
                    var ticks = Convert.ToInt64(entry.Value, CultureInfo.InvariantCulture);
                    if (ticks >= 0 && ticks <= DateTimeOffset.MaxValue.UtcTicks)
                    {
                        toasted[entry.Key] = ticks;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not read the toasted notifications - {ex.Message}");
            }

            return toasted;
        }

        private static void WriteToasted(Dictionary<string, long> toasted)
        {
            var stored = new ApplicationDataCompositeValue();
            foreach (var entry in toasted)
            {
                stored[entry.Key] = entry.Value;
            }

            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialToastedIds] = stored;
        }

        private static DateTimeOffset? ReadWatermark()
        {
            try
            {
                var raw = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialToastedThrough] as string;
                DateTimeOffset parsed;
                if (!string.IsNullOrEmpty(raw) &&
                    DateTimeOffset.TryParseExact(raw, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not read the toast watermark - {ex.Message}");
            }

            return null;
        }

        private static void WriteWatermark(DateTimeOffset value)
        {
            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialToastedThrough] =
                value.ToString("o", CultureInfo.InvariantCulture);
        }

        private static string AccountOf(UserProfile user)
        {
            return user.UserId ?? string.Empty;
        }

        private static string ReadActiveAccount()
        {
            return ReadStoredString(AppConstants.SettingSocialNotificationsAccount);
        }

        private static bool IsActiveAccount(string account)
        {
            return account != null && string.Equals(ReadActiveAccount(), account, StringComparison.Ordinal);
        }

        private static string ReadStoredString(string key)
        {
            try
            {
                return ApplicationData.Current.LocalSettings.Values[key] as string;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not read {key} - {ex.Message}");
                return null;
            }
        }

        private static void WriteStoredString(string key, string value)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (value == null)
                {
                    values.Remove(key);
                }
                else
                {
                    values[key] = value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: could not write {key} - {ex.Message}");
            }
        }

        private static async Task ShowToastAsync(SocialNotification notification, CancellationToken cancellationToken)
        {
            var item = SocialFeedItem.FromNotification(notification, true);
            if (item == null) return;

            Uri logo = null;
            if (notification.User != null)
            {
                logo = await AvatarIconService.GetToastAvatarUriAsync(notification.User.AvatarUrl, cancellationToken);
            }

            LiveTileService.ShowGroupedToast(new GroupedToast
            {
                Title = item.Title,
                Body = item.IsDirect ? LocalizedStrings.Get("SocialToastDirectNoteBody") : item.Body,
                Attribution = LocalizedStrings.Get("SocialToastAttribution"),
                AppLogo = logo,
                Timestamp = notification.CreatedAt,
                Group = AppConstants.SocialToastGroup,
                Tag = ToastKeyFor(notification),
                ArgumentKey = AppConstants.ToastArgumentOpen,
                ArgumentValue = AppConstants.ToastOpenNotifications
            });
        }

        private static void ShowSummaryToast(int count)
        {
            LiveTileService.ShowGroupedToast(new GroupedToast
            {
                Title = LocalizedStrings.Get("SocialToastSummaryTitle"),
                Body = LocalizedStrings.Format("SocialToastSummaryBodyFormat", count),
                Attribution = LocalizedStrings.Get("SocialToastAttribution"),
                Group = AppConstants.SocialToastGroup,
                Tag = "summary",
                ArgumentKey = AppConstants.ToastArgumentOpen,
                ArgumentValue = AppConstants.ToastOpenNotifications
            });
        }

        private static async void ToastFromStream(SocialNotification notification)
        {
            try
            {
                if (TryClaimForToast(notification))
                {
                    await ShowToastAsync(notification, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: live toast failed - {ex.Message}");
            }
        }

        #endregion

        #region Live stream

        private static void StartStream()
        {
            int generation;
            lock (s_lock)
            {
                if (s_stream != null) return;
                generation = ++s_streamGeneration;
            }

            ConnectStream(generation);
        }

        private static void StopStream()
        {
            SocialStream stream;
            lock (s_lock)
            {
                stream = s_stream;
                s_stream = null;
                s_streamGeneration++;
                s_reconnectDelay = MinimumReconnectDelay;
            }

            stream?.Dispose();
        }

        private static async void ConnectStream(int generation)
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                var stream = new SocialStream();
                lock (s_lock)
                {
                    if (generation != s_streamGeneration || s_stream != null || !s_active || s_suspended)
                    {
                        return;
                    }
                    s_stream = stream;
                }

                stream.MessageReceived += OnStreamMessage;
                stream.Closed += OnStreamClosed;

                if (!await stream.ConnectAsync(token, CancellationToken.None)) return;

                lock (s_lock)
                {
                    s_reconnectDelay = MinimumReconnectDelay;
                }

                await RunCheckAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: stream connect failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void OnStreamClosed(object sender, EventArgs e)
        {
            var stream = sender as SocialStream;
            if (stream == null) return;

            stream.MessageReceived -= OnStreamMessage;
            stream.Closed -= OnStreamClosed;

            int generation;
            TimeSpan delay;
            lock (s_lock)
            {
                if (s_stream != stream) return;
                s_stream = null;
                generation = s_streamGeneration;

                delay = s_reconnectDelay;
                var doubled = TimeSpan.FromTicks(s_reconnectDelay.Ticks * 2);
                s_reconnectDelay = doubled > MaximumReconnectDelay ? MaximumReconnectDelay : doubled;
            }

            if (stream.Unauthorized)
            {
                Debug.WriteLine("SocialNotificationService: the stream refused the token; not reconnecting");
                return;
            }

            ReconnectAfter(delay, generation);
        }

        private static async void ReconnectAfter(TimeSpan delay, int generation)
        {
            try
            {
                await Task.Delay(delay);

                lock (s_lock)
                {
                    if (generation != s_streamGeneration || s_stream != null) return;
                }

                ConnectStream(generation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotificationService: reconnect failed - {ex.Message}");
            }
        }

        private static void OnStreamMessage(object sender, SocialStreamEventArgs e)
        {
            switch (e.Type)
            {
                case "notification":
                    {
                        var notification = SocialNotification.FromJson(e.Body);
                        if (notification != null)
                        {
                            NotificationArrived?.Invoke(null, notification);
                        }
                        break;
                    }
                case "unreadNotification":
                    {
                        var notification = SocialNotification.FromJson(e.Body);
                        if (notification != null)
                        {
                            ToastFromStream(notification);
                        }
                        ScheduleCountRefresh();
                        break;
                    }
                case "readAllNotifications":
                case "notificationFlushed":
                    ApplyUnreadCount(0);
                    break;
            }
        }

        #endregion
    }
}
