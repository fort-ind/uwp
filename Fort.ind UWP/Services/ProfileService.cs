using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public class ProfileService
    {
        public static UserProfile CurrentUser { get; set; }

        public static event EventHandler<bool> AuthStateChanged;

        private static readonly object s_refreshLock = new object();

        private static Task s_refreshInFlight;

        private static string s_refreshInFlightToken;

        private static DateTime? s_lastRefreshUtc;

        private const int MaximumAutoRefreshMinutes = 24 * 60;

        public static bool AutoRefreshEnabled
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingProfileAutoRefresh];
                    if (stored == null) return true;
                    return Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProfileService: AutoRefreshEnabled read failed - {ex.Message}");
                    return true;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingProfileAutoRefresh] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProfileService: AutoRefreshEnabled write failed - {ex.Message}");
                }
            }
        }

        public static int AutoRefreshMinutes
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingProfileRefreshMinutes];
                    if (stored == null) return AppConstants.DefaultProfileRefreshMinutes;

                    var minutes = Convert.ToInt32(stored);
                    return minutes >= 0 && minutes <= MaximumAutoRefreshMinutes
                           ? minutes
                           : AppConstants.DefaultProfileRefreshMinutes;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProfileService: AutoRefreshMinutes read failed - {ex.Message}");
                    return AppConstants.DefaultProfileRefreshMinutes;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingProfileRefreshMinutes] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProfileService: AutoRefreshMinutes write failed - {ex.Message}");
                }
            }
        }

        public static async Task<LoginResult> LoginWithMisskeyAsync()
        {
            var result = await MisskeyAuthService.SignInAsync();
            if (!result.Success)
            {
                return new LoginResult(false, result.ErrorMessage);
            }

            await ApplySignInResultAsync(result);
            return new LoginResult(true, null, result.Profile);
        }

        public static async Task<bool> ApplySignInResultAsync(MisskeyAuthResult result)
        {
            if (result == null || !result.Success) return false;

            s_lastRefreshUtc = null;
            CurrentUser = result.Profile;
            await LocalStorageService.SaveProfileAsync(result.Profile);
            AuthStateChanged?.Invoke(null, true);

            LiveTileService.SendToast(LocalizedStrings.Get("SignInToastTitle"),
                                      LocalizedStrings.Format("SignInToastBodyFormat", DisplayNameOf(result.Profile)));

            if (!result.Profile.FollowersCount.HasValue || !result.Profile.FollowingCount.HasValue)
            {
                RefreshCurrentUserInBackground(result.Token);
            }

            return true;
        }

        public static Task LogoutAsync()
        {
            return LogoutAsync(false);
        }

        private static async Task LogoutAsync(bool tokenRejected)
        {
            var name = CurrentUser != null ? DisplayNameOf(CurrentUser) : "";
            CurrentUser = null;
            s_lastRefreshUtc = null;
            MisskeyAuthService.ClearToken();
            await LocalStorageService.ClearProfileAsync();
            AuthStateChanged?.Invoke(null, false);

            if (tokenRejected)
            {
                LiveTileService.SendToast(LocalizedStrings.Get("SessionExpiredToastTitle"),
                                          LocalizedStrings.Get("SessionExpiredToastBody"));
            }
            else if (!string.IsNullOrEmpty(name))
            {
                LiveTileService.SendToast(LocalizedStrings.Get("SignOutToastTitle"),
                                          LocalizedStrings.Format("SignOutToastBodyFormat", name));
            }
        }

        private static string DisplayNameOf(UserProfile profile)
        {
            return string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Username : profile.DisplayName;
        }

        public static async Task ResetAppDataAsync()
        {
            MisskeyAuthService.ClearToken();
            CurrentUser = null;
            s_lastRefreshUtc = null;
            LiveTileService.ClearTile();
            LiveTileService.ClearBadge();
            await LocalStorageService.ResetAllAppDataAsync();

            AvatarIconService.InvalidateCache();

            FavoritesService.ResetForAppDataWipe();

            AuthStateChanged?.Invoke(null, false);
        }

        public static async Task<bool> TryRestoreSessionAsync()
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                var cached = await LocalStorageService.LoadProfileAsync();
                if (cached == null)
                {
                    var fetched = await MisskeyAuthService.FetchCurrentUserAsync(token);
                    if (fetched.Profile == null)
                    {
                        if (fetched.TokenRejected)
                        {
                            await LogoutAsync(true);
                        }
                        return false;
                    }

                    s_lastRefreshUtc = DateTime.UtcNow;
                    CurrentUser = fetched.Profile;
                    await LocalStorageService.SaveProfileAsync(fetched.Profile);
                    AuthStateChanged?.Invoke(null, true);
                    return true;
                }

                CurrentUser = cached;
                AuthStateChanged?.Invoke(null, true);

                RefreshCurrentUserInBackground(token);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryRestoreSessionAsync failed: {ex}");
                return false;
            }
        }

        public static async void RefreshOnProfileVisit()
        {
            try
            {
                if (CurrentUser == null || !AutoRefreshEnabled) return;

                var last = s_lastRefreshUtc;
                if (last.HasValue && DateTime.UtcNow - last.Value < TimeSpan.FromMinutes(AutoRefreshMinutes))
                {
                    return;
                }

                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                await RefreshCurrentUserAsync(token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfileService: profile visit refresh failed - {ex.Message}");
            }
        }

        private static async void RefreshCurrentUserInBackground(string token)
        {
            try
            {
                await RefreshCurrentUserAsync(token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshCurrentUserInBackground failed: {ex.Message}");
            }
        }

        private static Task RefreshCurrentUserAsync(string token)
        {
            lock (s_refreshLock)
            {
                if (s_refreshInFlight == null
                    || s_refreshInFlight.IsCompleted
                    || !string.Equals(s_refreshInFlightToken, token, StringComparison.Ordinal))
                {
                    s_refreshInFlightToken = token;
                    s_refreshInFlight = RefreshCurrentUserCoreAsync(token);
                }

                return s_refreshInFlight;
            }
        }

        private static async Task RefreshCurrentUserCoreAsync(string token)
        {
            var fetched = await MisskeyAuthService.FetchCurrentUserAsync(token);

            if (!string.Equals(await MisskeyAuthService.TryGetTokenAsync(), token, StringComparison.Ordinal))
            {
                return;
            }

            if (fetched.TokenRejected)
            {
                Debug.WriteLine("ProfileService: stored token was rejected; signing out");
                await LogoutAsync(true);
                return;
            }

            if (fetched.Profile == null) return;

            var current = CurrentUser;
            if (current == null) return;

            s_lastRefreshUtc = DateTime.UtcNow;

            if (HasSameAccountDetails(current, fetched.Profile)) return;

            fetched.Profile.LastLoginDate = current.LastLoginDate;
            CurrentUser = fetched.Profile;
            await LocalStorageService.SaveProfileAsync(fetched.Profile);
            AuthStateChanged?.Invoke(null, true);
        }

        private static bool HasSameAccountDetails(UserProfile current, UserProfile fetched)
        {
            if (current == null || fetched == null) return false;

            return string.Equals(current.UserId, fetched.UserId, StringComparison.Ordinal)
                   && string.Equals(current.Username, fetched.Username, StringComparison.Ordinal)
                   && string.Equals(current.Host, fetched.Host, StringComparison.Ordinal)
                   && string.Equals(current.DisplayName, fetched.DisplayName, StringComparison.Ordinal)
                   && string.Equals(current.Bio, fetched.Bio, StringComparison.Ordinal)
                   && string.Equals(current.AvatarUrl, fetched.AvatarUrl, StringComparison.Ordinal)
                   && string.Equals(current.BannerUrl, fetched.BannerUrl, StringComparison.Ordinal)
                   && string.Equals(current.BannerBlurhash, fetched.BannerBlurhash, StringComparison.Ordinal)
                   && current.FollowersCount == fetched.FollowersCount
                   && current.FollowingCount == fetched.FollowingCount
                   && current.CreatedDate.Ticks == fetched.CreatedDate.Ticks;
        }
    }

    public class LoginResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public UserProfile Profile { get; set; }

        public LoginResult(bool success, string message, UserProfile profile = null)
        {
            this.Success = success;
            this.Message = message;
            this.Profile = profile;
        }
    }
}
