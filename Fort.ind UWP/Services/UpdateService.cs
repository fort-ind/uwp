using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public sealed class UpdateService
    {
        private UpdateService()
        {
        }

        public const string LatestReleasePageUrl = "https://github.com/fort-ind/uwp/releases/latest";

        private static readonly Uri LatestReleaseApiUri = new Uri("https://api.github.com/repos/fort-ind/uwp/releases/latest");

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(AppConstants.UpdateCheckIntervalHours);

        private static readonly Regex s_packageVersionPattern =
            new Regex(@"(?<!\d)(?<!\d\.)(\d+)\.(\d+)\.(\d+)\.(\d+)(?!\.?\d)", RegexOptions.CultureInvariant);

        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(CreateClient);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();

            var version = AppConstants.AppVersionDisplay;
            int space = version.IndexOf(' ');
            if (space > 0)
            {
                version = version.Substring(0, space);
            }

            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind/" + version))
            {
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind");
            }

            client.DefaultRequestHeaders.Accept.TryParseAdd("application/vnd.github+json");

            return client;
        }

        public static bool AutomaticChecksEnabled
        {
            get
            {
                try
                {
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingCheckForUpdates];
                    if (stored == null) return true;
                    return Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UpdateService: AutomaticChecksEnabled read failed - {ex.Message}");
                    return true;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingCheckForUpdates] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UpdateService: AutomaticChecksEnabled write failed - {ex.Message}");
                }
            }
        }

        public static async Task<UpdateCheckResult> CheckNowAsync()
        {
            var installed = InstalledVersion();
            if (installed == null) return UpdateCheckResult.Failed();

            var latest = await FetchLatestReleaseVersionAsync();
            if (latest == null) return UpdateCheckResult.Failed();

            RememberLatest(latest);

            return latest > installed
                   ? UpdateCheckResult.Available(latest)
                   : UpdateCheckResult.UpToDate(latest);
        }

        public static async Task<Version> GetUpdateToOfferAsync()
        {
            if (!AutomaticChecksEnabled) return null;

            var installed = InstalledVersion();
            if (installed == null) return null;

            Version latest = null;
            if (IsAutomaticCheckDue())
            {
                latest = await FetchLatestReleaseVersionAsync();
                if (latest != null)
                {
                    RememberLatest(latest);
                }
            }

            if (latest == null)
            {
                latest = ReadVersionSetting(AppConstants.SettingUpdateLatestVersion);
            }

            if (latest == null || latest <= installed) return null;

            var dismissed = ReadVersionSetting(AppConstants.SettingUpdateDismissedVersion);
            if (dismissed != null && latest <= dismissed) return null;

            return latest;
        }

        public static bool IsMajorUpgrade(Version latest)
        {
            var installed = InstalledVersion();
            return latest != null && installed != null && latest.Major > installed.Major;
        }

        public static void Dismiss(Version version)
        {
            if (version == null) return;

            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingUpdateDismissedVersion] = version.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService: could not record the dismissed version - {ex.Message}");
            }
        }

        public static string FormatForDisplay(Version version)
        {
            if (version == null) return "";
            return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }

        private static Version InstalledVersion()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return new Version(v.Major, v.Minor, v.Build, v.Revision);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService: could not read the installed version - {ex.Message}");
                return null;
            }
        }

        private static async Task<Version> FetchLatestReleaseVersionAsync()
        {
            try
            {
                using (var cts = new CancellationTokenSource(RequestTimeout))
                using (var response = await s_client.Value.GetAsync(LatestReleaseApiUri).AsTask(cts.Token))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"UpdateService: latest release request returned {(int)response.StatusCode}");
                        return null;
                    }

                    var body = await response.Content.ReadAsStringAsync().AsTask(cts.Token);

                    JsonObject release;
                    if (!JsonObject.TryParse(body, out release)) return null;

                    if (JsonBoolean(release, "draft") || JsonBoolean(release, "prerelease")) return null;

                    return ReleaseVersion(release);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService: latest release check failed - {ex.Message}");
                return null;
            }
        }

        private static Version ReleaseVersion(JsonObject release)
        {
            Version fromAssets = null;

            IJsonValue assets;
            if (release.TryGetValue("assets", out assets) && assets.ValueType == JsonValueType.Array)
            {
                foreach (var asset in assets.GetArray())
                {
                    if (asset.ValueType != JsonValueType.Object) continue;

                    var version = TryParsePackageVersion(JsonString(asset.GetObject(), "name"));
                    if (version != null && (fromAssets == null || version > fromAssets))
                    {
                        fromAssets = version;
                    }
                }
            }

            return fromAssets ?? TryParseTagVersion(JsonString(release, "tag_name"));
        }

        private static Version TryParsePackageVersion(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;

            var match = s_packageVersionPattern.Match(assetName);
            if (!match.Success) return null;

            int major, minor, build, revision;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out build) ||
                !int.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out revision))
            {
                return null;
            }

            return new Version(major, minor, build, revision);
        }

        private static Version TryParseTagVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var text = tag.Trim().TrimStart('v', 'V');
            var dash = text.IndexOf('-');
            if (dash >= 0)
            {
                text = text.Substring(0, dash);
            }

            Version parsed;
            if (!Version.TryParse(text, out parsed)) return null;

            return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        }

        private static bool IsAutomaticCheckDue()
        {
            string raw = null;
            try
            {
                raw = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingUpdateLastCheckedUtc] as string;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService: could not read the last check time - {ex.Message}");
            }

            DateTimeOffset lastChecked;
            if (string.IsNullOrEmpty(raw) ||
                !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lastChecked))
            {
                return true;
            }

            var age = DateTimeOffset.UtcNow - lastChecked;
            return age < TimeSpan.Zero || age >= AutomaticCheckInterval;
        }

        private static void RememberLatest(Version latest)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                values[AppConstants.SettingUpdateLatestVersion] = latest.ToString();
                values[AppConstants.SettingUpdateLastCheckedUtc] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService: could not record the check - {ex.Message}");
            }
        }

        private static Version ReadVersionSetting(string key)
        {
            try
            {
                var raw = ApplicationData.Current.LocalSettings.Values[key] as string;

                Version parsed;
                return !string.IsNullOrEmpty(raw) && Version.TryParse(raw, out parsed) ? parsed : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService: could not read {key} - {ex.Message}");
                return null;
            }
        }

        private static string JsonString(JsonObject obj, string key)
        {
            IJsonValue value;
            if (obj == null || !obj.TryGetValue(key, out value) || value.ValueType != JsonValueType.String) return null;
            return value.GetString();
        }

        private static bool JsonBoolean(JsonObject obj, string key)
        {
            IJsonValue value;
            if (obj == null || !obj.TryGetValue(key, out value) || value.ValueType != JsonValueType.Boolean) return false;
            return value.GetBoolean();
        }
    }

    public enum UpdateCheckOutcome
    {
        UpToDate,
        UpdateAvailable,
        Failed
    }

    public sealed class UpdateCheckResult
    {
        private UpdateCheckResult(UpdateCheckOutcome outcome, Version latestVersion)
        {
            Outcome = outcome;
            LatestVersion = latestVersion;
        }

        public UpdateCheckOutcome Outcome { get; private set; }

        public Version LatestVersion { get; private set; }

        internal static UpdateCheckResult Available(Version latest)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, latest);
        }

        internal static UpdateCheckResult UpToDate(Version latest)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.UpToDate, latest);
        }

        internal static UpdateCheckResult Failed()
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, null);
        }
    }
}
