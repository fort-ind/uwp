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

    public sealed class SocialTagArgs
    {
        public SocialTagArgs(string accountId, string tag)
        {
            AccountId = accountId;
            Tag = tag;
        }

        public string AccountId { get; private set; }

        public string Tag { get; private set; }

        public bool IsSearch { get; private set; }

        public static SocialTagArgs ForSearch(string accountId, string query)
        {
            return new SocialTagArgs(accountId, query) { IsSearch = true };
        }
    }

    public enum SocialComposeMode
    {
        New,
        Quote,
        Edit,
        Redraft,
        Scheduled
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

        public SocialScheduledNote ScheduledNote { get; private set; }

        public static SocialComposeArgs ForScheduled(string accountId, SocialScheduledNote scheduled)
        {
            return new SocialComposeArgs(accountId, SocialComposeMode.Scheduled, null) { ScheduledNote = scheduled };
        }
    }

    public sealed class SocialScheduledArgs
    {
        public SocialScheduledArgs(string accountId)
        {
            AccountId = accountId;
        }

        public string AccountId { get; private set; }
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
