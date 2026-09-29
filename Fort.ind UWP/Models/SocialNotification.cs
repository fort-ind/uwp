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

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(Name) ? Username : Name; }
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
                AvatarUrl = SocialJson.String(obj, "avatarUrl")
            };
        }
    }

    public sealed class SocialNote
    {
        private const string DirectVisibility = "specified";

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

        public SocialNote Renote { get; private set; }

        internal static SocialNote FromJson(JsonObject obj)
        {
            return FromJson(obj, true);
        }

        private static SocialNote FromJson(JsonObject obj, bool readRenote)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            if (string.IsNullOrWhiteSpace(id)) return null;

            return new SocialNote
            {
                Id = id,
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                Text = SocialJson.String(obj, "text"),
                ContentWarning = SocialJson.String(obj, "cw"),
                Visibility = SocialJson.String(obj, "visibility"),
                User = SocialUser.FromJson(SocialJson.Object(obj, "user")),
                Renote = readRenote ? FromJson(SocialJson.Object(obj, "renote"), false) : null
            };
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
