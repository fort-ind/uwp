using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialChatMemberItem
    {
        private const int AvatarDecodeSize = 32;

        private ImageSource _avatar;

        public SocialChatMemberItem(SocialUser user, bool isOwner)
        {
            User = user;
            IsOwner = isOwner;
        }

        public SocialUser User { get; private set; }

        public bool IsOwner { get; private set; }

        public string PlainName
        {
            get { return MfmText.PlainName(SocialNoteItem.DisplayNameOf(User)); }
        }

        public string Handle
        {
            get { return User == null ? "" : User.Handle; }
        }

        public Visibility OwnerVisibility
        {
            get { return IsOwner ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string AutomationName
        {
            get
            {
                return LocalizedStrings.Format(IsOwner ? "SocialChatRoomOwnerAutomationFormat" : "SocialChatRoomMemberAutomationFormat",
                                               PlainName, Handle);
            }
        }

        public ImageSource Avatar
        {
            get
            {
                if (_avatar != null) return _avatar;

                var uri = WebLauncher.TryCreateFetchUri(User == null ? null : User.AvatarUrl);
                if (uri == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = AvatarDecodeSize;
                bitmap.DecodePixelHeight = AvatarDecodeSize;
                RemoteImage.SetUri(bitmap, uri);
                _avatar = bitmap;
                return _avatar;
            }
        }
    }
}
