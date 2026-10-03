using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

namespace Fort.ind_UWP
{
    public enum SocialApiStatus
    {
        Ok,
        PermissionDenied,
        TokenRejected,
        Refused,
        Failed
    }

    public enum SocialUserNotesTab
    {
        Notes,
        Replies,
        Media,
        Favorites
    }

    public enum SocialFollowList
    {
        Following,
        Followers
    }

    public sealed class SocialFollowEntry
    {
        public SocialFollowEntry(string id, SocialUserDetail user)
        {
            Id = id;
            User = user;
        }

        public string Id { get; private set; }

        public SocialUserDetail User { get; private set; }
    }

    public sealed class SocialFavoriteEntry
    {
        public SocialFavoriteEntry(string id, SocialNote note)
        {
            Id = id;
            Note = note;
        }

        public string Id { get; private set; }

        public SocialNote Note { get; private set; }
    }

    public sealed class SocialNoteState
    {
        public SocialNoteState(bool isFavorited, bool isMutedThread)
        {
            IsFavorited = isFavorited;
            IsMutedThread = isMutedThread;
        }

        public bool IsFavorited { get; private set; }

        public bool IsMutedThread { get; private set; }

        public SocialNoteState WithFavorited(bool value)
        {
            return new SocialNoteState(value, IsMutedThread);
        }

        public SocialNoteState WithMutedThread(bool value)
        {
            return new SocialNoteState(IsFavorited, value);
        }
    }

    public sealed class SocialApiResult<T>
    {
        private SocialApiResult()
        {
        }

        public SocialApiStatus Status { get; private set; }

        public T Value { get; private set; }

        public string ErrorCode { get; private set; }

        public static SocialApiResult<T> Succeeded(T value)
        {
            return new SocialApiResult<T> { Status = SocialApiStatus.Ok, Value = value };
        }

        public static SocialApiResult<T> Failed(SocialApiStatus status)
        {
            return new SocialApiResult<T> { Status = status };
        }

        public static SocialApiResult<T> Failed(SocialApiStatus status, string errorCode)
        {
            return new SocialApiResult<T> { Status = status, ErrorCode = errorCode };
        }
    }

    public sealed class SocialMe
    {
        public SocialMe(int unreadCount, UserProfile profile)
        {
            UnreadCount = unreadCount;
            Profile = profile;
        }

        public int UnreadCount { get; }

        public UserProfile Profile { get; }
    }

    public static class SocialApiService
    {
        private const string PermissionDeniedCode = "PERMISSION_DENIED";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(5);

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

