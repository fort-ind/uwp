using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

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

        public static SocialAnnouncement FromJson(JObject obj)
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

        public static IReadOnlyList<SocialAnnouncement> ListFromJson(JArray array)
        {
            var announcements = new List<SocialAnnouncement>(array.Count);
            announcements.AddRange(array.OfType<JObject>().Select(value => FromJson(value)).Where(announcement => announcement != null));

            return announcements;
        }
    }
}
