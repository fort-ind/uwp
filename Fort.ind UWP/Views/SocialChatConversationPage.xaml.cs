using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatConversationPage : Page, IReleasablePage, ITrimmablePage, IReopenablePage
    {
        private SocialChatArgs _args;

        public SocialChatConversationPage()
        {
            this.InitializeComponent();

            Loaded += SocialChatConversationPage_Loaded;
            ChatView.PersonResolved += ChatView_PersonResolved;
        }

        public static string TitleFor(SocialChatArgs args)
        {
            var user = args == null ? null : args.User;
            var name = user == null ? null : MfmText.PlainName(SocialNoteItem.DisplayNameOf(user));
            return string.IsNullOrWhiteSpace(name) ? LocalizedStrings.Get("HeaderChat") : name;
        }

        public bool Shows(string userId)
        {
            return _args != null && string.Equals(_args.UserId, userId, StringComparison.Ordinal);
        }

        private bool IsOwnWindow
        {
            get { return WindowManagerService.IsSecondaryView && Frame != null && !Frame.CanGoBack; }
        }

        public void Release()
        {
            ChatView.Release();
        }

        public void Trim()
        {
            ChatView.Trim();
        }

        public void Reopen(object parameter)
        {
            ChatView.FocusComposer();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                _args = e.Parameter as SocialChatArgs;
                ChatView.CanPopOut = !IsOwnWindow;
                if (_args != null && SocialContentService.IsCurrentAccount(_args.Account)) ChatView.Open(_args);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatConversationPage: could not open the conversation", ex);
            }
        }

        private void SocialChatConversationPage_Loaded(object sender, RoutedEventArgs e)
        {
            ChatView.FocusComposer();
        }

        private void ChatView_PersonResolved(object sender, SocialUser user)
        {
            try
            {
                if (user == null || !IsOwnWindow) return;

                var name = MfmText.PlainName(SocialNoteItem.DisplayNameOf(user));
                if (!string.IsNullOrWhiteSpace(name)) WindowManagerService.SetCurrentWindowTitle(name);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatConversationPage: could not retitle the window", ex);
            }
        }
    }
}
