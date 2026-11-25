namespace Fort.ind_UWP
{
    public sealed class SocialChatArgs
    {
        public SocialChatArgs(string account, string userId, SocialUser user)
        {
            Account = account;
            UserId = userId;
            User = user;
            Key = userId;
        }

        private SocialChatArgs(string account, SocialChatRoom room, string roomId)
        {
            Account = account;
            Room = room;
            RoomId = roomId;
            Key = SocialChatKeys.ForRoom(roomId);
        }

        public static SocialChatArgs ForRoom(string account, string roomId, SocialChatRoom room)
        {
            return new SocialChatArgs(account, room, roomId);
        }

        public static SocialChatArgs ForKey(string account, string key)
        {
            return SocialChatKeys.IsRoom(key)
                   ? ForRoom(account, SocialChatKeys.RoomIdOf(key), null)
                   : new SocialChatArgs(account, key, null);
        }

        public string Account { get; private set; }

        public string Key { get; private set; }

        public string UserId { get; private set; }

        public SocialUser User { get; private set; }

        public string RoomId { get; private set; }

        public SocialChatRoom Room { get; private set; }

        public bool IsRoom
        {
            get { return RoomId != null; }
        }
    }
}
