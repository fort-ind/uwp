using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialUserItem : BindableBase, ISocialFeedEntry
    {
        private const int AvatarDecodeSize = 40;

        private SocialUserDetail _detail;

        private bool _isBusy;

        private ImageSource _avatar;

        private bool _avatarCreated;

        private SocialFollowRequestState _requestState;

        private bool _moderationActive = true;

        private SocialUserItem(string pagingId, SocialUserDetail detail, bool isRequest)
        {
            PagingId = pagingId;
            IsRequest = isRequest;
            _requestState = isRequest ? SocialFollowRequestState.Pending : SocialFollowRequestState.None;
            _detail = detail;
            NameSegments = MfmText.ParseName(detail.User.DisplayName, detail.User.Emojis);
        }

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

        public bool IsRequest { get; private set; }

        public SocialModerationList? Moderation { get; private set; }

        public DateTimeOffset? ModerationExpiresAt { get; private set; }

        public bool ModerationActive
        {
            get { return _moderationActive; }
            set
            {
                if (SetProperty(ref _moderationActive, value)) OnPropertyChanged("AutomationName");
            }
        }

        public SocialFollowRequestState RequestState
        {
            get { return _requestState; }
            set
            {
                if (SetProperty(ref _requestState, value)) OnPropertyChanged("AutomationName");
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set { SetProperty(ref _isBusy, value); }
        }

        public string AutomationName
        {
            get
            {
                if (Moderation.HasValue)
                {
                    var status = SocialModerationText.StatusOf(this);
                    return string.IsNullOrEmpty(status)
                           ? LocalizedStrings.Format("SocialUserRowAutomationFormat", DisplayName, Handle)
                           : LocalizedStrings.Format("SocialUserRowRelationAutomationFormat", DisplayName, Handle, status);
                }

                if (IsRequest)
                {
                    var outcome = SocialFollowRequestService.OutcomeText(_requestState);
                    return string.IsNullOrEmpty(outcome)
                           ? LocalizedStrings.Format("SocialFollowRequestRowAutomationFormat", DisplayName, Handle)
                           : LocalizedStrings.Format("SocialUserRowRelationAutomationFormat", DisplayName, Handle, outcome);
                }

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

        public static IReadOnlyList<SocialUserItem> CreateAll(IReadOnlyList<SocialFollowEntry> entries, bool requests = false)
        {
            if (entries == null) return new List<SocialUserItem>();

            return entries.Where(entry => entry != null && entry.User != null && entry.User.User != null)
                          .Select(entry => new SocialUserItem(entry.Id, entry.User, requests))
                          .ToList();
        }

        public static IReadOnlyList<SocialUserItem> CreateAll(IReadOnlyList<SocialModerationEntry> entries, SocialModerationList list)
        {
            if (entries == null) return new List<SocialUserItem>();

            return entries.Where(entry => entry != null && entry.User != null && entry.User.User != null)
                          .Select(entry => new SocialUserItem(entry.Id, entry.User, false)
                          {
                              Moderation = list,
                              ModerationExpiresAt = entry.ExpiresAt
                          })
                          .ToList();
        }
    }
}
