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

        private SocialChatRoom _room;

        private SocialChatMessage _last;

        private bool _isUnread;

        private string _timeText;

        private ImageSource _avatar;

        private string _avatarUrl;

        public SocialChatConversation(string account, string otherUserId, SocialUser other, SocialChatMessage last, bool isUnread)
        {
            _account = account;
            Key = otherUserId;
            _other = other;
            _last = last;
            _isUnread = isUnread;
            NameSegments = MfmText.ParseName(DisplayName, other == null ? null : other.Emojis);
            RefreshTime();
        }

        private SocialChatConversation(string account, SocialChatRoom room, SocialChatMessage last, bool isUnread)
        {
            _account = account;
            Key = SocialChatKeys.ForRoom(room.Id);
            _room = room;
            _last = last;
            _isUnread = isUnread;
            NameSegments = MfmText.ParseName(DisplayName, null);
            RefreshTime();
        }

        public static SocialChatConversation ForRoom(string account, SocialChatRoom room, SocialChatMessage last, bool isUnread)
        {
            return room == null ? null : new SocialChatConversation(account, room, last, isUnread);
        }

        public string Key { get; private set; }

        public bool IsRoom
        {
            get { return SocialChatKeys.IsRoom(Key); }
        }

        public SocialUser Other
        {
            get { return _other; }
        }

        public SocialChatRoom Room
        {
            get { return _room; }
        }

        public SocialChatMessage LastMessage
        {
            get { return _last; }
        }

        public DateTimeOffset SortTime
        {
            get { return _last != null ? _last.CreatedAt : _room != null ? _room.CreatedAt : DateTimeOffset.MinValue; }
        }

        public SocialChatArgs Args
        {
            get { return IsRoom ? SocialChatArgs.ForRoom(_account, _room.Id, _room) : new SocialChatArgs(_account, Key, _other); }
        }

        public IReadOnlyList<MfmSegment> NameSegments { get; private set; }

        public string DisplayName
        {
            get
            {
                if (IsRoom) return _room == null ? "" : _room.DisplayName;
                return _other == null ? "" : SocialNoteItem.DisplayNameOf(_other);
            }
        }

        public string PlainName
        {
            get { return MfmText.PlainName(DisplayName); }
        }

        public string Preview
        {
            get
            {
                if (_last == null) return IsRoom ? LocalizedStrings.Get("SocialChatRoomNoMessages") : "";

                var body = SocialChatToasts.BodyFor(_last);
                if (body.Length > AppConstants.SocialChatPreviewLength) body = body.Substring(0, AppConstants.SocialChatPreviewLength);
                if (_last.IsFrom(_account)) return LocalizedStrings.Format("SocialChatPreviewMineFormat", body);
                if (!IsRoom) return body;

                var sender = _last.FromUser == null ? null : MfmText.PlainName(SocialNoteItem.DisplayNameOf(_last.FromUser));
                return string.IsNullOrWhiteSpace(sender) ? body : LocalizedStrings.Format("SocialChatRoomPreviewFormat", sender, body);
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

        public Visibility AvatarVisibility
        {
            get { return IsRoom ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility RoomPlateVisibility
        {
            get { return IsRoom ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility MutedVisibility
        {
            get { return _room != null && _room.IsMuted ? Visibility.Visible : Visibility.Collapsed; }
        }

        public FontWeight NameWeight
        {
            get { return _isUnread ? FontWeights.SemiBold : FontWeights.Normal; }
        }

        public string AutomationName
        {
            get
            {
                var key = _isUnread ? "SocialChatRowUnreadAutomationFormat" : "SocialChatRowAutomationFormat";
                var name = IsRoom ? LocalizedStrings.Format("SocialChatRoomAutomationNameFormat", PlainName) : PlainName;
                return LocalizedStrings.Format(key, name, Preview, TimeText ?? "");
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
            var user = IsRoom ? null : message.OtherUser(_account);
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
            if (user == null || _other != null || IsRoom) return;

            _other = user;
            NameSegments = MfmText.ParseName(DisplayName, user.Emojis);
            OnPropertyChanged("NameSegments");
            OnPropertyChanged("DisplayName");
            OnPropertyChanged("PlainName");
            OnPropertyChanged("Avatar");
            OnPropertyChanged("AutomationName");
        }

        public void SetRoom(SocialChatRoom room)
        {
            if (room == null || !IsRoom) return;

            _room = room;
            NameSegments = MfmText.ParseName(DisplayName, null);
            OnPropertyChanged("NameSegments");
            OnPropertyChanged("DisplayName");
            OnPropertyChanged("PlainName");
            OnPropertyChanged("MutedVisibility");
            OnPropertyChanged("AutomationName");
        }

        public void RefreshTime()
        {
            var time = SortTime;
            TimeText = time == DateTimeOffset.MinValue ? "" : RelativeTime.Short(time, DateTimeOffset.Now);
        }
    }
}
