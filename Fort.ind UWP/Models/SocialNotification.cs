using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialUser
    {
        private SocialUser()
        {
        }

        public string Id { get; private set; }

        public string Username { get; private set; }

        public string Host { get; private set; }

        public string Name { get; private set; }

        public string AvatarUrl { get; private set; }

        public string AvatarBlurhash { get; private set; }

        public bool IsBot { get; private set; }

        public IReadOnlyDictionary<string, string> Emojis { get; private set; }

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(Name) ? Username : Name; }
        }

        public string Handle
        {
            get
            {
                return string.IsNullOrWhiteSpace(Host)
                       ? "@" + Username
                       : "@" + Username + "@" + Host;
            }
        }

        internal static SocialUser FromJson(JsonObject obj)
        {
            if (obj == null) return null;

            var username = SocialJson.String(obj, "username");
            if (string.IsNullOrWhiteSpace(username)) return null;

            return new SocialUser
            {
                Id = SocialJson.String(obj, "id"),
                Username = username,
                Host = SocialJson.String(obj, "host"),
                Name = SocialJson.String(obj, "name"),
                AvatarUrl = SocialJson.String(obj, "avatarUrl"),
                AvatarBlurhash = SocialJson.String(obj, "avatarBlurhash"),
                IsBot = SocialJson.Bool(obj, "isBot").GetValueOrDefault(),
                Emojis = SocialJson.StringMap(obj, "emojis")
            };
        }

        internal JsonObject ToJson()
        {
            var obj = new JsonObject();
            SocialJson.Put(obj, "id", Id);
            SocialJson.Put(obj, "username", Username);
            SocialJson.Put(obj, "host", Host);
            SocialJson.Put(obj, "name", Name);
            SocialJson.Put(obj, "avatarUrl", AvatarUrl);
            SocialJson.Put(obj, "avatarBlurhash", AvatarBlurhash);
            obj.Add("isBot", JsonValue.CreateBooleanValue(IsBot));

            var emojis = new JsonObject();
            foreach (var pair in Emojis)
            {
                emojis.Add(pair.Key, JsonValue.CreateStringValue(pair.Value ?? ""));
            }
            obj.Add("emojis", emojis);
            return obj;
        }
    }

    public sealed class SocialNote
    {
        private const string DirectVisibility = "specified";

        private const int DefaultDepth = 2;

        private SocialNote()
        {
        }

        public string Id { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public string Text { get; private set; }

        public string ContentWarning { get; private set; }

        public string Visibility { get; private set; }

        public bool IsDirect
        {
            get { return string.Equals(Visibility, DirectVisibility, StringComparison.Ordinal); }
        }

        public SocialUser User { get; private set; }

        public string RenoteId { get; private set; }

        public SocialNote Renote { get; private set; }

        public string ReplyId { get; private set; }

        public SocialNote Reply { get; private set; }

        public bool IsHidden { get; private set; }

        public IReadOnlyList<SocialDriveFile> Files { get; private set; }

        public bool HasPoll { get; private set; }

        public IReadOnlyDictionary<string, string> Emojis { get; private set; }

        public IReadOnlyDictionary<string, string> MentionHandles { get; private set; }

        public int RepliesCount { get; private set; }

        public int RenoteCount { get; private set; }

        public int ReactionCount { get; private set; }

        public string UserId { get; private set; }

        public DateTimeOffset? UpdatedAt { get; private set; }

        public bool LocalOnly { get; private set; }

        public string ReactionAcceptance { get; private set; }

        public IReadOnlyList<SocialReactionCount> Reactions { get; private set; }

        public IReadOnlyDictionary<string, string> ReactionEmojis { get; private set; }

        public string MyReaction { get; private set; }

        public SocialPoll Poll { get; private set; }

        public IReadOnlyList<string> VisibleUserIds { get; private set; }

        public IReadOnlyList<string> MentionIds { get; private set; }

        public IReadOnlyList<string> FileIds { get; private set; }

        public string RemoteUri { get; private set; }

        public string RemoteUrl { get; private set; }

        public bool IsPureRenote
        {
            get
            {
                return Renote != null
                       && string.IsNullOrEmpty(Text)
                       && string.IsNullOrEmpty(ContentWarning)
                       && Files.Count == 0
                       && !HasPoll;
            }
        }

        internal static SocialNote FromJson(JsonObject obj)
        {
            return FromJson(obj, DefaultDepth);
        }

        private static SocialNote FromJson(JsonObject obj, int depth)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            if (string.IsNullOrWhiteSpace(id)) return null;

            var isHidden = SocialJson.Bool(obj, "isHidden").GetValueOrDefault();
            var reactions = isHidden
                            ? (IReadOnlyList<SocialReactionCount>)new SocialReactionCount[0]
                            : SocialReactionCount.ListFromJson(SocialJson.Object(obj, "reactions"));
            var updatedAt = SocialJson.Date(obj, "updatedAt");
            var poll = SocialPoll.FromJson(SocialJson.Object(obj, "poll"));
            var myReaction = SocialJson.String(obj, "myReaction");

            return new SocialNote
            {
                Id = id,
                UserId = SocialJson.String(obj, "userId"),
                UpdatedAt = updatedAt == DateTimeOffset.MinValue ? (DateTimeOffset?)null : updatedAt,
                LocalOnly = SocialJson.Bool(obj, "localOnly").GetValueOrDefault(),
                ReactionAcceptance = SocialJson.String(obj, "reactionAcceptance"),
                Reactions = reactions,
                ReactionEmojis = SocialJson.StringMap(obj, "reactionEmojis"),
                MyReaction = string.IsNullOrEmpty(myReaction) ? null : SocialReactions.Normalize(myReaction),
                Poll = poll,
                VisibleUserIds = SocialJson.StringList(obj, "visibleUserIds"),
                MentionIds = SocialJson.StringList(obj, "mentions"),
                FileIds = SocialJson.StringList(obj, "fileIds"),
                RemoteUri = SocialJson.String(obj, "uri"),
                RemoteUrl = SocialJson.String(obj, "url"),
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                Text = SocialJson.String(obj, "text"),
                ContentWarning = SocialJson.String(obj, "cw"),
                Visibility = SocialJson.String(obj, "visibility"),
                User = SocialUser.FromJson(SocialJson.Object(obj, "user")),
                RenoteId = SocialJson.String(obj, "renoteId"),
                Renote = depth > 0 ? FromJson(SocialJson.Object(obj, "renote"), depth - 1) : null,
                ReplyId = SocialJson.String(obj, "replyId"),
                Reply = depth > 0 ? FromJson(SocialJson.Object(obj, "reply"), 0) : null,
                IsHidden = isHidden,
                Files = SocialDriveFile.ListFromJson(SocialJson.Array(obj, "files")),
                HasPoll = poll != null,
                Emojis = SocialJson.StringMap(obj, "emojis"),
                MentionHandles = SocialJson.StringMap(obj, "mentionHandles"),
                RepliesCount = SocialJson.Int(obj, "repliesCount") ?? 0,
                RenoteCount = SocialJson.Int(obj, "renoteCount") ?? 0,
                ReactionCount = SocialJson.Int(obj, "reactionCount") ?? SocialReactionCount.Total(reactions)
            };
        }

        public static SocialNote FromJsonValue(IJsonValue value)
        {
            return value != null && value.ValueType == JsonValueType.Object ? FromJson(value.GetObject()) : null;
        }

        public static IReadOnlyList<SocialNote> ListFromJson(JsonArray array)
        {
            var notes = new List<SocialNote>();
            if (array == null) return notes;

            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var note = FromJson(value.GetObject());
                if (note != null) notes.Add(note);
            }

            return notes;
        }
    }

    public sealed class SocialNotification
    {
        private SocialNotification()
        {
        }

        public string Id { get; private set; }

        public string Type { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public SocialUser User { get; private set; }

        public SocialNote Note { get; private set; }

        public string Reaction { get; private set; }

        public string RoleName { get; private set; }

        public string AppHeader { get; private set; }

        public string AppBody { get; private set; }

        public static SocialNotification FromJson(JsonObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            var type = SocialJson.String(obj, "type");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type)) return null;

            var role = SocialJson.Object(obj, "role");

            return new SocialNotification
            {
                Id = id,
                Type = type,
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                User = SocialUser.FromJson(SocialJson.Object(obj, "user")),
                Note = SocialNote.FromJson(SocialJson.Object(obj, "note")),
                Reaction = SocialJson.String(obj, "reaction"),
                RoleName = role == null ? null : SocialJson.String(role, "name"),
                AppHeader = SocialJson.String(obj, "header"),
                AppBody = SocialJson.String(obj, "body")
            };
        }

        public static IReadOnlyList<SocialNotification> ListFromJson(JsonArray array)
        {
            var notifications = new List<SocialNotification>();
            if (array == null) return notifications;

            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var notification = FromJson(value.GetObject());
                if (notification != null) notifications.Add(notification);
            }

            return notifications;
        }
    }

    internal static class SocialJson
    {
        private static readonly IReadOnlyDictionary<string, string> s_emptyMap =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly IReadOnlyList<string> s_emptyList = new string[0];

        public static IReadOnlyDictionary<string, string> EmptyMap
        {
            get { return s_emptyMap; }
        }

        public static void Put(JsonObject obj, string key, string value)
        {
            obj.SetNamedValue(key, value == null ? JsonValue.CreateNullValue() : JsonValue.CreateStringValue(value));
        }

        public static string String(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;

            var value = obj.GetNamedValue(key);
            return value.ValueType == JsonValueType.String ? value.GetString() : null;
        }

        public static JsonObject Object(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;

            var value = obj.GetNamedValue(key);
            return value.ValueType == JsonValueType.Object ? value.GetObject() : null;
        }

        public static JsonArray Array(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;

            var value = obj.GetNamedValue(key);
            return value.ValueType == JsonValueType.Array ? value.GetArray() : null;
        }

        public static int? Int(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;

            var value = obj.GetNamedValue(key);
            if (value.ValueType != JsonValueType.Number) return null;

            var number = value.GetNumber();
            if (double.IsNaN(number)) return null;
            if (number >= int.MaxValue) return int.MaxValue;
            if (number <= int.MinValue) return int.MinValue;
            return (int)number;
        }

        public static bool? Bool(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key)) return null;

            var value = obj.GetNamedValue(key);
            return value.ValueType == JsonValueType.Boolean ? value.GetBoolean() : (bool?)null;
        }

        public static IReadOnlyList<string> EmptyList
        {
            get { return s_emptyList; }
        }

        public static IReadOnlyList<string> StringList(JsonObject obj, string key)
        {
            var array = Array(obj, key);
            if (array == null || array.Count == 0) return s_emptyList;

            var result = new List<string>(array.Count);
            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.String) continue;

                var text = value.GetString();
                if (!string.IsNullOrEmpty(text)) result.Add(text);
            }

            return result.Count == 0 ? s_emptyList : result;
        }

        public static IReadOnlyDictionary<string, string> StringMap(JsonObject obj, string key)
        {
            var map = Object(obj, key);
            if (map == null || map.Count == 0) return s_emptyMap;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                if (pair.Value.ValueType != JsonValueType.String) continue;

                var text = pair.Value.GetString();
                if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(text))
                {
                    result[pair.Key] = text;
                }
            }

            return result.Count == 0 ? s_emptyMap : result;
        }

        public static DateTimeOffset Date(JsonObject obj, string key)
        {
            DateTimeOffset parsed;
            var raw = String(obj, key);
            if (!string.IsNullOrEmpty(raw) &&
                DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed;
            }

            return DateTimeOffset.MinValue;
        }
    }
}
