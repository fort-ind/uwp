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
            ChatView.TitleChanged += ChatView_TitleChanged;
            ChatView.Left += ChatView_Left;
        }

        public static string TitleFor(SocialChatArgs args)
        {
            if (args != null && args.IsRoom)
            {
                var room = args.Room ?? SocialChatService.RoomFor(args.RoomId);
                return room == null ? LocalizedStrings.Get("HeaderChat") : room.DisplayName;
            }

            var user = args == null ? null : args.User;
            var name = user == null ? null : MfmText.PlainName(SocialNoteItem.DisplayNameOf(user));
            return string.IsNullOrWhiteSpace(name) ? LocalizedStrings.Get("HeaderChat") : name;
        }

        public bool Shows(string key)
        {
            return _args != null && string.Equals(_args.Key, key, StringComparison.Ordinal);
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

        private void ChatView_Left(object sender, string key)
        {
            try
            {
                if (IsOwnWindow)
                {
                    WindowManagerService.CloseCurrentWindow();
                }
                else if (Frame != null && Frame.CanGoBack && ReferenceEquals(Frame.Content, this))
                {
                    Frame.GoBack();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatConversationPage: could not leave the room", ex);
            }
        }

        private void ChatView_TitleChanged(object sender, string title)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(title) && IsOwnWindow) WindowManagerService.SetCurrentWindowTitle(title);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatConversationPage: could not retitle the window", ex);
            }
        }
    }
}
