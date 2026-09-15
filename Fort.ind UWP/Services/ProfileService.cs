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

        /// <param name="tokenRejected">
        /// True when the instance refused the stored token, rather than the user choosing to sign
        /// out. The toast then says the session ended instead of saying goodbye - nobody asked to
        /// leave, and a farewell out of nowhere reads as the app misbehaving.
        /// </param>
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

            // The wipe deletes the cached avatar PNG, but AvatarIconService remembers its URI in a
            // static that outlives the reset - so signing back in with the same avatar handed the
            // nav item an ms-appdata URI pointing at a file that no longer exists.
            AvatarIconService.InvalidateCache();

            // Same class of problem: the favorites file is gone with the rest of LocalFolder, but
            // the in-memory set behind it is static and would survive.
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
                        // Only discard the token when the instance actually refused it. This used
                        // to sign the user out on any null, so launching with no network and no
                        // cached profile threw away a perfectly good token and made them sign in
                        // again; now the session simply stays unrestored until the next launch.
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

        /// <param name="token">The token the session was restored with.</param>
        /// <param name="lastLoginDate">
        /// Carried over from the cached profile. /api/i describes the account, not this app's
        /// sign-in, so the refreshed profile has no sign-in time of its own to offer.
        /// </param>
        private static async void RefreshCurrentUserInBackground(string token, DateTime lastLoginDate)
        {
            try
            {
                var fetched = await MisskeyAuthService.FetchCurrentUserAsync(token);

                // Check the token is still the live one before acting on anything: the user may
                // have signed out, or signed in as someone else, while this was in flight.
                if (!string.Equals(await MisskeyAuthService.TryGetTokenAsync(), token, StringComparison.Ordinal))
                {
                    return;
                }

                if (fetched.TokenRejected)
                {
                    // The instance says this token is gone - revoked from fort.social's settings,
                    // or the account was deleted. The cached profile would otherwise keep the app
                    // looking signed in indefinitely against credentials that can never work, with
                    // every request silently failing behind a perfectly normal-looking UI.
                    Debug.WriteLine("ProfileService: stored token was rejected; signing out");
                    await LogoutAsync(true);
                    return;
                }

                if (fetched.Profile == null) return;

                // The usual answer is "nothing changed", and acting on it anyway rewrote the cache
                // file and raised AuthStateChanged on every launch - repainting the nav item,
                // Settings > Data storage and ProfilePage for a profile identical to the one they
                // were already showing.
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

        /// <summary>
        /// True when <paramref name="fetched"/> carries nothing <paramref name="current"/> does not
        /// already show - i.e. every field /api/i supplies is unchanged.
        /// </summary>
        /// <remarks>
        /// LastLoginDate and Preferences are deliberately not compared: the instance supplies
        /// neither. CreatedDate compares by Ticks because the cache's JSON round trip can change a
        /// DateTime's Kind but not its instant. A false mismatch costs nothing but the save and
        /// event this exists to skip.
        /// </remarks>
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
