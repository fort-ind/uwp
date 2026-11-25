using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialChatRoom
    {
        private SocialChatRoom()
        {
        }

        public string Id { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public string OwnerId { get; private set; }

        public SocialUser Owner { get; private set; }

        public bool IsMuted { get; private set; }

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(Name) ? LocalizedStrings.Get("SocialChatRoomUnnamed") : Name; }
        }

        public bool IsOwnedBy(string userId)
        {
            return !string.IsNullOrEmpty(userId) && string.Equals(OwnerId, userId, StringComparison.Ordinal);
        }

        public SocialChatRoom WithMuted(bool muted)
        {
            var copy = (SocialChatRoom)MemberwiseClone();
            copy.IsMuted = muted;
            return copy;
        }

        internal static SocialChatRoom FromJson(JObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            if (string.IsNullOrWhiteSpace(id)) return null;

            return new SocialChatRoom
            {
                Id = id,
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                Name = SocialJson.String(obj, "name"),
                Description = SocialJson.String(obj, "description"),
                OwnerId = SocialJson.String(obj, "ownerId"),
                Owner = SocialUser.FromJson(SocialJson.Object(obj, "owner")),
                IsMuted = SocialJson.Bool(obj, "isMuted").GetValueOrDefault()
            };
        }

        internal static IReadOnlyList<SocialChatRoom> ListFromJson(JArray array)
        {
            var rooms = new List<SocialChatRoom>();
            if (array == null) return rooms;

            rooms.AddRange(array.OfType<JObject>().Select(value => FromJson(value)).Where(room => room != null));
            return rooms;
        }
    }

    public sealed class SocialChatInvitation
    {
        private SocialChatInvitation()
        {
        }

        public string Id { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public string RoomId { get; private set; }

        public SocialChatRoom Room { get; private set; }

        public SocialUser User { get; private set; }

        public string Title
        {
            get { return Room == null ? LocalizedStrings.Get("SocialChatRoomUnnamed") : Room.DisplayName; }
        }

        public string FromText
        {
            get
            {
                var from = Room == null ? null : Room.Owner;
                return from == null ? "" : LocalizedStrings.Format("SocialChatInvitationFromFormat", from.Handle);
            }
        }

        internal static SocialChatInvitation FromJson(JObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            var roomId = SocialJson.String(obj, "roomId");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(roomId)) return null;

            return new SocialChatInvitation
            {
                Id = id,
                CreatedAt = SocialJson.Date(obj, "createdAt"),
                RoomId = roomId,
                Room = SocialChatRoom.FromJson(SocialJson.Object(obj, "room")),
                User = SocialUser.FromJson(SocialJson.Object(obj, "user"))
            };
        }

        internal static IReadOnlyList<SocialChatInvitation> ListFromJson(JArray array)
        {
            var invitations = new List<SocialChatInvitation>();
            if (array == null) return invitations;

            invitations.AddRange(array.OfType<JObject>().Select(value => FromJson(value)).Where(invitation => invitation != null));
            return invitations;
        }
    }

    public sealed class SocialChatMembership
    {
        private SocialChatMembership()
        {
        }

        public string Id { get; private set; }

        public string UserId { get; private set; }

        public SocialUser User { get; private set; }

        public string RoomId { get; private set; }

        public SocialChatRoom Room { get; private set; }

        internal static SocialChatMembership FromJson(JObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            if (string.IsNullOrWhiteSpace(id)) return null;

            return new SocialChatMembership
            {
                Id = id,
                UserId = SocialJson.String(obj, "userId"),
                User = SocialUser.FromJson(SocialJson.Object(obj, "user")),
                RoomId = SocialJson.String(obj, "roomId"),
                Room = SocialChatRoom.FromJson(SocialJson.Object(obj, "room"))
            };
        }

        internal static IReadOnlyList<SocialChatMembership> ListFromJson(JArray array)
        {
            var memberships = new List<SocialChatMembership>();
            if (array == null) return memberships;

            memberships.AddRange(array.OfType<JObject>().Select(value => FromJson(value)).Where(membership => membership != null));
            return memberships;
        }
    }

    public static class SocialChatKeys
    {
        private const string RoomPrefix = "room:";

        public static string ForRoom(string roomId)
        {
            return string.IsNullOrEmpty(roomId) ? null : RoomPrefix + roomId;
        }

        public static bool IsRoom(string key)
        {
            return key != null && key.StartsWith(RoomPrefix, StringComparison.Ordinal);
        }

        public static string RoomIdOf(string key)
        {
            return IsRoom(key) ? key.Substring(RoomPrefix.Length) : null;
        }

        public static string UserIdOf(string key)
        {
            return IsRoom(key) ? null : key;
        }

        public static string Of(SocialChatMessage message, string account)
        {
            if (message == null) return null;
            return message.IsRoomMessage ? ForRoom(message.ToRoomId) : message.OtherUserId(account);
        }
    }
}
