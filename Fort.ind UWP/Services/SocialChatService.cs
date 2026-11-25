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
        Reset
    }

    public sealed class SocialChatChange
    {
        private SocialChatChange()
        {
        }

        public SocialChatChangeKind Kind { get; private set; }

        public string OtherUserId { get; private set; }

        public SocialChatMessage Message { get; private set; }

        public string MessageId { get; private set; }

        public string Reaction { get; private set; }

        public bool ReactionAdded { get; private set; }

        internal static SocialChatChange ForMessage(string otherUserId, SocialChatMessage message)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Message, OtherUserId = otherUserId, Message = message, MessageId = message.Id };
        }

        internal static SocialChatChange ForDeleted(string otherUserId, string messageId)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Deleted, OtherUserId = otherUserId, MessageId = messageId };
        }

        internal static SocialChatChange ForReaction(string otherUserId, string messageId, string reaction, bool added)
        {
            return new SocialChatChange
            {
                Kind = SocialChatChangeKind.Reacted,
                OtherUserId = otherUserId,
                MessageId = messageId,
                Reaction = reaction,
                ReactionAdded = added
            };
        }

        internal static SocialChatChange ForRead(string otherUserId)
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Read, OtherUserId = otherUserId };
        }

        internal static SocialChatChange ForReset()
        {
            return new SocialChatChange { Kind = SocialChatChangeKind.Reset };
        }
    }

    public static class SocialChatService
    {
        private const string ScopeNone = "none";

        private const string ScopeFollowers = "followers";

        private const string ScopeFollowing = "following";

        private const string ScopeMutual = "mutual";

        private static readonly object s_lock = new object();

        private static readonly HashSet<string> s_unread = new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> s_seen = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Queue<string> s_seenOrder = new Queue<string>();

        private static readonly Dictionary<int, string> s_active = new Dictionary<int, string>();

        private static readonly Dictionary<string, string> s_drafts = new Dictionary<string, string>(StringComparer.Ordinal);

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
                    return s_unread.Count;
                }
            }
        }

        public static bool IsUnread(string otherUserId)
        {
            if (string.IsNullOrEmpty(otherUserId)) return false;

            lock (s_lock)
            {
                return s_unread.Contains(otherUserId);
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
                await FetchHistoryAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatService: could not refresh the unread chats", ex);
            }
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialChatMessage>>> FetchHistoryAsync(CancellationToken cancellationToken)
        {
            var account = SocialContentService.CurrentAccountId();
            if (!LabsService.ChatEnabled || account == null || !SocialPermissions.Has(SocialPermissions.ReadChat))
            {
                return SocialApiResult<IReadOnlyList<SocialChatMessage>>.Failed(SocialApiStatus.PermissionDenied);
            }

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetChatHistoryAsync(token, AppConstants.SocialChatHistoryLimit, cancellationToken);
            if (!SocialContentService.IsCurrentAccount(account)) return SocialApiResult<IReadOnlyList<SocialChatMessage>>.Failed(SocialApiStatus.Failed);

            if (result.Status == SocialApiStatus.Ok)
            {
                ApplyHistory(account, result.Value);
            }
            else if (result.Status == SocialApiStatus.PermissionDenied)
            {
                SocialPermissions.Forget(SocialPermissions.ReadChat);
            }

            return result;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialChatMessage>>> FetchTimelineAsync(string otherUserId, string sinceId,
                                                                                                    string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetChatTimelineAsync(token, otherUserId, sinceId, untilId,
                                                                     AppConstants.SocialChatPageSize, cancellationToken);
            if (result.Status == SocialApiStatus.Ok)
            {
                MarkRead(otherUserId);
            }
            else if (result.Status == SocialApiStatus.PermissionDenied)
            {
                SocialPermissions.Forget(SocialPermissions.ReadChat);
            }

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

        private static void ApplyHistory(string account, IReadOnlyList<SocialChatMessage> history)
        {
            var unread = history.Where(message => !message.IsRoomMessage
                                                  && message.IsKnownUnread
                                                  && !message.IsFrom(account))
                                .Select(message => message.OtherUserId(account))
                                .Where(other => !string.IsNullOrEmpty(other) && !IsActive(other))
                                .ToList();

            bool changed;
            lock (s_lock)
            {
                changed = unread.Count != s_unread.Count || unread.Any(other => !s_unread.Contains(other));
                if (changed)
                {
                    s_unread.Clear();
                    s_unread.UnionWith(unread);
                }
            }

            if (changed) RaiseUnreadChanged();
        }

        public static void ApplyFromMainChannel(SocialChatMessage message)
        {
            if (message == null || message.IsRoomMessage || !LabsService.ChatEnabled) return;

            var account = SocialContentService.CurrentAccountId();
            if (account == null) return;

            ApplyIncoming(message);

            var other = message.OtherUserId(account);
            if (!message.IsFrom(account) && !IsActive(other)) SocialChatToasts.ToastFromStream(message, account);
        }

        public static void ApplyIncoming(SocialChatMessage message)
        {
            if (message == null || message.IsRoomMessage || !LabsService.ChatEnabled) return;

            var account = SocialContentService.CurrentAccountId();
            if (account == null) return;

            var other = message.OtherUserId(account);
            if (string.IsNullOrEmpty(other)) return;

            var incoming = !message.IsFrom(account);
            bool unreadChanged = false;
            lock (s_lock)
            {
                if (!Remember(message.Id)) return;

                if (incoming && !s_active.ContainsValue(other))
                {
                    unreadChanged = s_unread.Add(other);
                }
            }

            Raise(SocialChatChange.ForMessage(other, message));
            if (unreadChanged) RaiseUnreadChanged();
        }

        public static void ApplySent(string otherUserId, SocialChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(otherUserId)) return;

            lock (s_lock)
            {
                if (!Remember(message.Id)) return;
            }

            Raise(SocialChatChange.ForMessage(otherUserId, message));
        }

        public static void ApplyDeleted(string otherUserId, string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) return;

            Raise(SocialChatChange.ForDeleted(otherUserId, messageId));
        }

        public static void ApplyReaction(string otherUserId, string messageId, string reaction, bool added)
        {
            if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(reaction)) return;

            Raise(SocialChatChange.ForReaction(otherUserId, messageId, reaction, added));
        }

        public static void MarkRead(string otherUserId)
        {
            if (string.IsNullOrEmpty(otherUserId)) return;

            bool changed;
            lock (s_lock)
            {
                changed = s_unread.Remove(otherUserId);
            }

            SocialChatToasts.Remove(otherUserId);
            Raise(SocialChatChange.ForRead(otherUserId));
            if (changed) RaiseUnreadChanged();
        }

        public static void SetActive(int viewId, string otherUserId)
        {
            lock (s_lock)
            {
                if (string.IsNullOrEmpty(otherUserId))
                {
                    s_active.Remove(viewId);
                }
                else
                {
                    s_active[viewId] = otherUserId;
                }
            }
        }

        public static void ForgetView(int viewId)
        {
            SetActive(viewId, null);
        }

        public static bool IsActive(string otherUserId)
        {
            if (string.IsNullOrEmpty(otherUserId)) return false;

            lock (s_lock)
            {
                return s_active.ContainsValue(otherUserId);
            }
        }

        public static void SaveDraft(string otherUserId, string text)
        {
            if (string.IsNullOrEmpty(otherUserId)) return;

            lock (s_lock)
            {
                if (string.IsNullOrEmpty(text))
                {
                    s_drafts.Remove(otherUserId);
                }
                else
                {
                    s_drafts[otherUserId] = text;
                }
            }
        }

        public static string DraftFor(string otherUserId)
        {
            if (string.IsNullOrEmpty(otherUserId)) return null;

            lock (s_lock)
            {
                string text;
                return s_drafts.TryGetValue(otherUserId, out text) ? text : null;
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

        public static async Task<SocialApiResult<SocialChatMessage>> SendAsync(string otherUserId, string text, string fileId,
                                                                               CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.SendChatMessageAsync(token, otherUserId, text, fileId, cancellationToken);
            if (result.Status == SocialApiStatus.Ok)
            {
                ApplySent(otherUserId, result.Value);
            }
            else if (result.Status == SocialApiStatus.PermissionDenied)
            {
                SocialPermissions.Forget(SocialPermissions.WriteChat);
            }

            return result;
        }

        public static async Task<bool> ReactAsync(UIElement owner, string otherUserId, SocialChatMessage message, string key, bool add)
        {
            if (message == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message.Id)) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var reaction = SocialReactions.ToRequestKey(key);
            var present = message.HasReaction(reaction);
            if (present == add) return true;

            ApplyReaction(otherUserId, message.Id, reaction, add);

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

            ApplyReaction(otherUserId, message.Id, reaction, !add);

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat);
                return false;
            }

            await DialogService.ShowMessageAsync(owner,
                                                 LocalizedStrings.Get(add ? "SocialChatReactFailedTitle" : "SocialChatUnreactFailedTitle"),
                                                 LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric"),
                                                 LocalizedStrings.Get("DialogOk"));
            return false;
        }

        public static string SendFailureText(SocialApiStatus status, string errorCode)
        {
            switch (errorCode)
            {
                case "NO_SUCH_USER":
                    return LocalizedStrings.Get("SocialChatErrorNoSuchUser");
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

        public static async Task<bool> DeleteAsync(UIElement owner, string otherUserId, SocialChatMessage message)
        {
            if (message == null) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Get("SocialChatDeleteConfirmTitle"),
                                                                 LocalizedStrings.Get("SocialChatDeleteConfirmBody"),
                                                                 LocalizedStrings.Get("SocialChatDeleteConfirmButton"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            if (!confirmed) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.DeleteChatMessageAsync(token, message.Id, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok || string.Equals(result.ErrorCode, "NO_SUCH_MESSAGE", StringComparison.Ordinal))
            {
                ApplyDeleted(otherUserId, message.Id);
                AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Get("SocialChatDeletedAnnouncement"), "SocialChatDeleted");
                return true;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteChat, SocialSignInPrompt.Chat);
                return false;
            }

            await DialogService.ShowMessageAsync(owner,
                                                 LocalizedStrings.Get("SocialChatDeleteFailedTitle"),
                                                 LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric"),
                                                 LocalizedStrings.Get("DialogOk"));
            return false;
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
