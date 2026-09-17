using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    public class ProfileService
    {
        public static UserProfile CurrentUser { get; set; }

        public static event EventHandler<bool> AuthStateChanged;

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

            CurrentUser = result.Profile;
            await LocalStorageService.SaveProfileAsync(result.Profile);
            AuthStateChanged?.Invoke(null, true);

            LiveTileService.SendToast(LocalizedStrings.Get("SignInToastTitle"),
                                      LocalizedStrings.Format("SignInToastBodyFormat", DisplayNameOf(result.Profile)));

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

                    CurrentUser = fetched.Profile;
                    await LocalStorageService.SaveProfileAsync(fetched.Profile);
                    AuthStateChanged?.Invoke(null, true);
                    return true;
                }

                CurrentUser = cached;
                AuthStateChanged?.Invoke(null, true);

                RefreshCurrentUserInBackground(token, cached.LastLoginDate);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryRestoreSessionAsync failed: {ex}");
                return false;
            }
        }

        private static async void RefreshCurrentUserInBackground(string token, DateTime lastLoginDate)
        {
            try
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

                if (HasSameAccountDetails(CurrentUser, fetched.Profile)) return;

                fetched.Profile.LastLoginDate = lastLoginDate;
                CurrentUser = fetched.Profile;
                await LocalStorageService.SaveProfileAsync(fetched.Profile);
                AuthStateChanged?.Invoke(null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshCurrentUserInBackground failed: {ex.Message}");
            }
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
