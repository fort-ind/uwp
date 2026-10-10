using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public enum SocialFollowState
    {
        None,
        Follow,
        FollowBack,
        RequestToFollow,
        Following,
        Requested
    }

    public enum SocialFollowOutcome
    {
        Changed,
        Cancelled,
        Failed,
        NeedsSignIn
    }

    public sealed class SocialFollowResult
    {
        public SocialFollowResult(SocialFollowOutcome outcome, SocialFollowState action, SocialUserDetail updated,
                                  SocialApiStatus status, string errorCode)
        {
            Outcome = outcome;
            Action = action;
            Updated = updated;
            Status = status;
            ErrorCode = errorCode;
        }

        public SocialFollowOutcome Outcome { get; private set; }

        public SocialFollowState Action { get; private set; }

        public SocialUserDetail Updated { get; private set; }

        public SocialApiStatus Status { get; private set; }

        public string ErrorCode { get; private set; }
    }

    public sealed class SocialFollowChangedEventArgs : EventArgs
    {
        public SocialFollowChangedEventArgs(SocialUserDetail user)
        {
            User = user;
        }

        public SocialUserDetail User { get; private set; }
    }

    public static class SocialFollowService
    {
        public const string Permission = SocialPermissions.WriteFollowing;

        private const string BlockedCode = "BLOCKED";

        private const string BlockingCode = "BLOCKING";

        private static readonly object s_lock = new object();

        private static bool s_initialized;

        public static event EventHandler<SocialFollowChangedEventArgs> Changed;

        public static void Initialize()
        {
            lock (s_lock)
            {
                if (s_initialized) return;
                s_initialized = true;
            }

            ProfileService.AuthStateChanged += OnAuthStateChanged;
        }

        private static void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            Raise(null);
        }

        private static void Raise(SocialUserDetail user)
        {
            try
            {
                var handler = Changed;
                if (handler != null) handler(null, new SocialFollowChangedEventArgs(user));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFollowService: a follow change handler failed", ex);
            }
        }

        public static bool CanFollow
        {
            get { return MisskeyAuthService.HasGrantedPermission(Permission); }
        }

        public static bool ShowRemoteListNotice
        {
            get
            {
                try
                {
                    var stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialRemoteListNotice];
                    return stored == null || Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialFollowService: could not read the remote list notice setting", ex);
                    return true;
                }
            }
            set
            {
                try
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialRemoteListNotice] = value;
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialFollowService: could not save the remote list notice setting", ex);
                }
            }
        }

        public static SocialFollowState StateOf(SocialUserDetail detail)
        {
            if (detail == null || detail.User == null) return SocialFollowState.None;
            if (detail.IsSuspended || detail.HasMoved) return SocialFollowState.None;

            var account = SocialContentService.CurrentAccountId();
            if (account == null || string.Equals(account, detail.User.Id, StringComparison.Ordinal)) return SocialFollowState.None;

            if (detail.IsFollowing) return SocialFollowState.Following;
            if (detail.HasPendingFollowRequest) return SocialFollowState.Requested;
            if (detail.IsLocked) return SocialFollowState.RequestToFollow;

            return detail.IsFollowed ? SocialFollowState.FollowBack : SocialFollowState.Follow;
        }

        public static bool IsEmphasised(SocialFollowState state)
        {
            return state == SocialFollowState.Follow
                   || state == SocialFollowState.FollowBack
                   || state == SocialFollowState.RequestToFollow;
        }

        public static string LabelFor(SocialFollowState state)
        {
            if (!CanFollow && IsEmphasised(state)) return LocalizedStrings.Get("SocialSignInToFollowButton");

            switch (state)
            {
                case SocialFollowState.Follow:
                    return LocalizedStrings.Get("SocialFollowButton");
                case SocialFollowState.FollowBack:
                    return LocalizedStrings.Get("SocialFollowBackButton");
                case SocialFollowState.RequestToFollow:
                    return LocalizedStrings.Get("SocialRequestToFollowButton");
                case SocialFollowState.Following:
                    return LocalizedStrings.Get("SocialFollowingButton");
                case SocialFollowState.Requested:
                    return LocalizedStrings.Get("SocialRequestedButton");
                default:
                    return "";
            }
        }

        public static async Task<SocialFollowResult> ToggleAsync(UIElement owner, SocialUserDetail detail)
        {
            var state = StateOf(detail);
            if (state == SocialFollowState.None)
            {
                return new SocialFollowResult(SocialFollowOutcome.Cancelled, state, null, SocialApiStatus.Ok, null);
            }

            if (!CanFollow)
            {
                await SocialPermissions.OfferSignInAsync(owner, SocialSignInPrompt.Follow);
                return new SocialFollowResult(SocialFollowOutcome.Cancelled, state, null, SocialApiStatus.Ok, null);
            }

            var name = SocialNoteItem.DisplayNameOf(detail.User);
            if (state == SocialFollowState.Following)
            {
                var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                     LocalizedStrings.Format("SocialUnfollowDialogTitleFormat", name),
                                                                     LocalizedStrings.Get("SocialUnfollowDialogBody"),
                                                                     LocalizedStrings.Get("SocialUnfollowDialogConfirm"),
                                                                     LocalizedStrings.Get("DialogCancel"),
                                                                     ContentDialogButton.Close);
                if (!confirmed) return new SocialFollowResult(SocialFollowOutcome.Cancelled, state, null, SocialApiStatus.Ok, null);
            }
            else if (state == SocialFollowState.Requested)
            {
                var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                     LocalizedStrings.Get("SocialWithdrawRequestDialogTitle"),
                                                                     LocalizedStrings.Format("SocialWithdrawRequestDialogBodyFormat", name),
                                                                     LocalizedStrings.Get("SocialWithdrawRequestDialogConfirm"),
                                                                     LocalizedStrings.Get("DialogCancel"),
                                                                     ContentDialogButton.Close);
                if (!confirmed) return new SocialFollowResult(SocialFollowOutcome.Cancelled, state, null, SocialApiStatus.Ok, null);
            }

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var userId = detail.User.Id;

            SocialApiResult<bool> result;
            switch (state)
            {
                case SocialFollowState.Following:
                    result = await SocialApiService.UnfollowAsync(token, userId, CancellationToken.None);
                    break;
                case SocialFollowState.Requested:
                    result = await SocialApiService.CancelFollowRequestAsync(token, userId, CancellationToken.None);
                    break;
                default:
                    result = await SocialApiService.FollowAsync(token, userId, CancellationToken.None);
                    break;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                SocialPermissions.Forget(Permission);
                Raise(null);
                await SocialPermissions.OfferSignInAsync(owner, SocialSignInPrompt.Follow);
                return new SocialFollowResult(SocialFollowOutcome.NeedsSignIn, state, null, result.Status, result.ErrorCode);
            }

            var fresh = await SocialApiService.GetUserAsync(token, userId, CancellationToken.None);
            var updated = fresh.Status == SocialApiStatus.Ok ? fresh.Value : null;
            if (updated != null) Raise(updated);

            if (result.Status == SocialApiStatus.Ok)
            {
                ProfileService.RefreshNow();
            }

            var changed = result.Status == SocialApiStatus.Ok
                          || (result.Status == SocialApiStatus.Refused && updated != null && StateOf(updated) != state);

            return new SocialFollowResult(changed ? SocialFollowOutcome.Changed : SocialFollowOutcome.Failed,
                                          state, updated, result.Status, result.ErrorCode);
        }

        public static async Task RereadAsync(string userId)
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var fresh = await SocialApiService.GetUserAsync(token, userId, CancellationToken.None);
                if (fresh.Status == SocialApiStatus.Ok) Raise(fresh.Value);

                ProfileService.RefreshNow();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFollowService: could not re-read the person", ex);
            }
        }

        public static async Task<SocialFollowResult> ToggleWithFeedbackAsync(UIElement owner, SocialUserDetail detail)
        {
            var result = await ToggleAsync(owner, detail);
            var name = SocialNoteItem.DisplayNameOf(detail == null ? null : detail.User);

            if (result.Outcome == SocialFollowOutcome.Failed)
            {
                await DialogService.ShowMessageAsync(owner,
                                                     FailureTitle(result, name),
                                                     FailureMessage(result, name),
                                                     LocalizedStrings.Get("DialogOk"));
            }
            else if (result.Outcome == SocialFollowOutcome.Changed)
            {
                var announcement = AnnouncementFor(result);
                if (announcement != null) AutomationHelper.AnnounceStatus(owner, announcement, "SocialFollowChanged");
            }

            return result;
        }

        public static string FailureMessage(SocialFollowResult result, string name)
        {
            if (result != null && result.Action != SocialFollowState.Following && result.Action != SocialFollowState.Requested)
            {
                if (string.Equals(result.ErrorCode, BlockedCode, StringComparison.Ordinal))
                {
                    return LocalizedStrings.Format("SocialFollowBlockedFormat", name);
                }

                if (string.Equals(result.ErrorCode, BlockingCode, StringComparison.Ordinal))
                {
                    return LocalizedStrings.Format("SocialFollowBlockingFormat", name);
                }
            }

            var connectionKey = result == null ? null : SocialApiService.ConnectionMessageKey(result.Status);
            return LocalizedStrings.Get(connectionKey ?? "SocialFollowFailedBody");
        }

        public static string FailureTitle(SocialFollowResult result, string name)
        {
            var action = result == null ? SocialFollowState.Follow : result.Action;
            switch (action)
            {
                case SocialFollowState.Following:
                    return LocalizedStrings.Format("SocialUnfollowFailedTitleFormat", name);
                case SocialFollowState.Requested:
                    return LocalizedStrings.Get("SocialWithdrawFailedTitle");
                default:
                    return LocalizedStrings.Format("SocialFollowFailedTitleFormat", name);
            }
        }

        public static string AnnouncementFor(SocialFollowResult result)
        {
            if (result == null || result.Updated == null) return null;

            var name = SocialNoteItem.DisplayNameOf(result.Updated.User);
            switch (StateOf(result.Updated))
            {
                case SocialFollowState.Following:
                    return LocalizedStrings.Format("SocialNowFollowingAnnouncementFormat", name);
                case SocialFollowState.Requested:
                    return LocalizedStrings.Format("SocialRequestSentAnnouncementFormat", name);
                case SocialFollowState.None:
                    return null;
                default:
                    return result.Action == SocialFollowState.Requested
                           ? LocalizedStrings.Format("SocialRequestWithdrawnAnnouncementFormat", name)
                           : LocalizedStrings.Format("SocialNoLongerFollowingAnnouncementFormat", name);
            }
        }
    }
}
