using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialChatReaction
    {
        public SocialChatReaction(string key, string userId)
        {
            Key = key;
            UserId = userId;
        }

        public string Key { get; private set; }

        public string UserId { get; private set; }

        public bool Matches(string key, string userId)
        {
            if (!SocialReactions.AreSame(Key, key)) return false;
            return userId == null || string.Equals(UserId, userId, StringComparison.Ordinal);
        }
    }

    public sealed class SocialChatMessage
    {
        private static readonly IReadOnlyList<SocialChatReaction> s_noReactions = new SocialChatReaction[0];

        private SocialChatMessage()
        {
        }

        public string Id { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public string FromUserId { get; private set; }

        public string ToUserId { get; private set; }

        public string ToRoomId { get; private set; }

        public SocialUser FromUser { get; private set; }

        public SocialUser ToUser { get; private set; }

        public SocialChatRoom ToRoom { get; private set; }

        public string Text { get; private set; }

        public SocialDriveFile File { get; private set; }

        public bool? IsRead { get; private set; }

        public IReadOnlyList<SocialChatReaction> ReactionEntries { get; private set; }

        public bool IsKnownUnread
        {
            get { return IsRead.HasValue && !IsRead.GetValueOrDefault(); }
        }

        public bool IsRoomMessage
        {
            get { return !string.IsNullOrEmpty(ToRoomId); }
        }

        public IReadOnlyList<SocialReactionCount> Reactions
        {
            get
            {
                if (ReactionEntries.Count == 0) return new SocialReactionCount[0];

                return ReactionEntries.GroupBy(entry => SocialReactions.Normalize(entry.Key) ?? entry.Key, StringComparer.Ordinal)
                                      .Select(group => new SocialReactionCount(group.First().Key, group.Count()))
                                      .ToList();
            }
        }

        public string OtherUserId(string myId)
        {
            return string.Equals(FromUserId, myId, StringComparison.Ordinal) ? ToUserId : FromUserId;
        }

        public SocialUser OtherUser(string myId)
        {
            return string.Equals(FromUserId, myId, StringComparison.Ordinal) ? ToUser : FromUser;
        }

        public bool IsFrom(string userId)
        {
            return !string.IsNullOrEmpty(userId) && string.Equals(FromUserId, userId, StringComparison.Ordinal);
        }

        public bool HasReaction(string key, string userId)
        {
            return ReactionEntries.Any(entry => entry.Matches(key, userId));
        }

        public SocialChatMessage WithReaction(string key, bool added, string userId)
        {
            if (string.IsNullOrEmpty(key) || HasReaction(key, userId) == added) return this;

            var entries = new List<SocialChatReaction>(ReactionEntries);
            if (added)
            {
                entries.Add(new SocialChatReaction(key, userId));
            }
            else
            {
                entries.RemoveAll(entry => entry.Matches(key, userId));
            }

            var copy = (SocialChatMessage)MemberwiseClone();
            copy.ReactionEntries = entries;
            return copy;
        }

        public SocialChatMessage WithUsers(SocialUser fromUser, SocialUser toUser)
        {
            var copy = (SocialChatMessage)MemberwiseClone();
            if (copy.FromUser == null) copy.FromUser = fromUser;
            if (copy.ToUser == null) copy.ToUser = toUser;
            return copy;
        }

        internal static SocialChatMessage FromJson(JObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            var fromUserId = SocialJson.String(obj, "fromUserId");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(fromUserId)) return null;

            return new SocialChatMessage
            {
                Id = id,
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                FromUserId = fromUserId,
                ToUserId = SocialJson.String(obj, "toUserId"),
                ToRoomId = SocialJson.String(obj, "toRoomId"),
                FromUser = SocialUser.FromJson(SocialJson.Object(obj, "fromUser")),
                ToUser = SocialUser.FromJson(SocialJson.Object(obj, "toUser")),
                ToRoom = SocialChatRoom.FromJson(SocialJson.Object(obj, "toRoom")),
                Text = SocialJson.String(obj, "text"),
                File = SocialDriveFile.FromJson(SocialJson.Object(obj, "file")),
                IsRead = SocialJson.Bool(obj, "isRead"),
                ReactionEntries = ReactionsFrom(SocialJson.Array(obj, "reactions"))
            };
        }

        internal static IReadOnlyList<SocialChatMessage> ListFromJson(JArray array)
        {
            var messages = new List<SocialChatMessage>();
            if (array == null) return messages;

            messages.AddRange(array.OfType<JObject>().Select(value => FromJson(value)).Where(message => message != null));
            return messages;
        }

        private static IReadOnlyList<SocialChatReaction> ReactionsFrom(JArray array)
        {
            if (array == null || array.Count == 0) return s_noReactions;

            var entries = array.OfType<JObject>()
                               .Select(entry => new
                               {
                                   Key = SocialJson.String(entry, "reaction"),
                                   User = SocialUser.FromJson(SocialJson.Object(entry, "user"))
                               })
                               .Where(entry => !string.IsNullOrEmpty(entry.Key))
                               .Select(entry => new SocialChatReaction(entry.Key, entry.User == null ? null : entry.User.Id))
                               .ToList();
            return entries.Count == 0 ? s_noReactions : entries;
        }
    }
}
