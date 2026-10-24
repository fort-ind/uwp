using System;
using System.Collections.Generic;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialAnnouncement
    {
        private SocialAnnouncement()
        {
        }

        public string Id { get; private set; }

        public string Title { get; private set; }

        public string Text { get; private set; }

        public string Icon { get; private set; }

        public bool IsRead { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static SocialAnnouncement FromJson(JsonObject obj)
        {
            var id = SocialJson.String(obj, "id");
            if (string.IsNullOrEmpty(id)) return null;

            var title = SocialJson.String(obj, "title");
            var text = SocialJson.String(obj, "text");
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text)) return null;

            return new SocialAnnouncement
            {
                Id = id,
                Title = title == null ? string.Empty : title.Trim(),
                Text = text == null ? string.Empty : text.Trim(),
                Icon = SocialJson.String(obj, "icon"),
                IsRead = SocialJson.Bool(obj, "isRead").GetValueOrDefault(),
                CreatedAt = SocialJson.Date(obj, "createdAt")
            };
        }

        public static IReadOnlyList<SocialAnnouncement> ListFromJson(JsonArray array)
        {
            var announcements = new List<SocialAnnouncement>(array.Count);
            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var announcement = FromJson(value.GetObject());
                if (announcement != null) announcements.Add(announcement);
            }

            return announcements;
        }
    }
}
