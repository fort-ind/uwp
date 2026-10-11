using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public enum SocialFollowRequestState
    {
        None,
        Pending,
        Accepted,
        Declined,
        Gone
    }

    public sealed class SocialFollowRequestChangedEventArgs : EventArgs
    {
        public SocialFollowRequestChangedEventArgs(string userId, SocialFollowRequestState state)
        {
            UserId = userId;
            State = state;
        }

        public string UserId { get; private set; }

        public SocialFollowRequestState State { get; private set; }
    }

    public static class SocialFollowRequestService
    {
        private const int PendingCheckLimit = 100;

        private const string NoFollowRequestCode = "NO_FOLLOW_REQUEST";

        private static readonly object s_lock = new object();

        private static HashSet<string> s_pending;

        private static string s_pendingAccount;

        private static bool s_pendingMore;

        public static event EventHandler<SocialFollowRequestChangedEventArgs> Changed;

        public static int? PendingCount
        {
            get
            {
                lock (s_lock)
                {
                    return IsSnapshotCurrent() ? s_pending.Count : (int?)null;
                }
            }
        }

        public static bool PendingCountIsPartial
        {
            get
            {
                lock (s_lock)
                {
                    return IsSnapshotCurrent() && s_pendingMore;
                }
            }
        }

        public static SocialFollowRequestState StateOf(string userId)
        {
            lock (s_lock)
            {
                if (string.IsNullOrEmpty(userId) || !IsSnapshotCurrent()) return SocialFollowRequestState.Pending;
                if (s_pending.Contains(userId)) return SocialFollowRequestState.Pending;
                return s_pendingMore ? SocialFollowRequestState.Pending : SocialFollowRequestState.None;
            }
        }

        public static string OutcomeText(SocialFollowRequestState state)
        {
            switch (state)
            {
                case SocialFollowRequestState.Accepted: return LocalizedStrings.Get("SocialFollowRequestAccepted");
                case SocialFollowRequestState.Declined: return LocalizedStrings.Get("SocialFollowRequestDeclined");
                case SocialFollowRequestState.Gone: return LocalizedStrings.Get("SocialFollowRequestGone");
                default: return "";
            }
        }

        private static bool IsSnapshotCurrent()
        {
            return s_pending != null
                   && string.Equals(s_pendingAccount, SocialContentService.CurrentAccountId(), StringComparison.Ordinal);
        }

        public static async Task<int?> RefreshPendingAsync(CancellationToken cancellationToken)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                if (account == null || !SocialPermissions.Has(SocialPermissions.ReadFollowing)) return null;

                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return null;

                var result = await SocialApiService.GetFollowRequestsAsync(token, null, PendingCheckLimit, cancellationToken);
                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SocialPermissions.Forget(SocialPermissions.ReadFollowing);
                    return null;
                }

                if (result.Status != SocialApiStatus.Ok) return PendingCount;
                if (!string.Equals(account, SocialContentService.CurrentAccountId(), StringComparison.Ordinal)) return null;

                lock (s_lock)
                {
                    s_pending = new HashSet<string>(result.Value.Select(entry => entry.User.User.Id), StringComparer.Ordinal);
                    s_pendingAccount = account;
                    s_pendingMore = result.Value.Count >= PendingCheckLimit;
                }

                Raise(null, SocialFollowRequestState.None);
                return result.Value.Count;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFollowRequestService: could not read the pending requests", ex);
                return null;
            }
        }

        public static async Task<SocialFollowRequestState?> RespondAsync(UIElement owner, SocialUser user, bool accept)
        {
            if (user == null || string.IsNullOrEmpty(user.Id)) return null;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteFollowing, SocialSignInPrompt.Follow)) return null;

            SocialApiResult<bool> result;
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                result = await SendAsync(token, user.Id, accept, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFollowRequestService: answering a follow request failed", ex);
                result = SocialApiResult<bool>.Failed(SocialApiStatus.Failed);
            }

            var state = Record(user.Id, accept, result);
            if (state.HasValue) return state;

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteFollowing, SocialSignInPrompt.Follow);
                return null;
            }

            await DialogService.ShowMessageAsync(owner,
                                                 LocalizedStrings.Format(accept ? "SocialFollowRequestAcceptFailedFormat" : "SocialFollowRequestRejectFailedFormat",
                                                                         SocialNoteItem.DisplayNameOf(user)),
                                                 LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric"),
                                                 LocalizedStrings.Get("DialogOk"));
            return null;
        }

        public static async Task<string> RespondInBackgroundAsync(string userId, string account, bool accept, CancellationToken cancellationToken)
        {
            if (!SocialNotificationService.IsActiveAccount(account)) return LocalizedStrings.Get("SocialToastFailedAccount");

            var token = await MisskeyAuthService.TryGetTokenAsync();
            if (string.IsNullOrEmpty(token)) return LocalizedStrings.Get("SocialToastFailedSignedOut");
            if (!SocialPermissions.Has(SocialPermissions.WriteFollowing)) return LocalizedStrings.Get("SocialToastFailedPermission");

            var result = await SendAsync(token, userId, accept, cancellationToken);
            if (Record(userId, accept, result).HasValue) return null;

            switch (result.Status)
            {
                case SocialApiStatus.PermissionDenied:
                    SocialPermissions.Forget(SocialPermissions.WriteFollowing);
                    return LocalizedStrings.Get("SocialToastFailedPermission");
                case SocialApiStatus.TokenRejected:
                    return LocalizedStrings.Get("SocialToastFailedSignedOut");
                default:
                    return LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric");
            }
        }

        private static Task<SocialApiResult<bool>> SendAsync(string token, string userId, bool accept, CancellationToken cancellationToken)
        {
            return accept
                   ? SocialApiService.AcceptFollowRequestAsync(token, userId, cancellationToken)
                   : SocialApiService.RejectFollowRequestAsync(token, userId, cancellationToken);
        }

        private static SocialFollowRequestState? Record(string userId, bool accept, SocialApiResult<bool> result)
        {
            SocialFollowRequestState state;
            if (result.Status == SocialApiStatus.Ok)
            {
                state = accept ? SocialFollowRequestState.Accepted : SocialFollowRequestState.Declined;
            }
            else if (string.Equals(result.ErrorCode, NoFollowRequestCode, StringComparison.Ordinal))
            {
                state = SocialFollowRequestState.Gone;
            }
            else
            {
                return null;
            }

            lock (s_lock)
            {
                if (s_pending != null) s_pending.Remove(userId);
            }

            Raise(userId, state);
            if (state == SocialFollowRequestState.Accepted) RefreshProfile();
            return state;
        }

        private static void RefreshProfile()
        {
            try
            {
                if (ProfileService.CurrentUser != null) ProfileService.RefreshNow();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFollowRequestService: could not refresh the profile after accepting", ex);
            }
        }

        private static void Raise(string userId, SocialFollowRequestState state)
        {
            try
            {
                Changed?.Invoke(null, new SocialFollowRequestChangedEventArgs(userId, state));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFollowRequestService: a change handler failed", ex);
            }
        }
    }
}