            return client;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNotification>>> GetNotificationsAsync(
            string token, string untilId, int limit, bool markAsRead, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            body.Add("markAsRead", JsonValue.CreateBooleanValue(markAsRead));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var response = await PostAsync("i/notifications", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialNotification>>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialNotification>>.Failed(SocialApiStatus.Failed);
            }

            return SocialApiResult<IReadOnlyList<SocialNotification>>.Succeeded(
                SocialNotification.ListFromJson(response.Value.GetArray()));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetMentionsAsync(
            string token, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var response = await PostAsync("notes/mentions", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialNote>>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialNote>>.Failed(SocialApiStatus.Failed);
            }

            return SocialApiResult<IReadOnlyList<SocialNote>>.Succeeded(
                SocialNote.ListFromJson(response.Value.GetArray()));
        }

        public static async Task<SocialApiResult<SocialMe>> GetMeAsync(string token, CancellationToken cancellationToken)
        {
            var response = await PostAsync("i", token, new JsonObject(), cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialMe>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Object)
            {
                return SocialApiResult<SocialMe>.Failed(SocialApiStatus.Failed);
            }

            var me = response.Value.GetObject();
            var count = ReadUnreadCount(me);
            if (!count.HasValue) return SocialApiResult<SocialMe>.Failed(SocialApiStatus.Failed);

            return SocialApiResult<SocialMe>.Succeeded(new SocialMe(count.Value, MisskeyAuthService.ParseCurrentUser(me)));
        }

        public static int? ReadUnreadCount(JsonObject me)
        {
            if (me == null || !me.ContainsKey("unreadNotificationsCount")) return null;

            var value = me.GetNamedValue("unreadNotificationsCount");
            if (value.ValueType != JsonValueType.Number) return null;

            var number = value.GetNumber();
            if (double.IsNaN(number) || number < 0) return null;

            return number > int.MaxValue ? int.MaxValue : (int)number;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetTimelineAsync(
            string token, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            return NotesFrom(await PostAsync("notes/timeline", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetUserNotesAsync(
            string token, string userId, SocialUserNotesTab tab, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("userId", JsonValue.CreateStringValue(userId ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            body.Add("withReplies", JsonValue.CreateBooleanValue(tab == SocialUserNotesTab.Replies));
            body.Add("withFiles", JsonValue.CreateBooleanValue(tab == SocialUserNotesTab.Media));
            body.Add("withRenotes", JsonValue.CreateBooleanValue(tab != SocialUserNotesTab.Media));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            return NotesFrom(await PostAsync("users/notes", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<SocialUserDetail>> GetUserAsync(
            string token, string userId, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("userId", JsonValue.CreateStringValue(userId ?? ""));

            return UserFrom(await PostAsync("users/show", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<SocialUserDetail>> GetUserByHandleAsync(
            string token, string username, string host, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("username", JsonValue.CreateStringValue(username ?? ""));
            body.Add("host", string.IsNullOrWhiteSpace(host) ? JsonValue.CreateNullValue() : JsonValue.CreateStringValue(host));

            return UserFrom(await PostAsync("users/show", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialFollowEntry>>> GetFollowListAsync(
            string token, string userId, SocialFollowList list, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("userId", JsonValue.CreateStringValue(userId ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var followers = list == SocialFollowList.Followers;
            var response = await PostAsync(followers ? "users/followers" : "users/following", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialFollowEntry>>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialFollowEntry>>.Failed(SocialApiStatus.Failed);
            }

            var entries = new List<SocialFollowEntry>();
            foreach (var value in response.Value.GetArray())
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var following = value.GetObject();
                var id = SocialJson.String(following, "id");
                var person = SocialJson.Object(following, followers ? "follower" : "followee");
                var detail = person == null ? null : SocialUserDetail.FromJson(person);
                if (string.IsNullOrEmpty(id) || detail == null) continue;

                entries.Add(new SocialFollowEntry(id, detail));
            }

            return SocialApiResult<IReadOnlyList<SocialFollowEntry>>.Succeeded(entries);
        }

        public static Task<SocialApiResult<bool>> FollowAsync(string token, string userId, CancellationToken cancellationToken)
        {
            return UserActionAsync("following/create", token, userId, cancellationToken);
        }

        public static Task<SocialApiResult<bool>> UnfollowAsync(string token, string userId, CancellationToken cancellationToken)
        {
            return UserActionAsync("following/delete", token, userId, cancellationToken);
        }

        public static Task<SocialApiResult<bool>> CancelFollowRequestAsync(string token, string userId, CancellationToken cancellationToken)
        {
            return UserActionAsync("following/requests/cancel", token, userId, cancellationToken);
        }

        private static async Task<SocialApiResult<bool>> UserActionAsync(string endpoint, string token, string userId,
                                                                        CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("userId", JsonValue.CreateStringValue(userId ?? ""));

            var response = await PostAsync(endpoint, token, body, cancellationToken);
            return response.Status == SocialApiStatus.Ok
                   ? SocialApiResult<bool>.Succeeded(true)
                   : SocialApiResult<bool>.Failed(response.Status, response.ErrorCode);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialCustomEmoji>>> GetEmojisAsync(
            string token, CancellationToken cancellationToken)
        {
            var response = await PostAsync("emojis", token, new JsonObject(), cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialCustomEmoji>>.Failed(response.Status);
            }

            var root = response.Value.ValueType == JsonValueType.Object ? response.Value.GetObject() : null;
            var list = SocialJson.Array(root, "emojis");
            if (list == null)
            {
                return SocialApiResult<IReadOnlyList<SocialCustomEmoji>>.Failed(SocialApiStatus.Failed);
            }

            var emojis = new List<SocialCustomEmoji>();
            foreach (var value in list)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var emoji = SocialCustomEmoji.FromJson(value.GetObject());
                if (emoji != null) emojis.Add(emoji);
            }

            return SocialApiResult<IReadOnlyList<SocialCustomEmoji>>.Succeeded(emojis);
        }

        public static async Task<SocialApiResult<SocialNote>> GetNoteAsync(string token, string noteId, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("noteId", JsonValue.CreateStringValue(noteId ?? ""));

            var response = await PostAsync("notes/show", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialNote>.Failed(response.Status, response.ErrorCode);
            }

            var note = SocialNote.FromJsonValue(response.Value);
            return note == null
                   ? SocialApiResult<SocialNote>.Failed(SocialApiStatus.Failed)
                   : SocialApiResult<SocialNote>.Succeeded(note);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetChildrenAsync(
            string token, string noteId, string sinceId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("noteId", JsonValue.CreateStringValue(noteId ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            body.Add("showQuotes", JsonValue.CreateBooleanValue(false));
            body.Add("sinceId", JsonValue.CreateStringValue(string.IsNullOrEmpty(sinceId) ? noteId ?? "" : sinceId));

            return NotesFrom(await PostAsync("notes/children", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetConversationAsync(
            string token, string noteId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("noteId", JsonValue.CreateStringValue(noteId ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));

            return NotesFrom(await PostAsync("notes/conversation", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetRenotesAsync(
            string token, string noteId, string untilId, int limit, CancellationToken cancellationToken)
        {
            return await GetRenotesAsync(token, noteId, null, untilId, limit, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetRenotesAsync(
            string token, string noteId, string userId, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("noteId", JsonValue.CreateStringValue(noteId ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(userId))
            {
                body.Add("userId", JsonValue.CreateStringValue(userId));
            }
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            return NotesFrom(await PostAsync("notes/renotes", token, body, cancellationToken));
        }

        public static Task<SocialApiResult<bool>> ReactAsync(string token, string noteId, string reaction, CancellationToken cancellationToken)
        {
            JsonObject body = NoteBody(noteId);
            body.Add("reaction", JsonValue.CreateStringValue(reaction ?? ""));

            return ActionAsync("notes/reactions/create", token, body, cancellationToken);
        }

        public static Task<SocialApiResult<bool>> UnreactAsync(string token, string noteId, CancellationToken cancellationToken)
        {
            return ActionAsync("notes/reactions/delete", token, NoteBody(noteId), cancellationToken);
        }

        public static async Task<SocialApiResult<SocialNote>> RenoteAsync(string token, string noteId, string visibility, bool localOnly,
                                                                         CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("renoteId", JsonValue.CreateStringValue(noteId ?? ""));
            body.Add("visibility", JsonValue.CreateStringValue(visibility ?? "public"));
            body.Add("localOnly", JsonValue.CreateBooleanValue(localOnly));

            var response = await PostAsync("notes/create", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialNote>.Failed(response.Status, response.ErrorCode);
            }

            var root = response.Value.ValueType == JsonValueType.Object ? response.Value.GetObject() : null;
            var created = SocialNote.FromJson(SocialJson.Object(root, "createdNote"));
            return created == null
                   ? SocialApiResult<SocialNote>.Failed(SocialApiStatus.Failed)
                   : SocialApiResult<SocialNote>.Succeeded(created);
        }

        public static Task<SocialApiResult<bool>> UnrenoteAsync(string token, string noteId, CancellationToken cancellationToken)
        {
            return ActionAsync("notes/unrenote", token, NoteBody(noteId), cancellationToken);
        }

        public static Task<SocialApiResult<bool>> FavoriteAsync(string token, string noteId, bool favorite, CancellationToken cancellationToken)
        {
            return ActionAsync(favorite ? "notes/favorites/create" : "notes/favorites/delete", token, NoteBody(noteId), cancellationToken);
        }

        public static Task<SocialApiResult<bool>> MuteThreadAsync(string token, string noteId, bool mute, CancellationToken cancellationToken)
        {
            return ActionAsync(mute ? "notes/thread-muting/create" : "notes/thread-muting/delete", token, NoteBody(noteId), cancellationToken);
        }

        public static Task<SocialApiResult<bool>> PinAsync(string token, string noteId, bool pin, CancellationToken cancellationToken)
        {
            return ActionAsync(pin ? "i/pin" : "i/unpin", token, NoteBody(noteId), cancellationToken);
        }

        public static Task<SocialApiResult<bool>> DeleteNoteAsync(string token, string noteId, CancellationToken cancellationToken)
        {
            return ActionAsync("notes/delete", token, NoteBody(noteId), cancellationToken);
        }

        public static Task<SocialApiResult<bool>> VoteAsync(string token, string noteId, int choice, CancellationToken cancellationToken)
        {
            JsonObject body = NoteBody(noteId);
            body.Add("choice", JsonValue.CreateNumberValue(choice));

            return ActionAsync("notes/polls/vote", token, body, cancellationToken);
        }

        public static Task<SocialApiResult<bool>> ReportAsync(string token, string userId, string comment, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("userId", JsonValue.CreateStringValue(userId ?? ""));
            body.Add("comment", JsonValue.CreateStringValue(comment ?? ""));

            return ActionAsync("users/report-abuse", token, body, cancellationToken);
        }

        public static async Task<SocialApiResult<SocialNoteState>> GetNoteStateAsync(string token, string noteId, CancellationToken cancellationToken)
        {
            var response = await PostAsync("notes/state", token, NoteBody(noteId), cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialNoteState>.Failed(response.Status, response.ErrorCode);
            }

            var root = response.Value.ValueType == JsonValueType.Object ? response.Value.GetObject() : null;
            if (root == null) return SocialApiResult<SocialNoteState>.Failed(SocialApiStatus.Failed);

            return SocialApiResult<SocialNoteState>.Succeeded(
                new SocialNoteState(SocialJson.Bool(root, "isFavorited") == true, SocialJson.Bool(root, "isMutedThread") == true));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialFavoriteEntry>>> GetFavoritesAsync(
            string token, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var response = await PostAsync("i/favorites", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialFavoriteEntry>>.Failed(response.Status, response.ErrorCode);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialFavoriteEntry>>.Failed(SocialApiStatus.Failed);
            }

            var entries = new List<SocialFavoriteEntry>();
            foreach (var value in response.Value.GetArray())
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var favorite = value.GetObject();
                var id = SocialJson.String(favorite, "id");
                var note = SocialNote.FromJson(SocialJson.Object(favorite, "note"));
                if (string.IsNullOrEmpty(id) || note == null) continue;

                entries.Add(new SocialFavoriteEntry(id, note));
            }

            return SocialApiResult<IReadOnlyList<SocialFavoriteEntry>>.Succeeded(entries);
        }

        public static Task<SocialApiResult<SocialNote>> CreateNoteAsync(string token, JsonObject body, CancellationToken cancellationToken)
        {
            return CreatedNoteAsync("notes/create", token, body, cancellationToken);
        }

        public static Task<SocialApiResult<SocialNote>> EditNoteAsync(string token, JsonObject body, CancellationToken cancellationToken)
        {
            return CreatedNoteAsync("notes/edit", token, body, cancellationToken);
        }

        private static async Task<SocialApiResult<SocialNote>> CreatedNoteAsync(string endpoint, string token, JsonObject body,
                                                                               CancellationToken cancellationToken)
        {
            var response = await PostAsync(endpoint, token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialNote>.Failed(response.Status, response.ErrorCode);
            }

            var root = response.Value.ValueType == JsonValueType.Object ? response.Value.GetObject() : null;
            var created = SocialNote.FromJson(SocialJson.Object(root, "createdNote"));
            return created == null
                   ? SocialApiResult<SocialNote>.Failed(SocialApiStatus.Failed)
                   : SocialApiResult<SocialNote>.Succeeded(created);
        }

        public static async Task<SocialApiResult<SocialDriveFile>> UploadFileAsync(string token, IRandomAccessStream stream, string fileName,
                                                                                  string contentType, bool isSensitive,
                                                                                  IProgress<HttpProgress> progress,
                                                                                  CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return SocialApiResult<SocialDriveFile>.Failed(SocialApiStatus.TokenRejected);
            }

            Uri uri = new Uri($"https://{MisskeyAuthService.InstanceHost}/api/drive/files/create");
            Func<IHttpContent> content = () =>
            {
                var form = new HttpMultipartFormDataContent();
                form.Add(new HttpStringContent(token), "i");
                form.Add(new HttpStringContent(isSensitive ? "true" : "false"), "isSensitive");
                if (!string.IsNullOrWhiteSpace(fileName)) form.Add(new HttpStringContent(fileName), "name");

                var file = new HttpStreamContent(stream);
                if (!string.IsNullOrWhiteSpace(contentType)) file.Headers.ContentType = new HttpMediaTypeHeaderValue(contentType);
                form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName);
                return form;
            };

            var response = await SendContentAsync("drive/files/create", uri, content, UploadTimeout, progress, cancellationToken);
            return FileFrom(response);
        }

        public static async Task<SocialApiResult<SocialDriveFile>> UpdateFileAsync(string token, string fileId, string comment, bool isSensitive,
                                                                                  CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("fileId", JsonValue.CreateStringValue(fileId ?? ""));
            body.Add("comment", string.IsNullOrWhiteSpace(comment) ? JsonValue.CreateNullValue() : JsonValue.CreateStringValue(comment));
            body.Add("isSensitive", JsonValue.CreateBooleanValue(isSensitive));

            return FileFrom(await PostAsync("drive/files/update", token, body, cancellationToken));
        }

        private static SocialApiResult<SocialDriveFile> FileFrom(SocialApiResult<IJsonValue> response)
        {
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialDriveFile>.Failed(response.Status, response.ErrorCode);
            }

            var file = response.Value.ValueType == JsonValueType.Object ? SocialDriveFile.FromJson(response.Value.GetObject()) : null;
            return file == null
                   ? SocialApiResult<SocialDriveFile>.Failed(SocialApiStatus.Failed)
                   : SocialApiResult<SocialDriveFile>.Succeeded(file);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialUser>>> SearchUsersAsync(string token, string username, string host, int limit,
                                                                                             CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("username", JsonValue.CreateStringValue(username ?? ""));
            body.Add("host", string.IsNullOrWhiteSpace(host) ? JsonValue.CreateNullValue() : JsonValue.CreateStringValue(host));
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            body.Add("detail", JsonValue.CreateBooleanValue(false));

            return UsersFrom(await PostAsync("users/search-by-username-and-host", token, body, cancellationToken));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialUser>>> GetUsersAsync(string token, IReadOnlyList<string> userIds,
                                                                                          CancellationToken cancellationToken)
        {
            var ids = new JsonArray();
            foreach (var id in userIds)
            {
                if (!string.IsNullOrEmpty(id)) ids.Add(JsonValue.CreateStringValue(id));
            }

            JsonObject body = new JsonObject();
            body.Add("userIds", ids);

            return UsersFrom(await PostAsync("users/show", token, body, cancellationToken));
        }

        private static SocialApiResult<IReadOnlyList<SocialUser>> UsersFrom(SocialApiResult<IJsonValue> response)
        {
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialUser>>.Failed(response.Status, response.ErrorCode);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialUser>>.Failed(SocialApiStatus.Failed);
            }

            var users = new List<SocialUser>();
            foreach (var value in response.Value.GetArray())
            {
                var user = value.ValueType == JsonValueType.Object ? SocialUser.FromJson(value.GetObject()) : null;
                if (user != null) users.Add(user);
            }

            return SocialApiResult<IReadOnlyList<SocialUser>>.Succeeded(users);
        }

        public static async Task<SocialApiResult<IReadOnlyList<string>>> SearchHashtagsAsync(string token, string query, int limit,
                                                                                            CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("query", JsonValue.CreateStringValue(query ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));

            var response = await PostAsync("hashtags/search", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<string>>.Failed(response.Status, response.ErrorCode);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<string>>.Failed(SocialApiStatus.Failed);
            }

            var tags = new List<string>();
            foreach (var value in response.Value.GetArray())
            {
                if (value.ValueType == JsonValueType.String && value.GetString().Length > 0) tags.Add(value.GetString());
            }

            return SocialApiResult<IReadOnlyList<string>>.Succeeded(tags);
        }

        private static JsonObject NoteBody(string noteId)
        {
            JsonObject body = new JsonObject();
            body.Add("noteId", JsonValue.CreateStringValue(noteId ?? ""));
            return body;
        }

        private static async Task<SocialApiResult<bool>> ActionAsync(string endpoint, string token, JsonObject body,
                                                                    CancellationToken cancellationToken)
        {
            var response = await PostAsync(endpoint, token, body, cancellationToken);
            return response.Status == SocialApiStatus.Ok
                   ? SocialApiResult<bool>.Succeeded(true)
                   : SocialApiResult<bool>.Failed(response.Status, response.ErrorCode);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialReactionEntry>>> GetReactionsAsync(
            string token, string noteId, string type, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("noteId", JsonValue.CreateStringValue(noteId ?? ""));
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(type))
            {
                body.Add("type", JsonValue.CreateStringValue(type));
            }
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var response = await PostAsync("notes/reactions", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialReactionEntry>>.Failed(response.Status, response.ErrorCode);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialReactionEntry>>.Failed(SocialApiStatus.Failed);
            }

            return SocialApiResult<IReadOnlyList<SocialReactionEntry>>.Succeeded(
                SocialReactionEntry.ListFromJson(response.Value.GetArray()));
        }

        public static async Task<SocialApiResult<SocialLinkPreview>> GetUrlPreviewAsync(
            string url, string language, CancellationToken cancellationToken)
        {
            var query = "url=" + Uri.EscapeDataString(url ?? "");
            if (!string.IsNullOrEmpty(language))
            {
                query += "&lang=" + Uri.EscapeDataString(language);
            }

            var response = await SendAsync("url", new Uri($"https://{MisskeyAuthService.InstanceHost}/url?{query}"), null, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialLinkPreview>.Failed(response.Status);
            }

            var preview = response.Value.ValueType == JsonValueType.Object
                          ? SocialLinkPreview.FromJson(response.Value.GetObject(), url)
                          : null;

            return preview == null
                   ? SocialApiResult<SocialLinkPreview>.Failed(SocialApiStatus.Refused)
                   : SocialApiResult<SocialLinkPreview>.Succeeded(preview);
        }

        private static SocialApiResult<IReadOnlyList<SocialNote>> NotesFrom(SocialApiResult<IJsonValue> response)
        {
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialNote>>.Failed(response.Status, response.ErrorCode);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialNote>>.Failed(SocialApiStatus.Failed);
            }

            return SocialApiResult<IReadOnlyList<SocialNote>>.Succeeded(SocialNote.ListFromJson(response.Value.GetArray()));
        }

        private static SocialApiResult<SocialUserDetail> UserFrom(SocialApiResult<IJsonValue> response)
        {
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialUserDetail>.Failed(response.Status);
            }

            var detail = response.Value.ValueType == JsonValueType.Object
                         ? SocialUserDetail.FromJson(response.Value.GetObject())
                         : null;

            return detail == null
                   ? SocialApiResult<SocialUserDetail>.Failed(SocialApiStatus.Failed)
                   : SocialApiResult<SocialUserDetail>.Succeeded(detail);
        }

        private static Task<SocialApiResult<IJsonValue>> PostAsync(string endpoint, string token, JsonObject body,
                                                                  CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult(SocialApiResult<IJsonValue>.Failed(SocialApiStatus.TokenRejected));
            }

            body.SetNamedValue("i", JsonValue.CreateStringValue(token));
            Uri uri = new Uri($"https://{MisskeyAuthService.InstanceHost}/api/{endpoint}");

            return SendAsync(endpoint, uri, body.Stringify(), cancellationToken);
        }

        private static Task<SocialApiResult<IJsonValue>> SendAsync(string endpoint, Uri uri, string jsonBody,
                                                                  CancellationToken cancellationToken)
        {
            Func<IHttpContent> content = null;
            if (jsonBody != null)
            {
                content = () => new HttpStringContent(jsonBody, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
            }

            return SendContentAsync(endpoint, uri, content, RequestTimeout, null, cancellationToken);
        }

        private static async Task<SocialApiResult<IJsonValue>> SendContentAsync(string endpoint, Uri uri, Func<IHttpContent> contentFactory,
                                                                               TimeSpan deadline, IProgress<HttpProgress> progress,
                                                                               CancellationToken cancellationToken)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(deadline))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
                using (var content = contentFactory == null ? null : contentFactory())
                using (var response = content == null
                                      ? await s_client.Value.GetAsync(uri).AsTask(linked.Token)
                                      : progress == null
                                        ? await s_client.Value.PostAsync(uri, content).AsTask(linked.Token)
                                        : await s_client.Value.PostAsync(uri, content).AsTask(linked.Token, progress))
                {
                    var text = await response.Content.ReadAsStringAsync().AsTask(linked.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorCode = ReadErrorCode(text);
                        var status = ClassifyFailure((int)response.StatusCode, errorCode);
                        Debug.WriteLine($"SocialApiService: {endpoint} answered {(int)response.StatusCode} {errorCode} ({status})");
                        return SocialApiResult<IJsonValue>.Failed(status, errorCode);
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return SocialApiResult<IJsonValue>.Succeeded(JsonValue.CreateNullValue());
                    }

                    JsonValue parsed;
                    if (!JsonValue.TryParse(text, out parsed))
                    {
                        Debug.WriteLine($"SocialApiService: {endpoint} returned something that is not JSON");
                        return SocialApiResult<IJsonValue>.Failed(SocialApiStatus.Failed);
                    }

                    return SocialApiResult<IJsonValue>.Succeeded(parsed);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) throw;

                Debug.WriteLine($"SocialApiService: {endpoint} failed - {ex.GetType().Name}: {ex.Message}");
                return SocialApiResult<IJsonValue>.Failed(SocialApiStatus.Failed);
            }
        }

        private static string ReadErrorCode(string body)
        {
            JsonObject parsed;
            if (string.IsNullOrEmpty(body) || !JsonObject.TryParse(body, out parsed)) return null;

            return SocialJson.String(SocialJson.Object(parsed, "error"), "code");
        }

        private static SocialApiStatus ClassifyFailure(int statusCode, string errorCode)
        {
            if (statusCode == 401) return SocialApiStatus.TokenRejected;
            if (statusCode == 400 || statusCode == 404 || statusCode == 422) return SocialApiStatus.Refused;
            if (statusCode != 403) return SocialApiStatus.Failed;

            return string.Equals(errorCode, PermissionDeniedCode, StringComparison.Ordinal)
                   ? SocialApiStatus.PermissionDenied
                   : SocialApiStatus.Failed;
        }
    }
}
