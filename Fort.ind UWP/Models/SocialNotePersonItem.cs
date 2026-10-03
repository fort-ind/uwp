using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialNotePersonItem : ISocialFeedEntry
    {
        private const int AvatarDecodeSize = 32;

        private const int ReactionDecodeSize = 20;

        private ImageSource _avatar;

        private bool _avatarCreated;

        private ImageSource _reactionImage;

        private bool _reactionImageCreated;

        private Uri _reactionUri;

        private SocialNotePersonItem()
        {
        }

        public string PagingId { get; private set; }

        public SocialUser User { get; private set; }

        public string DisplayName { get; private set; }

        public string Handle { get; private set; }

        public string ReactionKey { get; private set; }

        public string ReactionText { get; private set; }

        public string TimeText { get; private set; }

        public string TimeFull { get; private set; }

        public string AutomationName { get; private set; }

        public Visibility ReactionTextVisibility
        {
            get { return !string.IsNullOrEmpty(ReactionText) && _reactionUri == null ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ReactionImageVisibility
        {
            get { return _reactionUri != null ? Visibility.Visible : Visibility.Collapsed; }
        }

        public ImageSource Avatar
        {
            get
            {
                if (_avatarCreated) return _avatar;
                _avatarCreated = true;

                var uri = User == null ? null : WebLauncher.TryCreateFetchUri(User.AvatarUrl);
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

        public ImageSource ReactionImage
        {
            get
            {
                if (_reactionImageCreated) return _reactionImage;
                _reactionImageCreated = true;

                if (_reactionUri == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelHeight = ReactionDecodeSize;
                bitmap.UriSource = _reactionUri;
                _reactionImage = bitmap;
                return _reactionImage;
            }
        }

        public static SocialNotePersonItem FromRenote(SocialNote renote)
        {
            if (renote == null || renote.User == null) return null;

            var item = Create(renote.Id, renote.User, renote.CreatedAt);
            item.AutomationName = LocalizedStrings.Format("SocialThreadRenoterAutomationFormat",
                                                          item.DisplayName + " " + item.Handle, item.TimeFull);
            return item;
        }

        public static SocialNotePersonItem FromReaction(SocialReactionEntry entry, IReadOnlyDictionary<string, string> reactionEmojis,
                                                        IReadOnlyDictionary<string, Uri> localEmojis)
        {
            if (entry == null || entry.User == null) return null;

            var item = Create(entry.Id, entry.User, entry.CreatedAt);
            item.ReactionKey = entry.Type;
            item._reactionUri = SocialReactions.ImageUri(entry.Type, reactionEmojis, localEmojis);
            item.ReactionText = SocialReactions.SpokenName(entry.Type);
            item.AutomationName = LocalizedStrings.Format("SocialThreadReactorAutomationFormat",
                                                          item.DisplayName + " " + item.Handle, item.ReactionText, item.TimeFull);
            return item;
        }

        private static SocialNotePersonItem Create(string pagingId, SocialUser user, DateTimeOffset createdAt)
        {
            return new SocialNotePersonItem
            {
                PagingId = pagingId,
                User = user,
                DisplayName = SocialNoteItem.DisplayNameOf(user),
                Handle = user.Handle,
                TimeText = RelativeTime.Short(createdAt, DateTimeOffset.Now),
                TimeFull = RelativeTime.Full(createdAt)
            };
        }
    }
}
