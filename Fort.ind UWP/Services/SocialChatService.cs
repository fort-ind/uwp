using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public enum SocialChatChangeKind
    {
        Message,
        Deleted,
        Reacted,
        Read,
        Reset,
        Rooms,
        Invitations
    }

    public sealed class SocialChatChange
    {
        private SocialChatChange()
        {
        }

        public SocialChatChangeKind Kind { get; private set; }

        public string Key { get; private set; }

        public SocialChatMessage Message { get; private set; }

        public string MessageId { get; private set; }

        public string Reaction { get; private set; }

        public bool ReactionAdded { get; private set; }

        public string ReactionUserId { get; private set; }

        public SocialChatRoom Room { get; private set; }

        internal static SocialChatChange ForMessage(string key, SocialChatMessage message)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Message, Key = key, Message = message, MessageId = message.Id };
        }

        internal static SocialChatChange ForDeleted(string key, string messageId)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Deleted, Key = key, MessageId = messageId };
        }

        internal static SocialChatChange ForReaction(string key, string messageId, string reaction, bool added, string userId)
        {
            return new SocialChatChange
            {
                Kind = SocialChatChangeKind.Reacted,
                Key = key,
                MessageId = messageId,
                Reaction = reaction,
                ReactionAdded = added,
                ReactionUserId = userId
            };
        }

        internal static SocialChatChange ForRead(string key)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Read, Key = key };
        }

        internal static SocialChatChange ForRoom(string roomId, SocialChatRoom room)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Rooms, Key = SocialChatKeys.ForRoom(roomId), Room = room };
        }

        internal static SocialChatChange ForInvitations()
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Invitations };
        }

        internal static SocialChatChange ForReset()
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Reset };
        }
    }

    public sealed class SocialChatTimelinePage
    {
        internal SocialChatTimelinePage(IReadOnlyList<SocialChatMessage> messages, bool reachedOldest, bool olderUnavailable)
        {
            Messages = messages ?? new SocialChatMessage[0];
            ReachedOldest = reachedOldest;
            OlderUnavailable = olderUnavailable;
        }

        public IReadOnlyList<SocialChatMessage> Messages { get; }

        public bool ReachedOldest { get; }

        public bool OlderUnavailable { get; }
    }

    public static class SocialChatService
    {
        private const string InternalErrorCode = "INTERNAL_ERROR";

        private const string ScopeNone = "none";

        private const string ScopeFollowers = "followers";

        private const string ScopeFollowing = "following";

        private const string ScopeMutual = "mutual";

        private const string InvitationNotification = "chatRoomInvitationReceived";

        private static readonly object s_lock = new object();

        private static readonly HashSet<string> s_unread = new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> s_seen = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Queue<string> s_seenOrder = new Queue<string>();

        private static readonly Dictionary<int, string> s_active = new Dictionary<int, string>();

        private static readonly Dictionary<string, string> s_drafts = new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, SocialChatRoom> s_rooms = new Dictionary<string, SocialChatRoom>(StringComparer.Ordinal);

        private static IReadOnlyList<SocialChatInvitation> s_invitations = new SocialChatInvitation[0];

        private static bool s_initialized;

        private static string s_accountId;

        private static bool s_labWasOn;

        public static event EventHandler<SocialChatChange> Changed;

        public static event EventHandler UnreadChanged;

        public static bool IsAvailable
        {
            get { return LabsService.ChatEnabled && SocialContentService.CurrentAccountId() != null; }
        }

        public static int UnreadCount
        {
            get
            {
                if (!LabsService.ChatEnabled) return 0;

                lock (s_lock)
                {
                    return s_unread.Count + s_invitations.Count;
                }
            }
        }

        public static int UnreadPeopleCount
        {
            get
            {
                if (!LabsService.ChatEnabled) return 0;

                lock (s_lock)
                {
                    return s_unread.Count(key => !SocialChatKeys.IsRoom(key));
                }
            }
        }

        public static int UnreadRoomsCount
        {
            get
            {
                if (!LabsService.ChatEnabled) return 0;

                lock (s_lock)
                {
                    return s_unread.Count(SocialChatKeys.IsRoom) + s_invitations.Count;
                }
            }
        }

        public static IReadOnlyList<SocialChatInvitation> Invitations
        {
            get
            {
                lock (s_lock)
                {
                    return s_invitations;
                }
            }
        }

        public static bool IsUnread(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            lock (s_lock)
            {
                return s_unread.Contains(key);
            }
        }

        public static SocialChatRoom RoomFor(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;

            lock (s_lock)
            {
                SocialChatRoom room;
                return s_rooms.TryGetValue(roomId, out room) ? room : null;
            }
        }

        private static void Remember(SocialChatRoom room)
        {
            if (room == null) return;

            lock (s_lock)
            {
                s_rooms[room.Id] = room;
            }
        }

        public static void Initialize()
        {
            lock (s_lock)
            {
                if (s_initialized) return;
                s_initialized = true;
                s_accountId = SocialContentService.CurrentAccountId();
                s_labWasOn = LabsService.ChatEnabled;
            }

            ProfileService.AuthStateChanged += OnAuthStateChanged;
            LabsService.ChatChanged += OnLabChanged;
            SocialNotificationService.NotificationArrived += OnNotificationArrived;

            RefreshUnreadInBackground();
        }

        public static void OnResuming()
        {
            RefreshUnreadInBackground();
        }

        public static async void RefreshUnreadInBackground()
        {
            try
            {
                await FetchHistoryAsync(false, CancellationToken.None);
                await FetchHistoryAsync(true, CancellationToken.None);
                await FetchInvitationsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: could not refresh the unread chats", ex);
            }
        }

        public static async void RefreshInvitationsInBackground()
        {
            try
            {
                await FetchInvitationsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: could not refresh the invitations", ex);
            }
        }

        private static bool CanRead(out string account)
        {
            account = SocialContentService.CurrentAccountId();
            return LabsService.ChatEnabled && account != null && SocialPermissions.Has(SocialPermissions.ReadChat);
        }

        private static void ObserveRead<T>(SocialApiResult<T> result)
        {
            if (result.Status == SocialApiStatus.PermissionDenied) SocialPermissions.Forget(SocialPermissions.ReadChat);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialChatMessage>>> FetchHistoryAsync(bool rooms, CancellationToken cancellationToken)
        {
            string account;
            if (!CanRead(out account)) return SocialApiResult<IReadOnlyList<SocialChatMessage>>.Failed(SocialApiStatus.PermissionDenied);

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetChatHistoryAsync(token, AppConstants.SocialChatHistoryLimit, rooms, cancellationToken);
            if (!SocialContentService.IsCurrentAccount(account)) return SocialApiResult<IReadOnlyList<SocialChatMessage>>.Failed(SocialApiStatus.Failed);

            ObserveRead(result);
            if (result.Status == SocialApiStatus.Ok)
            {
                foreach (var room in result.Value.Select(message => message.ToRoom).Where(room => room != null))
                {
                    Remember(room);
                }

                ApplyHistory(account, result.Value, rooms);
            }

            return result;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialChatRoom>>> FetchRoomsAsync(CancellationToken cancellationToken)
        {
            string account;
            if (!CanRead(out account)) return SocialApiResult<IReadOnlyList<SocialChatRoom>>.Failed(SocialApiStatus.PermissionDenied);

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var owned = await SocialApiService.GetOwnedChatRoomsAsync(token, cancellationToken);
            ObserveRead(owned);
            if (owned.Status != SocialApiStatus.Ok) return owned;

            var joined = await SocialApiService.GetJoinedChatRoomsAsync(token, cancellationToken);
            ObserveRead(joined);
            if (joined.Status != SocialApiStatus.Ok) return SocialApiResult<IReadOnlyList<SocialChatRoom>>.Failed(joined.Status, joined.ErrorCode);
            if (!SocialContentService.IsCurrentAccount(account)) return SocialApiResult<IReadOnlyList<SocialChatRoom>>.Failed(SocialApiStatus.Failed);

            var rooms = owned.Value.Concat(joined.Value.Select(membership => membership.Room).Where(room => room != null))
                                   .GroupBy(room => room.Id, StringComparer.Ordinal)
                                   .Select(group => group.First())
                                   .ToList();
            foreach (var room in rooms)
            {
                Remember(room);
            }

            return SocialApiResult<IReadOnlyList<SocialChatRoom>>.Succeeded(rooms);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialChatInvitation>>> FetchInvitationsAsync(CancellationToken cancellationToken)
        {
            string account;
            if (!CanRead(out account)) return SocialApiResult<IReadOnlyList<SocialChatInvitation>>.Failed(SocialApiStatus.PermissionDenied);

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetChatInvitationsAsync(token, cancellationToken);
            if (!SocialContentService.IsCurrentAccount(account)) return SocialApiResult<IReadOnlyList<SocialChatInvitation>>.Failed(SocialApiStatus.Failed);

            ObserveRead(result);
            if (result.Status == SocialApiStatus.Ok) SetInvitations(result.Value);
            return result;
        }

        private static void SetInvitations(IReadOnlyList<SocialChatInvitation> invitations)
        {
            bool changed;
            lock (s_lock)
            {
                changed = invitations.Count != s_invitations.Count
                          || invitations.Any(invitation => !s_invitations.Any(known => string.Equals(known.Id, invitation.Id, StringComparison.Ordinal)));
                s_invitations = invitations;
            }

            if (!changed) return;

            Raise(SocialChatChange.ForInvitations());
            RaiseUnreadChanged();
        }

        public static async Task<SocialApiResult<SocialChatRoom>> FetchRoomAsync(string roomId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetChatRoomAsync(token, roomId, cancellationToken);
            ObserveRead(result);
            if (result.Status == SocialApiStatus.Ok) Remember(result.Value);
            return result;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialChatMembership>>> FetchMembersAsync(string roomId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetChatRoomMembersAsync(token, roomId, null, cancellationToken);
            ObserveRead(result);
            return result;
        }

        public static Task<SocialApiResult<IReadOnlyList<SocialChatMessage>>> FetchTimelineAsync(string key, string sinceId,
                                                                                              string untilId, CancellationToken cancellationToken)
        {
            return FetchTimelineAsync(key, sinceId, untilId, AppConstants.SocialChatPageSize, cancellationToken);
        }

        public static async Task<SocialApiResult<SocialChatTimelinePage>> FetchPageAsync(string key, string untilId,
                                                                                       CancellationToken cancellationToken)
        {
            var pageSize = AppConstants.SocialChatPageSize;
            var result = await FetchTimelineAsync(key, null, untilId, pageSize, cancellationToken);
            if (result.Status == SocialApiStatus.Ok)
            {
                return SocialApiResult<SocialChatTimelinePage>.Succeeded(
                    new SocialChatTimelinePage(result.Value, result.Value.Count < pageSize, false));
            }

            if (!IsServerFault(result)) return SocialApiResult<SocialChatTimelinePage>.Failed(result.Status, result.ErrorCode);

            IReadOnlyList<SocialChatMessage> loadable = null;
            var works = 0;
            var fails = pageSize;
            while (fails - works > 1)
            {
                var limit = (works + fails) / 2;
                var probe = await FetchTimelineAsync(key, null, untilId, limit, cancellationToken);
                if (probe.Status == SocialApiStatus.Ok)
                {
                    loadable = probe.Value;
                    if (probe.Value.Count < limit)
                    {
                        return SocialApiResult<SocialChatTimelinePage>.Succeeded(new SocialChatTimelinePage(loadable, true, false));
                    }

                    works = limit;
                }
                else if (IsServerFault(probe))
                {
                    fails = limit;
                }
                else
                {
                    return SocialApiResult<SocialChatTimelinePage>.Failed(probe.Status, probe.ErrorCode);
                }
            }

            AppLog.Warning($"SocialChatService: the server fails on message {works + 1} of this page, so {works} newer ones are shown and older ones are left out");
            return SocialApiResult<SocialChatTimelinePage>.Succeeded(new SocialChatTimelinePage(loadable, true, true));
        }

        private static bool IsServerFault<T>(SocialApiResult<T> result)
        {
            return result.Status == SocialApiStatus.Failed && string.Equals(result.ErrorCode, InternalErrorCode, StringComparison.Ordinal);
        }

        private static async Task<SocialApiResult<IReadOnlyList<SocialChatMessage>>> FetchTimelineAsync(string key, string sinceId,
                                                                                                     string untilId, int limit,
                                                                                                     CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = SocialChatKeys.IsRoom(key)
                         ? await SocialApiService.GetChatRoomTimelineAsync(token, SocialChatKeys.RoomIdOf(key), sinceId, untilId,
                                                                           limit, cancellationToken)
                         : await SocialApiService.GetChatTimelineAsync(token, key, sinceId, untilId, limit, cancellationToken);
            ObserveRead(result);
            if (result.Status == SocialApiStatus.Ok) MarkRead(key);
            return result;
        }

        public static void OfferHasUnread(string account, bool hasUnread)
        {
            if (!LabsService.ChatEnabled || !SocialContentService.IsCurrentAccount(account)) return;

            bool changed = false;
            bool refresh;
            lock (s_lock)
            {
                refresh = hasUnread && s_unread.Count == 0;
                if (!hasUnread && s_unread.Count > 0)
                {
                    s_unread.Clear();
                    changed = true;
                }
            }

            if (changed) RaiseUnreadChanged();
            if (refresh) RefreshUnreadInBackground();
        }

        private static void ApplyHistory(string account, IReadOnlyList<SocialChatMessage> history, bool rooms)
        {
            var unread = history.Where(message => message.IsRoomMessage == rooms
                                                  && message.IsKnownUnread
                                                  && !message.IsFrom(account))
                                .Select(message => SocialChatKeys.Of(message, account))
                                .Where(key => !string.IsNullOrEmpty(key) && !IsActive(key))
                                .ToList();

            bool changed;
            lock (s_lock)
            {
                var current = s_unread.Where(key => SocialChatKeys.IsRoom(key) == rooms).ToList();
                changed = unread.Count != current.Count || unread.Any(key => !s_unread.Contains(key));
                if (changed)
                {
                    s_unread.RemoveWhere(key => SocialChatKeys.IsRoom(key) == rooms);
                    s_unread.UnionWith(unread);
                }
            }

            if (changed) RaiseUnreadChanged();
        }

        public static void OfferNotification(string type)
        {
            if (LabsService.ChatEnabled && string.Equals(type, InvitationNotification, StringComparison.Ordinal))
            {
                RefreshInvitationsInBackground();
            }
        }

        private static void OnNotificationArrived(object sender, SocialNotification notification)
        {
            if (notification != null) OfferNotification(notification.Type);
        }

        public static void ApplyFromMainChannel(SocialChatMessage message)
        {
            if (message == null || !LabsService.ChatEnabled) return;

            var account = SocialContentService.CurrentAccountId();
            if (account == null) return;

            if (message.ToRoom != null) Remember(message.ToRoom);
            ApplyIncoming(message);

            var key = SocialChatKeys.Of(message, account);
            if (!message.IsFrom(account) && !IsActive(key)) SocialChatToasts.ToastFromStream(message, account);
        }

        public static void ApplyIncoming(SocialChatMessage message)
        {
            if (message == null || !LabsService.ChatEnabled) return;

            var account = SocialContentService.CurrentAccountId();
            if (account == null) return;

            var key = SocialChatKeys.Of(message, account);
            if (string.IsNullOrEmpty(key)) return;

            var room = message.IsRoomMessage ? RoomFor(message.ToRoomId) : null;
            var counts = !message.IsFrom(account) && (room == null || !room.IsMuted);
            bool unreadChanged = false;
            lock (s_lock)
            {
                if (!Remember(message.Id)) return;

                if (counts && !s_active.ContainsValue(key))
                {
                    unreadChanged = s_unread.Add(key);
                }
            }

            Raise(SocialChatChange.ForMessage(key, message));
            if (unreadChanged) RaiseUnreadChanged();
        }

        public static void ApplySent(string key, SocialChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(key)) return;

            lock (s_lock)
            {
                if (!Remember(message.Id)) return;
            }

            Raise(SocialChatChange.ForMessage(key, message));
        }

        public static void ApplyDeleted(string key, string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) return;

            Raise(SocialChatChange.ForDeleted(key, messageId));
        }

        public static void ApplyReaction(string key, string messageId, string reaction, bool added, string userId)
        {
            if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(reaction)) return;

            Raise(SocialChatChange.ForReaction(key, messageId, reaction, added, SocialChatKeys.IsRoom(key) ? userId : null));
        }

        public static void MarkRead(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            bool changed;
            lock (s_lock)
            {
                changed = s_unread.Remove(key);
            }

            SocialChatToasts.Remove(key);
            Raise(SocialChatChange.ForRead(key));
            if (changed) RaiseUnreadChanged();
        }

        public static void SetActive(int viewId, string key)
        {
            lock (s_lock)
            {
                if (string.IsNullOrEmpty(key))
                {
                    s_active.Remove(viewId);
                }
                else
                {
                    s_active[viewId] = key;
                }
            }
        }

        public static void ForgetView(int viewId)
        {
            SetActive(viewId, null);
        }

        public static bool IsActive(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            lock (s_lock)
            {
                return s_active.ContainsValue(key);
            }
        }

        public static void SaveDraft(string key, string text)
        {
            if (string.IsNullOrEmpty(key)) return;

            lock (s_lock)
            {
                if (string.IsNullOrEmpty(text))
                {
                    s_drafts.Remove(key);
                }
                else
                {
                    s_drafts[key] = text;
                }
            }
        }

        public static string DraftFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            lock (s_lock)
            {
                string text;
                return s_drafts.TryGetValue(key, out text) ? text : null;
            }
        }

        private static bool Remember(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) return true;
            if (!s_seen.Add(messageId)) return false;

            s_seenOrder.Enqueue(messageId);
            while (s_seenOrder.Count > AppConstants.SocialChatSeenIdLimit)
            {
                s_seen.Remove(s_seenOrder.Dequeue());
            }

            return true;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialUser>>> SearchPeopleAsync(string text, int limit,
                                                                                             CancellationToken cancellationToken)
        {
            var query = (text ?? "").Trim().TrimStart('@');
            var at = query.IndexOf('@');
            if (at >= 0) query = query.Substring(0, at);
            if (query.Length == 0) return SocialApiResult<IReadOnlyList<SocialUser>>.Succeeded(new SocialUser[0]);

            var me = SocialContentService.CurrentAccountId();
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.SearchUsersAsync(token, query, null, limit * 2, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return result;

            IReadOnlyList<SocialUser> people = result.Value.Where(user => string.IsNullOrWhiteSpace(user.Host)
                                                                          && !string.Equals(user.Id, me, StringComparison.Ordinal))
                                                           .Take(limit)
                                                           .ToList();
            return SocialApiResult<IReadOnlyList<SocialUser>>.Succeeded(people);
        }

        public static bool CanStartWith(SocialUserDetail detail)
        {
            if (detail == null || detail.User == null || !LabsService.ChatEnabled) return false;

            var account = SocialContentService.CurrentAccountId();
            if (account == null || string.Equals(detail.User.Id, account, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrWhiteSpace(detail.User.Host)) return false;
            if (detail.IsSuspended || detail.ChatRefused) return false;

            return SocialPolicies.Current.AllowsChatWriting;
        }

        public static bool ScopeAllows(SocialUserDetail detail, bool theyMessagedYou)
        {
            if (detail == null) return true;
            if (detail.ChatRefused) return false;
            if (theyMessagedYou) return true;

            switch (detail.ChatScope)
            {
                case ScopeNone:
                    return false;
                case ScopeFollowers:
                    return detail.IsFollowing;
                case ScopeFollowing:
                    return detail.IsFollowed;
                case ScopeMutual:
                    return detail.IsFollowing && detail.IsFollowed;
                default:
                    return true;
            }
        }

        public static string ScopeRefusalText(SocialUserDetail detail)
        {
            var name = detail == null || detail.User == null ? "" : MfmText.PlainName(SocialNoteItem.DisplayNameOf(detail.User));
            if (detail != null && detail.ChatRefused) return LocalizedStrings.Format("SocialChatRefusedPolicyFormat", name);

            switch (detail == null ? null : detail.ChatScope)
            {
                case ScopeNone:
                    return LocalizedStrings.Format("SocialChatRefusedNoneFormat", name);
                case ScopeFollowers:
                    return LocalizedStrings.Format("SocialChatRefusedFollowersFormat", name);
                case ScopeFollowing:
                    return LocalizedStrings.Format("SocialChatRefusedFollowingFormat", name);
                case ScopeMutual:
                    return LocalizedStrings.Format("SocialChatRefusedMutualFormat", name);
                default:
                    return null;
            }
        }

        public static async Task<SocialApiResult<SocialChatMessage>> SendAsync(string key, string text, string fileId,
                                                                               CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = SocialChatKeys.IsRoom(key)
                         ? await SocialApiService.SendChatRoomMessageAsync(token, SocialChatKeys.RoomIdOf(key), text, fileId, cancellationToken)
                         : await SocialApiService.SendChatMessageAsync(token, key, text, fileId, cancellationToken);
            if (result.Status == SocialApiStatus.Ok)
            {
                ApplySent(key, result.Value);
            }
            else if (result.Status == SocialApiStatus.PermissionDenied)
            {
                SocialPermissions.Forget(SocialPermissions.WriteChat);
            }

            return result;
        }

        public static async Task<bool> ReactAsync(UIElement owner, string key, SocialChatMessage message, string reactionKey, bool add)
        {
            if (message == null || string.IsNullOrEmpty(reactionKey) || string.IsNullOrEmpty(message.Id)) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var reaction = SocialReactions.ToRequestKey(reactionKey);
            var me = SocialChatKeys.IsRoom(key) ? SocialContentService.CurrentAccountId() : null;
            if (message.HasReaction(reaction, me) == add) return true;

            ApplyReaction(key, message.Id, reaction, add, me);

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.ReactChatMessageAsync(token, message.Id, reaction, add, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok)
            {
                var spoken = SocialReactions.SpokenName(reaction);
                AutomationHelper.AnnounceStatus(owner, add ? LocalizedStrings.Format("SocialChatReactedAnnouncementFormat", spoken)
                                                           : LocalizedStrings.Format("SocialChatUnreactedAnnouncementFormat", spoken),
                                                "SocialChatReaction");
                return true;
            }

            ApplyReaction(key, message.Id, reaction, !add, me);

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat);
                return false;
            }

            await ShowFailureAsync(owner, add ? "SocialChatReactFailedTitle" : "SocialChatUnreactFailedTitle", result.Status);
            return false;
        }

        public static string SendFailureText(SocialApiStatus status, string errorCode)
        {
            switch (errorCode)
            {
                case "NO_SUCH_USER":
                    return LocalizedStrings.Get("SocialChatErrorNoSuchUser");
                case "NO_SUCH_ROOM":
                    return LocalizedStrings.Get("SocialChatErrorNoSuchRoom");
                case "YOU_HAVE_BEEN_BLOCKED":
                    return LocalizedStrings.Get("SocialChatErrorBlocked");
                case "CONTENT_REQUIRED":
                    return LocalizedStrings.Get("SocialChatErrorEmpty");
            }

            switch (status)
            {
                case SocialApiStatus.PermissionDenied:
                    return LocalizedStrings.Get("SocialChatErrorPermission");
                case SocialApiStatus.TokenRejected:
                    return LocalizedStrings.Get("SocialToastFailedSignedOut");
                case SocialApiStatus.Failed:
                    return LocalizedStrings.Get("SocialChatErrorRefused");
                default:
                    return LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(status) ?? "SocialActionErrorGeneric");
            }
        }

        public static async Task<bool> DeleteAsync(UIElement owner, string key, SocialChatMessage message)
        {
            if (message == null) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Get("SocialChatDeleteConfirmTitle"),
                                                                 LocalizedStrings.Get(SocialChatKeys.IsRoom(key)
                                                                                      ? "SocialChatDeleteRoomMessageConfirmBody"
                                                                                      : "SocialChatDeleteConfirmBody"),
                                                                 LocalizedStrings.Get("SocialChatDeleteConfirmButton"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            if (!confirmed) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.DeleteChatMessageAsync(token, message.Id, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok || string.Equals(result.ErrorCode, "NO_SUCH_MESSAGE", StringComparison.Ordinal))
            {
                ApplyDeleted(key, message.Id);
                AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Get("SocialChatDeletedAnnouncement"), "SocialChatDeleted");
                return true;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat);
                return false;
            }

            await ShowFailureAsync(owner, "SocialChatDeleteFailedTitle", result.Status);
            return false;
        }

        public static async Task<SocialChatRoom> CreateRoomAsync(UIElement owner, string name, string description, IReadOnlyList<SocialUser> invitees)
        {
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return null;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var created = await SocialApiService.CreateChatRoomAsync(token, name, description, CancellationToken.None);
            if (created.Status != SocialApiStatus.Ok)
            {
                if (created.Status == SocialApiStatus.PermissionDenied)
                {
                    await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat);
                }
                else
                {
                    await ShowFailureAsync(owner, "SocialChatRoomCreateFailedTitle", created.Status);
                }
                return null;
            }

            var room = created.Value;
            Remember(room);
            Raise(SocialChatChange.ForRoom(room.Id, room));

            var failed = new List<string>();
            foreach (var user in invitees ?? new SocialUser[0])
            {
                var invited = await SocialApiService.InviteToChatRoomAsync(token, room.Id, user.Id, CancellationToken.None);
                if (invited.Status != SocialApiStatus.Ok) failed.Add(user.Handle);
            }

            if (failed.Count > 0)
            {
                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Get("SocialChatInviteFailedTitle"),
                                                     LocalizedStrings.Format("SocialChatInviteSomeFailedFormat", string.Join(", ", failed)),
                                                     LocalizedStrings.Get("DialogOk"));
            }

            AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Format("SocialChatRoomCreatedFormat", room.DisplayName), "SocialChatRoom");
            return room;
        }

        public static async Task<SocialChatRoom> UpdateRoomAsync(UIElement owner, SocialChatRoom room, string name, string description)
        {
            if (room == null || !await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return null;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.UpdateChatRoomAsync(token, room.Id, name, description, CancellationToken.None);
            if (result.Status != SocialApiStatus.Ok)
            {
                await ShowFailureAsync(owner, "SocialChatRoomUpdateFailedTitle", result.Status);
                return null;
            }

            var updated = result.Value;
            Remember(updated);
            Raise(SocialChatChange.ForRoom(updated.Id, updated));
            AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Get("SocialChatRoomSavedAnnouncement"), "SocialChatRoom");
            return updated;
        }

        public static async Task<bool> InviteAsync(UIElement owner, SocialChatRoom room, SocialUser user)
        {
            if (room == null || user == null || !await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.InviteToChatRoomAsync(token, room.Id, user.Id, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok)
            {
                AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Format("SocialChatInvitedFormat", user.Handle), "SocialChatRoom");
                return true;
            }

            await DialogService.ShowMessageAsync(owner,
                                                 LocalizedStrings.Get("SocialChatInviteFailedTitle"),
                                                 result.Status == SocialApiStatus.Failed
                                                 ? LocalizedStrings.Format("SocialChatInviteRefusedFormat", user.Handle, AppConstants.SocialChatRoomMemberLimit)
                                                 : LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric"),
                                                 LocalizedStrings.Get("DialogOk"));
            return false;
        }

        public static async Task<bool> SetRoomMutedAsync(UIElement owner, SocialChatRoom room, bool mute)
        {
            if (room == null || !await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.MuteChatRoomAsync(token, room.Id, mute, CancellationToken.None);
            if (result.Status != SocialApiStatus.Ok)
            {
                await ShowFailureAsync(owner, "SocialChatRoomMuteFailedTitle", result.Status);
                return false;
            }

            var updated = room.WithMuted(mute);
            Remember(updated);
            if (mute) MarkRead(SocialChatKeys.ForRoom(room.Id));
            Raise(SocialChatChange.ForRoom(room.Id, updated));
            AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Get(mute ? "SocialChatRoomMutedAnnouncement" : "SocialChatRoomUnmutedAnnouncement"),
                                            "SocialChatRoom");
            return true;
        }

        public static async Task<bool> LeaveRoomAsync(UIElement owner, SocialChatRoom room)
        {
            if (room == null) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Format("SocialChatRoomLeaveConfirmTitleFormat", room.DisplayName),
                                                                 LocalizedStrings.Get("SocialChatRoomLeaveConfirmBody"),
                                                                 LocalizedStrings.Get("SocialChatRoomLeaveConfirmButton"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            return confirmed && await RoomActionAsync(owner, room, "chat/rooms/leave", "SocialChatRoomLeaveFailedTitle", "SocialChatRoomLeftAnnouncement");
        }

        public static async Task<bool> DeleteRoomAsync(UIElement owner, SocialChatRoom room)
        {
            if (room == null) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Format("SocialChatRoomDeleteConfirmTitleFormat", room.DisplayName),
                                                                 LocalizedStrings.Get("SocialChatRoomDeleteConfirmBody"),
                                                                 LocalizedStrings.Get("SocialChatRoomDeleteConfirmButton"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            return confirmed && await RoomActionAsync(owner, room, "chat/rooms/delete", "SocialChatRoomDeleteFailedTitle", "SocialChatRoomDeletedAnnouncement");
        }

        private static async Task<bool> RoomActionAsync(UIElement owner, SocialChatRoom room, string endpoint, string failedTitleKey, string doneKey)
        {
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.ChatRoomActionAsync(endpoint, token, room.Id, CancellationToken.None);
            if (result.Status != SocialApiStatus.Ok && !string.Equals(result.ErrorCode, "NO_SUCH_ROOM", StringComparison.Ordinal))
            {
                await ShowFailureAsync(owner, failedTitleKey, result.Status);
                return false;
            }

            ForgetRoom(room.Id);
            AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Get(doneKey), "SocialChatRoom");
            return true;
        }

        private static void ForgetRoom(string roomId)
        {
            var key = SocialChatKeys.ForRoom(roomId);
            bool unreadChanged;
            lock (s_lock)
            {
                s_rooms.Remove(roomId);
                s_drafts.Remove(key);
                unreadChanged = s_unread.Remove(key);
            }

            SocialChatToasts.Remove(key);
            Raise(SocialChatChange.ForRoom(roomId, null));
            if (unreadChanged) RaiseUnreadChanged();
        }

        public static async Task<bool> JoinAsync(UIElement owner, SocialChatInvitation invitation)
        {
            if (invitation == null || !await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.ChatRoomActionAsync("chat/rooms/join", token, invitation.RoomId, CancellationToken.None);
            if (result.Status != SocialApiStatus.Ok)
            {
                await ShowFailureAsync(owner, "SocialChatJoinFailedTitle", result.Status);
                await FetchInvitationsAsync(CancellationToken.None);
                return false;
            }

            DropInvitation(invitation);
            if (invitation.Room != null) Remember(invitation.Room);
            Raise(SocialChatChange.ForRoom(invitation.RoomId, invitation.Room));
            AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Format("SocialChatJoinedFormat",
                                                                           invitation.Room == null ? "" : invitation.Room.DisplayName),
                                            "SocialChatRoom");
            return true;
        }

        public static async Task<bool> IgnoreAsync(UIElement owner, SocialChatInvitation invitation)
        {
            if (invitation == null || !await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.ChatRoomActionAsync("chat/rooms/invitations/ignore", token, invitation.RoomId, CancellationToken.None);
            if (result.Status != SocialApiStatus.Ok && !string.Equals(result.ErrorCode, "NO_SUCH_ROOM", StringComparison.Ordinal))
            {
                await ShowFailureAsync(owner, "SocialChatIgnoreFailedTitle", result.Status);
                return false;
            }

            DropInvitation(invitation);
            AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Get("SocialChatIgnoredAnnouncement"), "SocialChatRoom");
            return true;
        }

        private static void DropInvitation(SocialChatInvitation invitation)
        {
            lock (s_lock)
            {
                s_invitations = s_invitations.Where(known => !string.Equals(known.Id, invitation.Id, StringComparison.Ordinal)).ToList();
            }

            Raise(SocialChatChange.ForInvitations());
            RaiseUnreadChanged();
        }

        private static Task<bool> ShowFailureAsync(UIElement owner, string titleKey, SocialApiStatus status)
        {
            return DialogService.ShowMessageAsync(owner,
                                                  LocalizedStrings.Get(titleKey),
                                                  LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(status) ?? "SocialActionErrorGeneric"),
                                                  LocalizedStrings.Get("DialogOk"));
        }

        private static void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                lock (s_lock)
                {
                    if (string.Equals(account, s_accountId, StringComparison.Ordinal)) return;
                    s_accountId = account;
                }

                Reset();
                if (account != null) RefreshUnreadInBackground();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: account change handling failed", ex);
            }
        }

        private static void OnLabChanged(object sender, EventArgs e)
        {
            try
            {
                var on = LabsService.ChatEnabled;
                lock (s_lock)
                {
                    if (on == s_labWasOn) return;
                    s_labWasOn = on;
                }

                if (on)
                {
                    RefreshUnreadInBackground();
                    return;
                }

                Reset();
                SocialChatToasts.ClearState();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: lab change handling failed", ex);
            }
        }

        private static void Reset()
        {
            lock (s_lock)
            {
                s_unread.Clear();
                s_seen.Clear();
                s_seenOrder.Clear();
                s_drafts.Clear();
                s_rooms.Clear();
                s_invitations = new SocialChatInvitation[0];
            }

            SocialChatToasts.RemoveAll();
            Raise(SocialChatChange.ForReset());
            RaiseUnreadChanged();
        }

        private static void Raise(SocialChatChange change)
        {
            try
            {
                Changed?.Invoke(null, change);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: a chat change handler failed", ex);
            }
        }

        private static void RaiseUnreadChanged()
        {
            try
            {
                UnreadChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: an unread handler failed", ex);
            }
        }
    }
}
