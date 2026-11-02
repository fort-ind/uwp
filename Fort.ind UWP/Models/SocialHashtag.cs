using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialHashtag
    {
        private SocialHashtag()
        {
        }

        public string Tag { get; private set; }

        public int MentionedUsers { get; private set; }

        public string Label
        {
            get { return "#" + Tag; }
        }

        public string CountText
        {
            get
            {
                return LocalizedStrings.Format("SocialHashtagCountFormat", MentionedUsers.ToString("N0", CultureInfo.CurrentCulture));
            }
        }

        public string AutomationName
        {
            get { return LocalizedStrings.Format("SocialHashtagAutomationFormat", Label, CountText); }
        }

        public static SocialHashtag FromJson(JObject obj)
        {
            var tag = SocialJson.String(obj, "tag");
            if (string.IsNullOrWhiteSpace(tag)) return null;

            return new SocialHashtag
            {
                Tag = tag.Trim(),
                MentionedUsers = Math.Max(0, SocialJson.Int(obj, "mentionedUsersCount").GetValueOrDefault())
            };
        }

        public static IReadOnlyList<SocialHashtag> ListFromJson(JArray array)
        {
            var tags = new List<SocialHashtag>(array.Count);
            foreach (var value in array)
            {
                if (value.Type != JTokenType.Object) continue;

                var tag = FromJson((JObject)value);
                if (tag != null && tag.MentionedUsers > 0) tags.Add(tag);
            }

            return tags;
        }
    }
}
