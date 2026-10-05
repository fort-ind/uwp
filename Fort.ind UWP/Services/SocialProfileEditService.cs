using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public sealed class SocialProfileImage
    {
        public SocialProfileImage(string name, string contentType, ulong size, Func<Task<IRandomAccessStream>> open)
        {
            Name = name;
            ContentType = contentType;
            Size = size;
            Open = open;
        }

        public string Name { get; private set; }

        public string ContentType { get; private set; }

        public ulong Size { get; private set; }

        public Func<Task<IRandomAccessStream>> Open { get; private set; }
    }

    public sealed class SocialProfileEdit
    {
        public bool NameChanged { get; set; }

        public string Name { get; set; }

        public bool BioChanged { get; set; }

        public string Bio { get; set; }

        public SocialProfileImage Avatar { get; set; }

        public SocialProfileImage Banner { get; set; }

        public bool IsEmpty
        {
            get { return !NameChanged && !BioChanged && Avatar == null && Banner == null; }
        }
    }

    public static class SocialProfileEditService
    {
        public const int NameMaxLength = 50;

        public const int BioMaxLength = 1500;

        public const string BotField = "isBot";

        public const string CatField = "isCat";

        public const string SpeakAsCatField = "speakAsCat";

        private static readonly Dictionary<string, string> s_errorMessages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "YOUR_NAME_CONTAINS_PROHIBITED_WORDS", "SocialProfileErrorProhibitedName" },
            { "AVATAR_NOT_AN_IMAGE", "SocialProfileErrorNotAnImage" },
            { "BANNER_NOT_AN_IMAGE", "SocialProfileErrorNotAnImage" },
            { "MAX_FILE_SIZE_EXCEEDED", "SocialProfileErrorTooLarge" },
            { "NO_FREE_SPACE", "SocialProfileErrorNoSpace" },
            { "INAPPROPRIATE", "SocialProfileErrorInappropriate" },
            { "RATE_LIMIT_EXCEEDED", "SocialProfileErrorRateLimit" }
        };

        public static int Length(string text)
        {
            var normalized = SocialPostService.NormalizeText(text);
            var length = 0;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (char.IsHighSurrogate(normalized[i]) && i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1])) i++;
                length++;
            }

            return length;
        }

        public static async Task<bool> SaveAsync(UIElement owner, SocialProfileEdit edit, IProgress<double> progress)
        {
            if (edit == null || edit.IsEmpty) return true;

            var hasImages = edit.Avatar != null || edit.Banner != null;
            if (hasImages && !await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteDrive, SocialSignInPrompt.Profile)) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Profile)) return false;

            string token = null;
            try
            {
                token = await MisskeyAuthService.TryGetTokenAsync();

                var body = new JsonObject();
                if (edit.NameChanged) body.Add("name", OptionalText(edit.Name));
                if (edit.BioChanged) body.Add("description", OptionalText(edit.Bio));

                var total = (double)((edit.Avatar != null ? edit.Avatar.Size : 0) + (edit.Banner != null ? edit.Banner.Size : 0));
                var done = 0.0;

                if (edit.Avatar != null)
                {
                    var avatar = await UploadAsync(token, edit.Avatar, done, total, progress);
                    if (avatar.Status != SocialApiStatus.Ok) return await ReportAsync(owner, avatar.Status, avatar.ErrorCode, SocialPermissions.WriteDrive);
                    body.Add("avatarId", JsonValue.CreateStringValue(avatar.Value.Id));
                    done += edit.Avatar.Size;
                }

                if (edit.Banner != null)
                {
                    var banner = await UploadAsync(token, edit.Banner, done, total, progress);
                    if (banner.Status != SocialApiStatus.Ok) return await ReportAsync(owner, banner.Status, banner.ErrorCode, SocialPermissions.WriteDrive);
                    body.Add("bannerId", JsonValue.CreateStringValue(banner.Value.Id));
                }

                if (progress != null && hasImages) progress.Report(1);

                var result = await SocialApiService.UpdateProfileAsync(token, body, CancellationToken.None);
                if (result.Status != SocialApiStatus.Ok) return await ReportAsync(owner, result.Status, result.ErrorCode, SocialPermissions.WriteAccount);

                await ProfileService.ApplyOwnUpdateAsync(token, result.Value);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileEditService: saving the profile failed - {ex.GetType().Name}: {ex.Message}");
                return await ReportAsync(owner, SocialApiStatus.Failed, null, SocialPermissions.WriteAccount);
            }
        }

        public static async Task<bool> SetFlagAsync(UIElement owner, string field, bool value)
        {
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Profile)) return false;

            SocialApiResult<UserProfile> result;
            string token = null;
            try
            {
                token = await MisskeyAuthService.TryGetTokenAsync();
                var body = new JsonObject();
                body.Add(field, JsonValue.CreateBooleanValue(value));
                result = await SocialApiService.UpdateProfileAsync(token, body, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileEditService: changing {field} failed - {ex.GetType().Name}: {ex.Message}");
                result = SocialApiResult<UserProfile>.Failed(SocialApiStatus.Failed);
            }

            if (result.Status == SocialApiStatus.Ok)
            {
                await ProfileService.ApplyOwnUpdateAsync(token, result.Value);
                return true;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Profile);
            }
            else
            {
                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Get("SocialProfileFlagFailedTitle"),
                                                     MessageFor(result.ErrorCode),
                                                     LocalizedStrings.Get("DialogOk"));
            }

            return false;
        }

        private static IJsonValue OptionalText(string text)
        {
            var normalized = SocialPostService.NormalizeText(text).Trim();
            return normalized.Length == 0 ? JsonValue.CreateNullValue() : JsonValue.CreateStringValue(normalized);
        }

        private static async Task<SocialApiResult<SocialDriveFile>> UploadAsync(string token, SocialProfileImage image, double done, double total,
                                                                               IProgress<double> progress)
        {
            var report = new Progress<HttpProgress>(update =>
            {
                if (progress == null || total <= 0) return;
                progress.Report(Math.Min(1, (done + update.BytesSent) / total));
            });

            using (var stream = await image.Open())
            {
                return await SocialApiService.UploadFileAsync(token, stream, image.Name, image.ContentType, false, report, CancellationToken.None);
            }
        }

        private static async Task<bool> ReportAsync(UIElement owner, SocialApiStatus status, string errorCode, string permission)
        {
            if (status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, permission, SocialSignInPrompt.Profile);
                return false;
            }

            await DialogService.ShowMessageAsync(owner,
                                                 LocalizedStrings.Get("SocialProfileSaveFailedTitle"),
                                                 MessageFor(errorCode),
                                                 LocalizedStrings.Get("DialogOk"));
            return false;
        }

        public static string MessageFor(string errorCode)
        {
            string key;
            if (errorCode == null || !s_errorMessages.TryGetValue(errorCode, out key)) key = "SocialProfileErrorGeneric";

            return LocalizedStrings.Get(key);
        }

        public static string UploadName(string original, string extension)
        {
            var stem = string.IsNullOrWhiteSpace(original) ? "image" : System.IO.Path.GetFileNameWithoutExtension(original);
            if (string.IsNullOrWhiteSpace(stem)) stem = "image";
            return stem + extension;
        }
    }
}
