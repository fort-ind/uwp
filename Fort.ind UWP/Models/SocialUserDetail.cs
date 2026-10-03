using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialUserField
    {
        public SocialUserField(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; private set; }

        public string Value { get; private set; }
    }

    public sealed class SocialRole
    {
        public SocialRole(string name, string color, string iconUrl, string description)
        {
            Name = name;
            Color = color;
            IconUrl = iconUrl;
            Description = description;
        }

        public string Name { get; private set; }

        public string Color { get; private set; }

        public string IconUrl { get; private set; }

        public string Description { get; private set; }
    }

    public sealed class SocialUserDetail
    {
        private const string PublicVisibility = "public";

        private const string FollowersOnlyVisibility = "followers";

        private static readonly string[] CachedKeys =
        {
            "id", "username", "host", "name", "avatarUrl", "avatarBlurhash", "isBot", "emojis",
            "description", "location", "birthday", "createdAt", "bannerUrl", "bannerBlurhash", "fields",
            "notesCount", "followingCount", "followersCount", "followingVisibility", "followersVisibility",
            "isLocked", "roles"
        };

        private SocialUserDetail()
        {
        }

        public SocialUser User { get; private set; }

        public string Description { get; private set; }

        public string Location { get; private set; }

        public string Birthday { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public string BannerUrl { get; private set; }

        public string BannerBlurhash { get; private set; }

        public IReadOnlyList<SocialUserField> Fields { get; private set; }

        public int? NotesCount { get; private set; }

        public int? FollowingCount { get; private set; }

        public int? FollowersCount { get; private set; }

        public string FollowingVisibility { get; private set; }

        public string FollowersVisibility { get; private set; }

        public bool IsFollowing { get; private set; }

        public bool IsFollowed { get; private set; }

        public bool HasPendingFollowRequest { get; private set; }

        public bool IsLocked { get; private set; }

        public bool IsSuspended { get; private set; }

        public bool HasMoved { get; private set; }

        public IReadOnlyList<SocialNote> PinnedNotes { get; private set; }

        public IReadOnlyList<SocialRole> Roles { get; private set; }

        public bool ShowsFollowingCount(string viewerId)
        {
            return FollowingCount.HasValue && IsVisibleTo(FollowingVisibility, viewerId);
        }

        public bool ShowsFollowersCount(string viewerId)
        {
            return FollowersCount.HasValue && IsVisibleTo(FollowersVisibility, viewerId);
        }

        private bool IsVisibleTo(string visibility, string viewerId)
        {
            if (!string.IsNullOrEmpty(viewerId) && string.Equals(viewerId, User.Id, StringComparison.Ordinal)) return true;
            if (string.IsNullOrEmpty(visibility)) return true;
            if (string.Equals(visibility, PublicVisibility, StringComparison.Ordinal)) return true;
            if (string.Equals(visibility, FollowersOnlyVisibility, StringComparison.Ordinal)) return IsFollowing;
            return false;
        }

        internal static SocialUserDetail FromJson(JsonObject obj)
        {
            var user = SocialUser.FromJson(obj);
            if (user == null || string.IsNullOrWhiteSpace(user.Id)) return null;

            return new SocialUserDetail
            {
                User = user,
                Description = SocialJson.String(obj, "description"),
                Location = SocialJson.String(obj, "location"),
                Birthday = SocialJson.String(obj, "birthday"),
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                BannerUrl = SocialJson.String(obj, "bannerUrl"),
                BannerBlurhash = SocialJson.String(obj, "bannerBlurhash"),
                Fields = FieldsFromJson(SocialJson.Array(obj, "fields")),
                NotesCount = SocialJson.Int(obj, "notesCount"),
                FollowingCount = SocialJson.Int(obj, "followingCount"),
                FollowersCount = SocialJson.Int(obj, "followersCount"),
                FollowingVisibility = SocialJson.String(obj, "followingVisibility"),
                FollowersVisibility = SocialJson.String(obj, "followersVisibility"),
                IsFollowing = SocialJson.Bool(obj, "isFollowing") == true,
                IsFollowed = SocialJson.Bool(obj, "isFollowed") == true,
                HasPendingFollowRequest = SocialJson.Bool(obj, "hasPendingFollowRequestFromYou") == true,
                IsLocked = SocialJson.Bool(obj, "isLocked") == true,
                IsSuspended = SocialJson.Bool(obj, "isSuspended") == true,
                HasMoved = !string.IsNullOrWhiteSpace(SocialJson.String(obj, "movedTo")),
                PinnedNotes = SocialNote.ListFromJson(SocialJson.Array(obj, "pinnedNotes")),
                Roles = RolesFromJson(SocialJson.Array(obj, "roles"))
            };
        }

        internal static string ToCacheJson(JsonObject obj)
        {
            if (obj == null) return null;

            var subset = new JsonObject();
            foreach (var key in CachedKeys)
            {
                IJsonValue value;
                if (obj.TryGetValue(key, out value) && value != null && value.ValueType != JsonValueType.Null)
                {
                    subset.SetNamedValue(key, value);
                }
            }

            return subset.Stringify();
        }

        public static SocialUserDetail FromProfile(UserProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.UserId)) return null;

            JsonObject cached;
            if (!string.IsNullOrEmpty(profile.DetailJson) && JsonObject.TryParse(profile.DetailJson, out cached))
            {
                var detail = FromJson(cached);
                if (detail != null && string.Equals(detail.User.Id, profile.UserId, StringComparison.Ordinal)) return detail;
            }

            var obj = new JsonObject();
            PutString(obj, "id", profile.UserId);
            PutString(obj, "username", profile.Username);
            PutString(obj, "host", profile.Host);
            PutString(obj, "name", profile.DisplayName);
            PutString(obj, "avatarUrl", profile.AvatarUrl);
            PutString(obj, "description", profile.Bio);
            PutString(obj, "bannerUrl", profile.BannerUrl);
            PutString(obj, "bannerBlurhash", profile.BannerBlurhash);

            if (profile.FollowingCount.HasValue) obj.SetNamedValue("followingCount", JsonValue.CreateNumberValue(profile.FollowingCount.Value));
            if (profile.FollowersCount.HasValue) obj.SetNamedValue("followersCount", JsonValue.CreateNumberValue(profile.FollowersCount.Value));

            if (profile.CreatedDate > DateTime.MinValue)
            {
                PutString(obj, "createdAt", profile.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            }

            return FromJson(obj);
        }

        private static void PutString(JsonObject obj, string key, string value)
        {
            if (!string.IsNullOrEmpty(value)) obj.SetNamedValue(key, JsonValue.CreateStringValue(value));
        }

        private static IReadOnlyList<SocialRole> RolesFromJson(JsonArray array)
        {
            var roles = new List<SocialRole>();
            if (array == null) return roles;

            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var role = value.GetObject();
                var name = SocialJson.String(role, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                roles.Add(new SocialRole(name.Trim(),
                                         SocialJson.String(role, "color"),
                                         SocialJson.String(role, "iconUrl"),
                                         SocialJson.String(role, "description")));
            }

            return roles;
        }

        private static IReadOnlyList<SocialUserField> FieldsFromJson(JsonArray array)
        {
            var fields = new List<SocialUserField>();
            if (array == null) return fields;

            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var field = value.GetObject();
                var name = SocialJson.String(field, "name");
                var text = SocialJson.String(field, "value");
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(text)) continue;

                fields.Add(new SocialUserField(name ?? "", text ?? ""));
            }

            return fields;
        }
    }
}
