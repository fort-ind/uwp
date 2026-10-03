using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialUserItem : INotifyPropertyChanged, ISocialFeedEntry
    {
        private const int AvatarDecodeSize = 40;

        private SocialUserDetail _detail;

        private bool _isBusy;

        private ImageSource _avatar;

        private bool _avatarCreated;

        private SocialUserItem(string pagingId, SocialUserDetail detail)
        {
            PagingId = pagingId;
            _detail = detail;
            NameSegments = MfmText.ParseName(detail.User.DisplayName, detail.User.Emojis);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string PagingId { get; private set; }

        public SocialUserDetail Detail
        {
            get { return _detail; }
        }

        public SocialUser User
        {
            get { return _detail.User; }
        }

        public IReadOnlyList<MfmSegment> NameSegments { get; private set; }

        public string DisplayName
        {
            get { return SocialNoteItem.DisplayNameOf(_detail.User); }
        }

        public string Handle
        {
            get
            {
                var user = _detail.User;
                return SocialLinks.FormatHandle(user.Username, string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host);
            }
        }

        public SocialFollowState FollowState
        {
            get { return SocialFollowService.StateOf(_detail); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        public string AutomationName
        {
            get
            {
                var state = FollowState;
                return state == SocialFollowState.None || state == SocialFollowState.Follow || state == SocialFollowState.RequestToFollow
                       ? LocalizedStrings.Format("SocialUserRowAutomationFormat", DisplayName, Handle)
                       : LocalizedStrings.Format("SocialUserRowRelationAutomationFormat", DisplayName, Handle, SocialFollowService.LabelFor(state));
            }
        }

        public ImageSource Avatar
        {
            get
            {
                if (_avatarCreated) return _avatar;
                _avatarCreated = true;

                var uri = WebLauncher.TryCreateFetchUri(_detail.User.AvatarUrl);
                if (uri == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = AvatarDecodeSize;
                bitmap.DecodePixelHeight = AvatarDecodeSize;
                bitmap.UriSource = uri;
                _avatar = bitmap;
                return _avatar;
            }
        }

        public bool Apply(SocialUserDetail updated)
        {
            if (updated == null || !string.Equals(updated.User.Id, _detail.User.Id, StringComparison.Ordinal)) return false;

            _detail = updated;
            OnPropertyChanged("Detail");
            OnPropertyChanged("FollowState");
            OnPropertyChanged("AutomationName");
            return true;
        }

        public void RefreshFollowState()
        {
            OnPropertyChanged("FollowState");
            OnPropertyChanged("AutomationName");
        }

        public static IReadOnlyList<SocialUserItem> CreateAll(IReadOnlyList<SocialFollowEntry> entries)
        {
            var items = new List<SocialUserItem>();
            if (entries == null) return items;

            foreach (var entry in entries)
            {
                if (entry == null || entry.User == null || entry.User.User == null) continue;
                items.Add(new SocialUserItem(entry.Id, entry.User));
            }

            return items;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
