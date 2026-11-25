namespace Fort.ind_UWP
{
    public sealed class SocialChatArgs
    {
        public SocialChatArgs(string account, string userId, SocialUser user)
        {
            Account = account;
            UserId = userId;
            User = user;
        }

        public string Account { get; private set; }

        public string UserId { get; private set; }

        public SocialUser User { get; private set; }
    }
}
