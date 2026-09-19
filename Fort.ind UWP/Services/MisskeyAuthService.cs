using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Credentials;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public class MisskeyAuthService
    {
        public const string InstanceHost = "social.fort1nd.com";
        private const string AppName = "Fort.ind";
        private const string RequestedPermissions = "read:account";

        private const string VaultResource = "Fort.ind.Misskey";
        private const string VaultUsernameKey = "token";

        private const string CallbackScheme = "fortind";
        private const string CallbackHost = "miauth-callback";
        private const string CallbackSessionParam = "session";

        private const string PendingSessionSettingKey = "MisskeyAuth.PendingSession";
        private const string PendingSessionIssuedAtSettingKey = "MisskeyAuth.PendingSessionIssuedAtUtc";
        private static readonly TimeSpan PendingSessionExpiry = TimeSpan.FromMinutes(10);

        private static readonly object s_lock = new object();
        private static string s_pendingSession = null;
        private static TaskCompletionSource<bool> s_pendingCompletion = null;

        private static string s_lastHandledSession = null;

        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(() => new HttpClient());

        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

        public static async Task<MisskeyAuthResult> SignInAsync()
        {
            var session = Guid.NewGuid().ToString();
            Uri callbackUri = new Uri($"{CallbackScheme}://{CallbackHost}?{CallbackSessionParam}={session}");

            Uri startUri = new Uri(
                $"https://{InstanceHost}/miauth/{session}" +
                $"?name={Uri.EscapeDataString(AppName)}" +
                $"&callback={Uri.EscapeDataString(callbackUri.ToString())}" +
                $"&permission={RequestedPermissions}");

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            TaskCompletionSource<bool> superseded;
            lock (s_lock)
            {
                superseded = s_pendingCompletion;
                s_pendingSession = session;
                s_pendingCompletion = completion;
            }
            PersistPendingSession(session);

            superseded?.TrySetResult(false);

            try
            {
                var launched = await Windows.System.Launcher.LaunchUriAsync(startUri);
                if (!launched)
                {
                    ClearPending(session);
                    return MisskeyAuthResult.Failed("SignInErrorBrowserLaunch");
                }
            }
            catch (Exception ex)
            {
                ClearPending(session);
                Debug.WriteLine($"MisskeyAuthService: launch failed - {ex.Message}");
                return MisskeyAuthResult.Failed("SignInErrorBrowserLaunch");
            }

            Task finished = null;
            using (var timeoutCts = new CancellationTokenSource())
            {
                try
                {
                    finished = await Task.WhenAny(completion.Task, Task.Delay(SignInTimeout, timeoutCts.Token));
                }
                finally
                {
                    timeoutCts.Cancel();
                }
            }

            if (finished != completion.Task)
            {
                ClearPending(session);
                return MisskeyAuthResult.Failed("SignInErrorTimedOut");
            }

            var approved = await completion.Task;
            if (!approved)
            {
                return MisskeyAuthResult.Failed("SignInErrorCancelled");
            }

            return await CompleteSessionAsync(session);
        }

        public static async Task<MisskeyAuthResult> HandleProtocolActivationAsync(Uri uri)
        {
            if (uri == null || !string.Equals(uri.Host, CallbackHost, StringComparison.OrdinalIgnoreCase))
            {
                return MisskeyAuthResult.Failed("SignInErrorUnrecognizedCallback");
            }

            var session = ExtractSessionFromCallback(uri);
            if (string.IsNullOrWhiteSpace(session))
            {
                return MisskeyAuthResult.Failed("SignInErrorMissingSession");
            }

            TaskCompletionSource<bool> completion = null;
            bool alreadyHandled = false;
            lock (s_lock)
            {
                if (s_pendingCompletion != null && string.Equals(s_pendingSession, session, StringComparison.Ordinal))
                {
                    completion = s_pendingCompletion;
                    s_pendingSession = null;
                    s_pendingCompletion = null;
                    s_lastHandledSession = session;
                }
                else if (string.Equals(s_lastHandledSession, session, StringComparison.Ordinal))
                {
                    alreadyHandled = true;
                }
            }

            if (alreadyHandled)
            {
                Debug.WriteLine("MisskeyAuthService: ignoring a repeat callback for a session already handled");
                return null;
            }

            if (completion != null)
            {
                ClearPersistedSession(session);
                completion.TrySetResult(true);
                return null;
            }

            if (!TryConsumePersistedSession(session))
            {
                return MisskeyAuthResult.Failed("SignInErrorInvalidLink");
            }

            lock (s_lock)
            {
                s_lastHandledSession = session;
            }

            return await CompleteSessionAsync(session);
        }

        public static void CancelPendingSignIn()
        {
            TaskCompletionSource<bool> completion = null;
            lock (s_lock)
            {
                completion = s_pendingCompletion;
                s_pendingSession = null;
                s_pendingCompletion = null;
            }
            ClearPersistedSession();
            completion?.TrySetResult(false);
        }

        private static void ClearPending(string session)
        {
            lock (s_lock)
            {
                if (s_pendingSession == session)
                {
                    s_pendingSession = null;
                    s_pendingCompletion = null;
                }
            }

            ClearPersistedSession(session);
        }

        private static void PersistPendingSession(string session)
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            values[PendingSessionSettingKey] = session;
            values[PendingSessionIssuedAtSettingKey] = DateTimeOffset.UtcNow.ToString("o");
        }

        private static bool TryConsumePersistedSession(string session)
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            var storedSession = values[PendingSessionSettingKey] as string;
            var storedIssuedAtRaw = values[PendingSessionIssuedAtSettingKey] as string;

            if (string.IsNullOrEmpty(storedSession) || string.IsNullOrEmpty(storedIssuedAtRaw))
            {
                return false;
            }
            if (!string.Equals(storedSession, session, StringComparison.Ordinal))
            {
                return false;
            }

            DateTimeOffset issuedAt;
            if (!DateTimeOffset.TryParse(storedIssuedAtRaw, System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.RoundtripKind, out issuedAt))
            {
                return false;
            }
            if (DateTimeOffset.UtcNow - issuedAt > PendingSessionExpiry)
            {
                return false;
            }

            ClearPersistedSession();
            return true;
        }

        private static void ClearPersistedSession()
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            values.Remove(PendingSessionSettingKey);
            values.Remove(PendingSessionIssuedAtSettingKey);
        }

        private static void ClearPersistedSession(string session)
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            var stored = values[PendingSessionSettingKey] as string;
            if (stored != null && !string.Equals(stored, session, StringComparison.Ordinal))
            {
                return;
            }

            ClearPersistedSession();
        }

        private static string ExtractSessionFromCallback(Uri uri)
        {
            try
            {
                if (uri == null || string.IsNullOrEmpty(uri.Query)) return null;
                Windows.Foundation.WwwFormUrlDecoder decoder = new Windows.Foundation.WwwFormUrlDecoder(uri.Query);
                foreach (var entry in decoder)
                {
                    if (string.Equals(entry.Name, CallbackSessionParam, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: failed to parse callback URI - {ex.Message}");
            }
            return null;
        }

        private static async Task<MisskeyAuthResult> CompleteSessionAsync(string session)
        {
            try
            {
                Uri checkUri = new Uri($"https://{InstanceHost}/api/miauth/{Uri.EscapeDataString(session)}/check");
                using (HttpStringContent content = new HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json"))
                {
                    using (var response = await s_client.Value.PostAsync(checkUri, content))
                    {
                        response.EnsureSuccessStatusCode();

                        var body = await response.Content.ReadAsStringAsync();
                        var json = JsonObject.Parse(body);

                        if (!json.GetNamedBoolean("ok", false))
                        {
                            return MisskeyAuthResult.Failed("SignInErrorNotApproved");
                        }

                        var token = json.GetNamedString("token", "");
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            return MisskeyAuthResult.Failed("SignInErrorNoToken");
                        }

                        var profile = ParseUser(GetNamedObjectOrNull(json, "user"));
                        if (profile == null)
                        {
                            return MisskeyAuthResult.Failed("SignInErrorNoAccount");
                        }

                        profile.LastLoginDate = DateTime.Now;

                        SaveToken(token);
                        return MisskeyAuthResult.Succeeded(token, profile);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: check failed - {ex.Message}");
                return MisskeyAuthResult.Failed("SignInErrorUnreachable");
            }
        }

        public static async Task<MisskeyUserFetchResult> FetchCurrentUserAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return MisskeyUserFetchResult.Rejected();

            try
            {
                Uri uri = new Uri($"https://{InstanceHost}/api/i");
                JsonObject bodyJson = new JsonObject();
                bodyJson.Add("i", JsonValue.CreateStringValue(token));

                using (HttpStringContent content = new HttpStringContent(bodyJson.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json"))
                {
                    using (var response = await s_client.Value.PostAsync(uri, content))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            var status = (int)response.StatusCode;
                            if (status == 401 || status == 403)
                            {
                                Debug.WriteLine($"MisskeyAuthService: /api/i rejected the token ({status})");
                                return MisskeyUserFetchResult.Rejected();
                            }

                            Debug.WriteLine($"MisskeyAuthService: /api/i unavailable ({status})");
                            return MisskeyUserFetchResult.Unavailable();
                        }

                        var body = await response.Content.ReadAsStringAsync();
                        var profile = ParseUser(JsonObject.Parse(body));
                        return profile != null
                               ? MisskeyUserFetchResult.Succeeded(profile)
                               : MisskeyUserFetchResult.Unavailable();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: /api/i failed - {ex.Message}");
                return MisskeyUserFetchResult.Unavailable();
            }
        }

        private static UserProfile ParseUser(JsonObject obj)
        {
            if (obj == null) return null;

            var id = JsonString(obj, "id");
            var username = JsonString(obj, "username");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(username)) return null;

            UserProfile profile = new UserProfile();
            profile.UserId = id;
            profile.Username = username;
            profile.Host = JsonString(obj, "host");
            profile.DisplayName = JsonString(obj, "name");
            profile.Bio = JsonString(obj, "description");
            profile.AvatarUrl = JsonString(obj, "avatarUrl");

            var createdAt = JsonString(obj, "createdAt");
            DateTime parsedDate;
            if (!string.IsNullOrWhiteSpace(createdAt) &&
                DateTime.TryParse(createdAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out parsedDate))
            {
                profile.CreatedDate = parsedDate;
            }

            return profile;
        }

        private static string JsonString(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;
            var v = obj.GetNamedValue(key);
            if (v.ValueType != JsonValueType.String) return null;
            return v.GetString();
        }

        private static JsonObject GetNamedObjectOrNull(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;
            if (obj.GetNamedValue(key).ValueType != JsonValueType.Object) return null;
            return obj.GetNamedObject(key);
        }

        #region Token Storage

        private static void SaveToken(string token)
        {
            ClearToken();
            PasswordVault vault = new PasswordVault();
            vault.Add(new PasswordCredential(VaultResource, VaultUsernameKey, token));
        }

        public static string TryGetToken()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                var credential = vault.Retrieve(VaultResource, VaultUsernameKey);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch
            {
                return null;
            }
        }

        public static Task<string> TryGetTokenAsync()
        {
            return Task.Run(() => TryGetToken());
        }

        public static void ClearToken()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                var credential = vault.Retrieve(VaultResource, VaultUsernameKey);
                vault.Remove(credential);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MisskeyAuthService: no stored token removed - {ex.Message}");
            }
        }

        #endregion
    }

    public class MisskeyAuthResult
    {
        public bool Success { get; set; }

        public string ErrorKey { get; set; }

        public string ErrorMessage
        {
            get { return string.IsNullOrEmpty(ErrorKey) ? null : LocalizedStrings.Get(ErrorKey); }
        }

        public string Token { get; set; }
        public UserProfile Profile { get; set; }

        private MisskeyAuthResult()
        {
        }

        public static MisskeyAuthResult Failed(string errorKey)
        {
            return new MisskeyAuthResult { Success = false, ErrorKey = errorKey };
        }

        public static MisskeyAuthResult Succeeded(string token, UserProfile profile)
        {
            return new MisskeyAuthResult { Success = true, Token = token, Profile = profile };
        }
    }

    public class MisskeyUserFetchResult
    {
        public UserProfile Profile { get; set; }

        public bool TokenRejected { get; set; }

        private MisskeyUserFetchResult()
        {
        }

        public static MisskeyUserFetchResult Succeeded(UserProfile profile)
        {
            return new MisskeyUserFetchResult { Profile = profile };
        }

        public static MisskeyUserFetchResult Rejected()
        {
            return new MisskeyUserFetchResult { TokenRejected = true };
        }

        public static MisskeyUserFetchResult Unavailable()
        {
            return new MisskeyUserFetchResult();
        }
    }
}
