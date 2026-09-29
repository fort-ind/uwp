using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Windows.Globalization.DateTimeFormatting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialFeedItem : INotifyPropertyChanged
    {
        private const int ExcerptTextElements = 280;

        private const int AvatarDecodeSize = 40;

        private static readonly Lazy<DateTimeFormatter> s_timeFormatter =
            new Lazy<DateTimeFormatter>(() => new DateTimeFormatter("shorttime"));

        private static readonly Lazy<DateTimeFormatter> s_dateFormatter =
            new Lazy<DateTimeFormatter>(() => new DateTimeFormatter("shortdate"));

        private bool _isUnread;

        private ImageSource _avatar;

        private bool _avatarCreated;

        private SocialFeedItem()
        {
        }

        public string Id { get; private set; }

        public string Glyph { get; private set; }

        public string Title { get; private set; }

        public string Body { get; private set; }

        public bool IsDirect { get; private set; }

        public string TimeText { get; private set; }

        public string ActorName { get; private set; }

        public Uri AvatarUri { get; private set; }

        public string TargetUrl { get; private set; }

        public Visibility BodyVisibility
        {
            get { return string.IsNullOrEmpty(Body) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ActorVisibility
        {
            get { return string.IsNullOrEmpty(ActorName) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility GlyphPlateVisibility
        {
            get { return string.IsNullOrEmpty(ActorName) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public ImageSource Avatar
        {
            get
            {
                if (_avatarCreated) return _avatar;
                _avatarCreated = true;

                if (AvatarUri == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = AvatarDecodeSize;
                bitmap.DecodePixelHeight = AvatarDecodeSize;
                bitmap.UriSource = AvatarUri;
                _avatar = bitmap;
                return _avatar;
            }
        }

        public bool IsUnread
        {
            get { return _isUnread; }
            set
            {
                if (_isUnread == value) return;
                _isUnread = value;
                OnPropertyChanged();
                OnPropertyChanged("UnreadVisibility");
                OnPropertyChanged("AutomationName");
            }
        }

        public Visibility UnreadVisibility
        {
            get { return _isUnread ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string AutomationName
        {
            get
            {
                var content = string.IsNullOrEmpty(Body)
                              ? Title
                              : LocalizedStrings.Format("SocialFeedItemContentFormat", Title, Body);

                return LocalizedStrings.Format(_isUnread ? "SocialFeedItemUnreadAutomationFormat" : "SocialFeedItemAutomationFormat",
                                               content, TimeText);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static SocialFeedItem FromNotification(SocialNotification notification, bool isUnread)
        {
            if (notification == null) return null;

            var user = notification.User;
            var actor = user == null ? null : user.DisplayName;

            var item = new SocialFeedItem
            {
                Id = notification.Id,
                Glyph = GlyphFor(notification.Type),
                Title = TitleFor(notification, actor),
                Body = BodyFor(notification),
                IsDirect = notification.Type != "app" && IsDirectNote(notification.Note),
                TimeText = FormatTime(notification.CreatedAt),
                ActorName = actor,
                AvatarUri = user == null ? null : WebLauncher.TryCreateFetchUri(user.AvatarUrl),
                TargetUrl = TargetUrlFor(notification),
                _isUnread = isUnread
            };

            return item;
        }

        public static SocialFeedItem FromNote(SocialNote note)
        {
            if (note == null) return null;

            var user = note.User;
            var actor = user == null ? LocalizedStrings.Get("SocialUnknownUser") : user.DisplayName;

            return new SocialFeedItem
            {
                Id = note.Id,
                Glyph = "",
                Title = actor,
                Body = Excerpt(note),
                IsDirect = IsDirectNote(note),
                TimeText = FormatTime(note.CreatedAt),
                ActorName = actor,
                AvatarUri = user == null ? null : WebLauncher.TryCreateFetchUri(user.AvatarUrl),
                TargetUrl = NoteUrl(note.Id)
            };
        }

        private static string GlyphFor(string type)
        {
            switch (type)
            {
                case "reaction": return "";
                case "follow":
                case "receiveFollowRequest": return "";
                case "followRequestAccepted": return "";
                case "mention": return "";
                case "reply": return "";
                case "renote": return "";
                case "quote": return "";
                case "note": return "";
                case "edited": return "";
                case "pollEnded":
                case "pollVote": return "";
                case "achievementEarned": return "";
                case "roleAssigned":
                case "createToken": return "";
                case "login": return "";
                case "app": return "";
                case "exportCompleted": return "";
                case "scheduledNotePosted": return "";
                case "scheduledNoteFailed": return "";
                case "chatRoomInvitationReceived":
                case "groupInvited": return "";
                default: return "";
            }
        }

        private static string TitleFor(SocialNotification notification, string actor)
        {
            var name = actor ?? LocalizedStrings.Get("SocialUnknownUser");

            switch (notification.Type)
            {
                case "follow": return LocalizedStrings.Format("SocialNotificationFollowFormat", name);
                case "receiveFollowRequest": return LocalizedStrings.Format("SocialNotificationFollowRequestFormat", name);
                case "followRequestAccepted": return LocalizedStrings.Format("SocialNotificationFollowAcceptedFormat", name);
                case "mention": return LocalizedStrings.Format("SocialNotificationMentionFormat", name);
                case "reply": return LocalizedStrings.Format("SocialNotificationReplyFormat", name);
                case "renote": return LocalizedStrings.Format("SocialNotificationRenoteFormat", name);
                case "quote": return LocalizedStrings.Format("SocialNotificationQuoteFormat", name);
                case "reaction": return LocalizedStrings.Format("SocialNotificationReactionFormat", name, ReactionText(notification.Reaction));
                case "note": return LocalizedStrings.Format("SocialNotificationNoteFormat", name);
                case "edited": return LocalizedStrings.Format("SocialNotificationEditedFormat", name);
                case "pollVote": return LocalizedStrings.Format("SocialNotificationPollVoteFormat", name);
                case "pollEnded": return LocalizedStrings.Get("SocialNotificationPollEnded");
                case "achievementEarned": return LocalizedStrings.Get("SocialNotificationAchievement");
                case "roleAssigned":
                    return string.IsNullOrWhiteSpace(notification.RoleName)
                           ? LocalizedStrings.Get("SocialNotificationRoleAssigned")
                           : LocalizedStrings.Format("SocialNotificationRoleAssignedFormat", notification.RoleName);
                case "login": return LocalizedStrings.Get("SocialNotificationLogin");
                case "createToken": return LocalizedStrings.Get("SocialNotificationCreateToken");
                case "exportCompleted": return LocalizedStrings.Get("SocialNotificationExportCompleted");
                case "scheduledNotePosted": return LocalizedStrings.Get("SocialNotificationScheduledNotePosted");
                case "scheduledNoteFailed": return LocalizedStrings.Get("SocialNotificationScheduledNoteFailed");
                case "chatRoomInvitationReceived":
                case "groupInvited": return LocalizedStrings.Get("SocialNotificationChatInvite");
                case "test": return LocalizedStrings.Get("SocialNotificationTest");
                case "app":
                    return string.IsNullOrWhiteSpace(notification.AppHeader)
                           ? LocalizedStrings.Get("SocialNotificationApp")
                           : Collapse(notification.AppHeader);
                default: return LocalizedStrings.Get("SocialNotificationGeneric");
            }
        }

        private static string BodyFor(SocialNotification notification)
        {
            if (notification.Type == "app") return Truncate(Collapse(notification.AppBody));

            return Excerpt(notification.Note);
        }

        private static string TargetUrlFor(SocialNotification notification)
        {
            switch (notification.Type)
            {
                case "follow":
                case "followRequestAccepted":
                    return UserUrl(notification.User) ?? InstanceUrl("/my/notifications");
                case "receiveFollowRequest":
                    return InstanceUrl("/my/follow-requests");
                case "achievementEarned":
                    return InstanceUrl("/my/achievements");
                case "renote":
                    {
                        var note = notification.Note;
                        if (note != null && note.Renote != null) return NoteUrl(note.Renote.Id);
                        return note == null ? InstanceUrl("/my/notifications") : NoteUrl(note.Id);
                    }
                default:
                    return notification.Note != null
                           ? NoteUrl(notification.Note.Id)
                           : InstanceUrl("/my/notifications");
            }
        }

        private static string Excerpt(SocialNote note)
        {
            if (note == null) return "";

            if (!string.IsNullOrWhiteSpace(note.ContentWarning))
            {
                return Truncate(Collapse(note.ContentWarning));
            }

            if (!string.IsNullOrWhiteSpace(note.Text))
            {
                return Truncate(Collapse(note.Text));
            }

            if (note.Renote != null)
            {
                return Excerpt(note.Renote);
            }

            return "";
        }

        private static bool IsDirectNote(SocialNote note)
        {
            if (note == null) return false;

            return note.IsDirect || (note.Renote != null && note.Renote.IsDirect);
        }

        private static string ReactionText(string reaction)
        {
            if (string.IsNullOrEmpty(reaction)) return "";

            if (reaction.Length > 2 && reaction[0] == ':' && reaction[reaction.Length - 1] == ':')
            {
                var name = reaction.Substring(1, reaction.Length - 2);
                var at = name.IndexOf('@');
                if (at > 0) name = name.Substring(0, at);
                return ":" + name + ":";
            }

            return reaction;
        }

        private static string Collapse(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            StringBuilder builder = new StringBuilder(text.Length);
            bool pendingSpace = false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var head = TextHelper.FirstTextElements(text, ExcerptTextElements);
            return head.Length < text.Length ? head + "…" : head;
        }

        private static string FormatTime(DateTimeOffset createdAt)
        {
            if (createdAt == DateTimeOffset.MinValue) return "";

            try
            {
                var local = createdAt.ToLocalTime();
                return local.Date == DateTimeOffset.Now.Date
                       ? s_timeFormatter.Value.Format(local)
                       : s_dateFormatter.Value.Format(local);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedItem: could not format a timestamp - {ex.Message}");
                return "";
            }
        }

        private static string InstanceUrl(string path)
        {
            return "https://" + MisskeyAuthService.InstanceHost + path;
        }

        private static string NoteUrl(string noteId)
        {
            if (string.IsNullOrWhiteSpace(noteId)) return InstanceUrl("/my/notifications");
            return InstanceUrl("/notes/" + Uri.EscapeDataString(noteId));
        }

        private static string UserUrl(SocialUser user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Username)) return null;

            var handle = "/@" + Uri.EscapeDataString(user.Username);
            if (!string.IsNullOrWhiteSpace(user.Host) && Uri.CheckHostName(user.Host) != UriHostNameType.Unknown)
            {
                handle += "@" + user.Host;
            }

            return InstanceUrl(handle);
        }
    }
}
