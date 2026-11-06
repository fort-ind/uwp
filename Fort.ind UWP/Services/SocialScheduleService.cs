using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class SocialScheduleResult
    {
        public SocialScheduleResult(bool succeeded, SocialApiStatus status, string errorCode)
        {
            Succeeded = succeeded;
            Status = status;
            ErrorCode = errorCode;
        }

        public bool Succeeded { get; private set; }

        public SocialApiStatus Status { get; private set; }

        public string ErrorCode { get; private set; }
    }

    public static class SocialScheduleService
    {
        public static event EventHandler Changed;

        public static event EventHandler<DateTimeOffset> Scheduled;

        public static async Task<SocialApiResult<IReadOnlyList<SocialScheduledNote>>> FetchAsync(CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetScheduledNotesAsync(token, cancellationToken);
        }

        public static async Task<SocialScheduleResult> ScheduleAsync(UIElement owner, SocialComposeDraft draft, DateTimeOffset when)
        {
            if (draft == null) return new SocialScheduleResult(false, SocialApiStatus.Failed, null);
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteNotesSchedule, SocialSignInPrompt.Schedule))
            {
                return new SocialScheduleResult(false, SocialApiStatus.PermissionDenied, null);
            }

            SocialApiResult<bool> result;
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                result = await SocialApiService.CreateScheduledNoteAsync(token, SocialPostService.BuildScheduleBody(draft, when), CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduleService: scheduling failed", ex);
                result = SocialApiResult<bool>.Failed(SocialApiStatus.Failed);
            }

            if (result.Status == SocialApiStatus.Ok)
            {
                Raise(when);
                return new SocialScheduleResult(true, result.Status, null);
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteNotesSchedule, SocialSignInPrompt.Schedule);
            }
            else
            {
                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Get("SocialScheduleFailedTitle"),
                                                     MessageFor(result.Status, result.ErrorCode),
                                                     LocalizedStrings.Get("DialogOk"));
            }

            return new SocialScheduleResult(false, result.Status, result.ErrorCode);
        }

        public static async Task<bool> CancelAsync(UIElement owner, SocialScheduledNote scheduled)
        {
            if (scheduled == null) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Get("SocialScheduleCancelTitle"),
                                                                 LocalizedStrings.Get("SocialScheduleCancelBody"),
                                                                 LocalizedStrings.Get("SocialScheduleCancelConfirm"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            if (!confirmed) return false;

            var result = await DeleteAsync(scheduled.Id);
            if (result.Status == SocialApiStatus.Ok || IsGone(result))
            {
                Raise(null);
                return true;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteNotesSchedule, SocialSignInPrompt.Schedule);
            }
            else
            {
                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Get("SocialScheduleCancelFailedTitle"),
                                                     MessageFor(result.Status, result.ErrorCode),
                                                     LocalizedStrings.Get("DialogOk"));
            }

            return false;
        }

        public static async Task RemoveReplacedAsync(UIElement owner, string scheduleId)
        {
            if (string.IsNullOrEmpty(scheduleId)) return;

            var result = await DeleteAsync(scheduleId);
            if (result.Status == SocialApiStatus.Ok || IsGone(result))
            {
                Raise(null);
                return;
            }

            await DialogService.ShowMessageAsync(owner,
                                                 LocalizedStrings.Get("SocialScheduleReplaceFailedTitle"),
                                                 LocalizedStrings.Get("SocialScheduleReplaceFailedBody"),
                                                 LocalizedStrings.Get("DialogOk"));
        }

        public static string MessageFor(SocialApiStatus status, string errorCode)
        {
            switch (errorCode)
            {
                case "SCHEDULE_NOTE_MAX":
                    return LocalizedStrings.Get("SocialScheduleErrorMax");
                case "CANNOT_CREATE_ALREADY_EXPIRED_SCHEDULE":
                    return LocalizedStrings.Get("SocialScheduleErrorPast");
                default:
                    return SocialPostService.MessageFor(status, errorCode);
            }
        }

        private static async Task<SocialApiResult<bool>> DeleteAsync(string scheduleId)
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                return await SocialApiService.DeleteScheduledNoteAsync(token, scheduleId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduleService: could not delete a scheduled note", ex);
                return SocialApiResult<bool>.Failed(SocialApiStatus.Failed);
            }
        }

        private static bool IsGone(SocialApiResult<bool> result)
        {
            return string.Equals(result.ErrorCode, "NO_SUCH_NOTE", StringComparison.Ordinal);
        }

        private static void Raise(DateTimeOffset? scheduledAt)
        {
            try
            {
                Changed?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduleService: a change handler failed", ex);
            }

            if (!scheduledAt.HasValue) return;

            try
            {
                Scheduled?.Invoke(null, scheduledAt.Value);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduleService: a scheduled handler failed", ex);
            }
        }
    }
}
