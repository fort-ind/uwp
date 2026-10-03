using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.ApplicationModel.Background;
using Windows.Storage;
using Windows.UI.Notifications;

namespace Fort.ind_UWP
{
    public static class SocialToastActions
    {
        private const string FailedAccountKey = "account";

        private const string FailedNoteKey = "note";

        private const string FailedTextKey = "text";

        private const int MaximumTagLength = 64;

        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(25);

        public static async Task EnsureRegisteredAsync()
        {
            try
            {
                if (!await SocialNotificationService.RequestBackgroundAccessAsync()) return;
                if (FindRegistration() != null) return;

                var builder = new BackgroundTaskBuilder { Name = AppConstants.SocialToastActionTaskName };
                builder.SetTrigger(new ToastNotificationActionTrigger());
                builder.Register();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: could not register the toast actions - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void Unregister()
        {
            try
            {
                foreach (var registration in BackgroundTaskRegistration.AllTasks.Values.ToList())
                {
                    if (string.Equals(registration.Name, AppConstants.SocialToastActionTaskName, StringComparison.Ordinal))
                    {
                        registration.Unregister(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: could not unregister the toast actions - {ex.Message}");
            }
        }

        private static IBackgroundTaskRegistration FindRegistration()
        {
            foreach (var registration in BackgroundTaskRegistration.AllTasks.Values)
            {
                if (string.Equals(registration.Name, AppConstants.SocialToastActionTaskName, StringComparison.Ordinal))
                {
                    return registration;
                }
            }

            return null;
        }

        public static ToastQuickReply QuickReplyFor(SocialNote note, string account)
        {
            try
            {
                if (note == null || string.IsNullOrEmpty(note.Id) || string.IsNullOrEmpty(account)) return null;
                if (FindRegistration() == null) return null;

                var canReply = SocialPermissions.Has(SocialPermissions.WriteNotes);
                var canLike = SocialPermissions.Has(SocialPermissions.WriteReactions) && string.IsNullOrEmpty(note.MyReaction);
                if (!canReply && !canLike) return null;

                var handle = note.User == null ? "" : note.User.Handle;
                return new ToastQuickReply
                {
                    NoteId = note.Id,
                    Account = account,
                    Placeholder = LocalizedStrings.Format("SocialComposeReplyPlaceholderFormat", handle),
                    ReplyText = canReply ? LocalizedStrings.Get("SocialToastReplyButton") : null,
                    LikeText = canLike ? LocalizedStrings.Get("SocialToastLikeButton") : null
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: no quick reply for this toast - {ex.Message}");
                return null;
            }
        }

        public static bool IsToastAction(IBackgroundTaskInstance instance)
        {
            return instance != null && instance.Task != null
                   && string.Equals(instance.Task.Name, AppConstants.SocialToastActionTaskName, StringComparison.Ordinal);
        }

        public static async Task RunAsync(IBackgroundTaskInstance instance)
        {
            var deferral = instance.GetDeferral();
            using (var cts = new CancellationTokenSource(Budget))
            {
                BackgroundTaskCanceledEventHandler onCanceled = (sender, reason) =>
                {
                    Debug.WriteLine($"SocialToastActions: the toast action was cancelled - {reason}");
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        Debug.WriteLine("SocialToastActions: the action had already finished when it was cancelled");
                    }
                };
                instance.Canceled += onCanceled;

                try
                {
                    var details = instance.TriggerDetails as ToastNotificationActionTriggerDetail;
                    if (details != null) await HandleAsync(details, cts.Token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialToastActions: the toast action failed - {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    instance.Canceled -= onCanceled;
                    deferral.Complete();
                }
            }
        }

        private static async Task HandleAsync(ToastNotificationActionTriggerDetail details, CancellationToken cancellationToken)
        {
            ToastArguments arguments;
            try
            {
                arguments = ToastArguments.Parse(details.Argument);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: could not read the toast arguments - {ex.Message}");
                return;
            }

            string action;
            string noteId;
            string account;
            if (!arguments.TryGetValue(AppConstants.ToastArgumentAction, out action)
                || !arguments.TryGetValue(AppConstants.ToastArgumentNote, out noteId)
                || !arguments.TryGetValue(AppConstants.ToastArgumentAccount, out account)
                || string.IsNullOrEmpty(noteId) || string.IsNullOrEmpty(account))
            {
                return;
            }

            if (string.Equals(action, AppConstants.ToastActionReply, StringComparison.Ordinal))
            {
                object typed = null;
                if (details.UserInput != null) details.UserInput.TryGetValue(AppConstants.ToastReplyInputId, out typed);
                await ReplyAsync(noteId, account, typed as string, cancellationToken);
            }
            else if (string.Equals(action, AppConstants.ToastActionLike, StringComparison.Ordinal))
            {
                await LikeAsync(noteId, account, cancellationToken);
            }
        }

        private sealed class ReplyAttempt
        {
            public ReplyAttempt(string text)
            {
                Text = text;
            }

            public string Text { get; set; }
        }

        private static async Task ReplyAsync(string noteId, string account, string typed, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(typed)) return;

            var attempt = new ReplyAttempt(typed.Trim());
            string failure;
            try
            {
                failure = await SendReplyAsync(noteId, account, attempt, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                failure = LocalizedStrings.Get("SocialToastFailedTimeout");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: the reply failed - {ex.GetType().Name}: {ex.Message}");
                failure = LocalizedStrings.Get("SocialPostErrorGeneric");
            }

            if (failure != null) ShowReplyFailed(noteId, account, attempt.Text, failure);
        }

        private static async Task<string> SendReplyAsync(string noteId, string account, ReplyAttempt attempt, CancellationToken cancellationToken)
        {
            if (!SocialNotificationService.IsActiveAccount(account)) return LocalizedStrings.Get("SocialToastFailedAccount");

            var token = await MisskeyAuthService.TryGetTokenAsync();
            if (string.IsNullOrEmpty(token)) return LocalizedStrings.Get("SocialToastFailedSignedOut");
            if (!SocialPermissions.Has(SocialPermissions.WriteNotes)) return LocalizedStrings.Get("SocialToastFailedPermission");

            var parent = await SocialApiService.GetNoteAsync(token, noteId, cancellationToken);
            if (parent.Status != SocialApiStatus.Ok) return ReplyFailureFor(parent.Status, parent.ErrorCode);
            if (!SocialNotificationService.IsActiveAccount(account)) return LocalizedStrings.Get("SocialToastFailedAccount");

            var defaults = SocialPostService.ReplyDefaultsFor(parent.Value, account);
            attempt.Text = SocialPostService.WithReplyPrefix(defaults.Prefix, attempt.Text);

            var body = SocialPostService.BuildReplyBody(parent.Value, defaults, attempt.Text);
            var result = await SocialApiService.CreateNoteAsync(token, body, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return ReplyFailureFor(result.Status, result.ErrorCode);

            SocialNoteService.Raise(SocialNoteChange.RepliedWith(noteId, result.Value));
            SocialNoteService.Raise(SocialNoteChange.Posted(result.Value));
            return null;
        }

        private static string ReplyFailureFor(SocialApiStatus status, string errorCode)
        {
            switch (status)
            {
                case SocialApiStatus.PermissionDenied:
                    SocialPermissions.Forget(SocialPermissions.WriteNotes);
                    return LocalizedStrings.Get("SocialToastFailedPermission");
                case SocialApiStatus.TokenRejected:
                    return LocalizedStrings.Get("SocialToastFailedSignedOut");
                default:
                    return SocialPostService.MessageFor(errorCode);
            }
        }

        private static async Task LikeAsync(string noteId, string account, CancellationToken cancellationToken)
        {
            string failure;
            try
            {
                failure = await SendLikeAsync(noteId, account, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                failure = LocalizedStrings.Get("SocialToastFailedTimeout");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: the like failed - {ex.GetType().Name}: {ex.Message}");
                failure = LocalizedStrings.Get("SocialActionErrorGeneric");
            }

            if (failure != null) ShowLikeFailed(noteId, account, failure);
        }

        private static async Task<string> SendLikeAsync(string noteId, string account, CancellationToken cancellationToken)
        {
            if (!SocialNotificationService.IsActiveAccount(account)) return LocalizedStrings.Get("SocialToastFailedAccount");

            var token = await MisskeyAuthService.TryGetTokenAsync();
            if (string.IsNullOrEmpty(token)) return LocalizedStrings.Get("SocialToastFailedSignedOut");
            if (!SocialPermissions.Has(SocialPermissions.WriteReactions)) return LocalizedStrings.Get("SocialToastFailedPermission");

            var note = await SocialApiService.GetNoteAsync(token, noteId, cancellationToken);
            if (note.Status != SocialApiStatus.Ok) return LikeFailureFor(note.Status, note.ErrorCode);
            if (!string.IsNullOrEmpty(note.Value.MyReaction)) return null;

            var result = await SocialApiService.ReactAsync(token, noteId, SocialReactions.ToRequestKey(SocialReactions.Like), cancellationToken);
            if (result.Status != SocialApiStatus.Ok && !string.Equals(result.ErrorCode, "ALREADY_REACTED", StringComparison.Ordinal))
            {
                return LikeFailureFor(result.Status, result.ErrorCode);
            }

            SocialNoteService.Raise(SocialNoteChange.Reacted(noteId, account, SocialReactions.Like));
            return null;
        }

        private static string LikeFailureFor(SocialApiStatus status, string errorCode)
        {
            switch (status)
            {
                case SocialApiStatus.PermissionDenied:
                    SocialPermissions.Forget(SocialPermissions.WriteReactions);
                    return LocalizedStrings.Get("SocialToastFailedPermission");
                case SocialApiStatus.TokenRejected:
                    return LocalizedStrings.Get("SocialToastFailedSignedOut");
                default:
                    return LocalizedStrings.Get(string.Equals(errorCode, "NO_SUCH_NOTE", StringComparison.Ordinal)
                                                ? "SocialActionErrorNoSuchNote"
                                                : "SocialActionErrorGeneric");
            }
        }

        private static void ShowReplyFailed(string noteId, string account, string text, string reason)
        {
            RememberFailedReply(noteId, account, text);

            var toast = new GroupedToast
            {
                Title = LocalizedStrings.Get("SocialToastReplyFailedTitle"),
                Body = LocalizedStrings.Format("SocialToastReplyFailedBodyFormat", reason),
                Attribution = LocalizedStrings.Get("SocialToastAttribution"),
                Group = AppConstants.SocialToastFailureGroup,
                Tag = TagFor("reply:", noteId)
            };
            toast.AddArgument(AppConstants.ToastArgumentOpen, AppConstants.ToastOpenNote);
            toast.AddArgument(AppConstants.ToastArgumentNote, noteId);
            toast.AddArgument(AppConstants.ToastArgumentAccount, account);
            toast.AddArgument(AppConstants.ToastArgumentRestore, "1");

            LiveTileService.ShowGroupedToast(toast);
        }

        private static void ShowLikeFailed(string noteId, string account, string reason)
        {
            var toast = new GroupedToast
            {
                Title = LocalizedStrings.Get("SocialToastLikeFailedTitle"),
                Body = reason,
                Attribution = LocalizedStrings.Get("SocialToastAttribution"),
                Group = AppConstants.SocialToastFailureGroup,
                Tag = TagFor("like:", noteId)
            };
            toast.AddArgument(AppConstants.ToastArgumentOpen, AppConstants.ToastOpenNote);
            toast.AddArgument(AppConstants.ToastArgumentNote, noteId);
            toast.AddArgument(AppConstants.ToastArgumentAccount, account);

            LiveTileService.ShowGroupedToast(toast);
        }

        private static string TagFor(string prefix, string noteId)
        {
            var tag = prefix + noteId;
            return tag.Length > MaximumTagLength ? tag.Substring(0, MaximumTagLength) : tag;
        }

        private static void RememberFailedReply(string noteId, string account, string text)
        {
            try
            {
                var kept = text ?? "";
                if (kept.Length > AppConstants.SocialToastFailedReplyLimit)
                {
                    kept = kept.Substring(0, AppConstants.SocialToastFailedReplyLimit);
                    if (kept.Length > 0 && char.IsHighSurrogate(kept[kept.Length - 1])) kept = kept.Substring(0, kept.Length - 1);
                }

                var stored = new ApplicationDataCompositeValue();
                stored[FailedAccountKey] = account;
                stored[FailedNoteKey] = noteId;
                stored[FailedTextKey] = kept;
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialToastFailedReply] = stored;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: could not keep the failed reply - {ex.Message}");
            }
        }

        public static string TakeFailedReply(string account, string noteId)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                var stored = values[AppConstants.SettingSocialToastFailedReply] as ApplicationDataCompositeValue;
                if (stored == null) return null;

                if (!string.Equals(stored[FailedAccountKey] as string, account, StringComparison.Ordinal)
                    || !string.Equals(stored[FailedNoteKey] as string, noteId, StringComparison.Ordinal))
                {
                    return null;
                }

                values.Remove(AppConstants.SettingSocialToastFailedReply);
                return stored[FailedTextKey] as string;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialToastActions: could not read the failed reply - {ex.Message}");
                return null;
            }
        }
    }
}
