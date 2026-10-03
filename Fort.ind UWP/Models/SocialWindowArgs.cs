namespace Fort.ind_UWP
{
    public sealed class SocialUserWindowArgs
    {
        public SocialUserWindowArgs(string accountId, string userId, string handle, SocialUser user, SocialUserDetail detail)
        {
            AccountId = accountId;
            UserId = userId;
            Handle = handle;
            User = user;
            Detail = detail;
        }

        public string AccountId { get; private set; }

        public string UserId { get; private set; }

        public string Handle { get; private set; }

        public SocialUser User { get; private set; }

        public SocialUserDetail Detail { get; private set; }
    }

    public enum SocialThreadTab
    {
        Replies,
        Renotes,
        Reactions
    }

    public sealed class SocialNoteArgs
    {
        public SocialNoteArgs(string accountId, string noteId, SocialNote note, bool focusReply, SocialThreadTab initialTab)
        {
            AccountId = accountId;
            NoteId = noteId;
            Note = note;
            FocusReply = focusReply;
            InitialTab = initialTab;
        }

        public string AccountId { get; private set; }

        public string NoteId { get; private set; }

        public SocialNote Note { get; private set; }

        public bool FocusReply { get; private set; }

        public SocialThreadTab InitialTab { get; private set; }
    }

    public enum SocialComposeMode
    {
        New,
        Quote,
        Edit,
        Redraft
    }

    public sealed class SocialComposeArgs
    {
        public SocialComposeArgs(string accountId, SocialComposeMode mode, SocialNote note)
        {
            AccountId = accountId;
            Mode = mode;
            Note = note;
        }

        public string AccountId { get; private set; }

        public SocialComposeMode Mode { get; private set; }

        public SocialNote Note { get; private set; }
    }

    public sealed class SocialUserListArgs
    {
        public SocialUserListArgs(string accountId, string userId, string handle, SocialUser user, SocialFollowList list)
        {
            AccountId = accountId;
            UserId = userId;
            Handle = handle;
            User = user;
            List = list;
        }

        public string AccountId { get; private set; }

        public string UserId { get; private set; }

        public string Handle { get; private set; }

        public SocialUser User { get; private set; }

        public SocialFollowList List { get; private set; }
    }
}
