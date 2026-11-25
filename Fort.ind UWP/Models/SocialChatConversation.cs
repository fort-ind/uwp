using System;
using System.Collections.Generic;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialChatConversation : BindableBase
    {
        private const int AvatarDecodeSize = 40;

        private readonly string _account;

        private SocialUser _other;

        private SocialChatMessage _last;

        private bool _isUnread;

        private string _timeText;

        private ImageSource _avatar;

        private string _avatarUrl;

        public SocialChatConversation(string account, string otherUserId, SocialUser other, SocialChatMessage last, bool isUnread)
        {
            _account = account;
            OtherUserId = otherUserId;
            _other = other;
            _last = last;
            _isUnread = isUnread;
            NameSegments = MfmText.ParseName(DisplayName, other == null ? null : other.Emojis);
            RefreshTime();
        }

        public string OtherUserId { get; private set; }

        public SocialUser Other
        {
            get { return _other; }
        }

        public SocialChatMessage LastMessage
        {
            get { return _last; }
        }

        public IReadOnlyList<MfmSegment> NameSegments { get; private set; }

        public string DisplayName
        {
            get { return _other == null ? "" : SocialNoteItem.DisplayNameOf(_other); }
        }

        public string PlainName
        {
            get { return MfmText.PlainName(DisplayName); }
        }

        public string Handle
        {
            get { return _other == null ? "" : SocialLinks.FormatHandle(_other.Username, MisskeyAuthService.InstanceHost); }
        }

        public string Preview
        {
            get
            {
                var body = SocialChatToasts.BodyFor(_last);
                if (body.Length > AppConstants.SocialChatPreviewLength) body = body.Substring(0, AppConstants.SocialChatPreviewLength);
                return _last != null && _last.IsFrom(_account) ? LocalizedStrings.Format("SocialChatPreviewMineFormat", body) : body;
            }
        }

        public string TimeText
        {
            get { return _timeText; }
            private set { SetProperty(ref _timeText, value); }
        }

        public bool IsUnread
        {
            get { return _isUnread; }
            set
            {
                if (SetProperty(ref _isUnread, value))
                {
                    OnPropertyChanged("UnreadVisibility");
                    OnPropertyChanged("NameWeight");
                    OnPropertyChanged("AutomationName");
                }
            }
        }

        public Visibility UnreadVisibility
        {
            get { return _isUnread ? Visibility.Visible : Visibility.Collapsed; }
        }

        public FontWeight NameWeight
        {
            get { return _isUnread ? FontWeights.SemiBold : FontWeights.Normal; }
        }

        public string AutomationName
        {
            get
            {
                return LocalizedStrings.Format(_isUnread ? "SocialChatRowUnreadAutomationFormat" : "SocialChatRowAutomationFormat",
                                               MfmText.PlainName(DisplayName), Preview, TimeText ?? "");
            }
        }

        public ImageSource Avatar
        {
            get
            {
                var url = _other == null ? null : _other.AvatarUrl;
                if (_avatar != null && string.Equals(url, _avatarUrl, StringComparison.Ordinal)) return _avatar;

                _avatarUrl = url;
                _avatar = null;

                var uri = WebLauncher.TryCreateFetchUri(url);
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

        public bool Apply(SocialChatMessage message, bool isUnread)
        {
            if (message == null) return false;
            if (_last != null && message.CreatedAt < _last.CreatedAt) return false;

            _last = message;
            var user = message.OtherUser(_account);
            if (user != null && _other == null)
            {
                _other = user;
                NameSegments = MfmText.ParseName(DisplayName, user.Emojis);
                OnPropertyChanged("NameSegments");
                OnPropertyChanged("DisplayName");
                OnPropertyChanged("PlainName");
                OnPropertyChanged("Avatar");
            }

            IsUnread = isUnread;
            RefreshTime();
            OnPropertyChanged("Preview");
            OnPropertyChanged("AutomationName");
            return true;
        }

        public void SetOther(SocialUser user)
        {
            if (user == null || _other != null) return;

            _other = user;
            NameSegments = MfmText.ParseName(DisplayName, user.Emojis);
            OnPropertyChanged("NameSegments");
            OnPropertyChanged("DisplayName");
            OnPropertyChanged("PlainName");
            OnPropertyChanged("Handle");
            OnPropertyChanged("Avatar");
            OnPropertyChanged("AutomationName");
        }

        public void RefreshTime()
        {
            TimeText = _last == null ? "" : RelativeTime.Short(_last.CreatedAt, DateTimeOffset.Now);
        }
    }
}
