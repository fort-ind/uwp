using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

namespace Fort.ind_UWP
{
    public static class SocialFeedTileService
    {
        private static readonly SemaphoreSlim s_refreshGate = new SemaphoreSlim(1, 1);

        private static readonly SemaphoreSlim s_reconcileGate = new SemaphoreSlim(1, 1);

        public static bool Enabled
        {
            get { return AppSettings.ReadBool(AppConstants.SettingSocialFeedTile, false); }
            set { AppSettings.Write(AppConstants.SettingSocialFeedTile, value); }
        }

        public static bool ShowAvatars
        {
            get { return AppSettings.ReadBool(AppConstants.SettingSocialFeedTileAvatars, true); }
            set { AppSettings.Write(AppConstants.SettingSocialFeedTileAvatars, value); }
        }

        public static int IntervalMinutes
        {
            get
            {
                var minutes = AppSettings.ReadInt(AppConstants.SettingSocialFeedTileMinutes, AppConstants.SocialFeedTileDefaultMinutes);
                return Math.Max(AppConstants.SocialCheckMinimumMinutes, minutes);
            }
            set { AppSettings.Write(AppConstants.SettingSocialFeedTileMinutes, value); }
        }

        public static bool WantsFeed
        {
            get { return SocialTileService.StartShowsLiveTiles && Enabled && !LiveTileService.TileCleared; }
        }

        private static bool WantsBackgroundCheck
        {
            get { return SocialTileService.StartShowsLiveTiles && Enabled; }
        }

        public static async void RefreshInBackground()
        {
            try
            {
                await RefreshAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFeedTileService: refresh failed", ex);
            }
        }

        public static async Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (!WantsFeed || SocialTileService.ShowsNotificationPreviews) return;
            if (!await s_refreshGate.WaitAsync(0)) return;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.GetTimelineAsync(token, SocialTimeline.Local, null,
                                                                     AppConstants.SocialFeedPageSize, cancellationToken);
                if (result.Status != SocialApiStatus.Ok) return;

                var posts = result.Value.Where(IsEligible)
                                  .Take(AppConstants.SocialTilePreviewLimit)
                                  .ToList();
                if (posts.Count == 0) return;

                await SocialTileService.ShowFeedAsync(posts, ShowAvatars, TimeSpan.FromMinutes(IntervalMinutes), cancellationToken);
            }
            finally
            {
                s_refreshGate.Release();
            }
        }

        public static void OnResuming()
        {
            RefreshInBackground();
        }

        private static bool IsEligible(SocialNote note)
        {
            if (note == null || note.IsHidden || note.IsDirect) return false;
            if (!string.Equals(note.Visibility, SocialNoteActionService.PublicVisibility, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(note.ReplyId)) return false;
            if (!string.IsNullOrWhiteSpace(note.ContentWarning)) return false;
            if (string.IsNullOrWhiteSpace(SocialFeedItem.PlainBody(note))) return false;
            if (HasSensitiveFile(note)) return false;

            var quoted = note.Renote;
            return quoted == null ||
                   (!quoted.IsHidden && !quoted.IsDirect && string.IsNullOrWhiteSpace(quoted.ContentWarning) &&
                    !HasSensitiveFile(quoted));
        }

        private static bool HasSensitiveFile(SocialNote note)
        {
            return note.Files != null && note.Files.Any(file => file.IsSensitive);
        }

        #region Background check

        public static bool IsBackgroundCheck(IBackgroundTaskInstance instance)
        {
            return instance != null && instance.Task != null &&
                   string.Equals(instance.Task.Name, AppConstants.SocialFeedTileTaskName, StringComparison.Ordinal);
        }

        public static async Task RunBackgroundCheckAsync(IBackgroundTaskInstance instance)
        {
            var deferral = instance.GetDeferral();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConstants.SocialFeedTileBackgroundSeconds)))
            {
                BackgroundTaskCanceledEventHandler onCanceled = (sender, reason) =>
                {
                    Debug.WriteLine($"SocialFeedTileService: background check cancelled - {reason}");
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        Debug.WriteLine("SocialFeedTileService: the check had already finished when it was cancelled");
                    }
                };
                instance.Canceled += onCanceled;

                try
                {
                    if (WantsBackgroundCheck)
                    {
                        await RefreshAsync(cts.Token);
                    }
                    else
                    {
                        UnregisterBackgroundCheck();
                    }
                }
                catch (OperationCanceledException)
                {
                    AppLog.Warning("SocialFeedTileService: background check ran out of time");
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialFeedTileService: background check failed", ex);
                }
                finally
                {
                    instance.Canceled -= onCanceled;
                    deferral.Complete();
                }
            }
        }

        public static async void ReconcileInBackground()
        {
            try
            {
                await ReconcileAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFeedTileService: reconcile failed", ex);
            }
        }

        public static async Task ReconcileAsync()
        {
            await s_reconcileGate.WaitAsync();
            try
            {
                if (WantsBackgroundCheck)
                {
                    await EnsureBackgroundCheckAsync();
                }
                else
                {
                    UnregisterBackgroundCheck();
                }
            }
            finally
            {
                s_reconcileGate.Release();
            }
        }

        private static async Task EnsureBackgroundCheckAsync()
        {
            try
            {
                if (!await SocialNotificationService.RequestBackgroundAccessAsync()) return;

                var minutes = (uint)IntervalMinutes;
                if (FindBackgroundCheck() != null)
                {
                    if (AppSettings.ReadInt(AppConstants.SettingSocialFeedTileRegisteredMinutes, 0) == (int)minutes) return;
                    UnregisterBackgroundCheck();
                }

                BackgroundTaskBuilder builder = new BackgroundTaskBuilder();
                builder.Name = AppConstants.SocialFeedTileTaskName;
                builder.IsNetworkRequested = true;
                builder.SetTrigger(new TimeTrigger(minutes, false));
                builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
                builder.Register();

                AppSettings.Write(AppConstants.SettingSocialFeedTileRegisteredMinutes, (int)minutes);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFeedTileService: could not register the background check", ex);
            }
        }

        private static IBackgroundTaskRegistration FindBackgroundCheck()
        {
            return BackgroundTaskRegistration.AllTasks.Values.FirstOrDefault(
                registration => string.Equals(registration.Name, AppConstants.SocialFeedTileTaskName, StringComparison.Ordinal));
        }

        private static void UnregisterBackgroundCheck()
        {
            try
            {
                var registrations = BackgroundTaskRegistration.AllTasks.Values
                    .Where(registration => string.Equals(registration.Name, AppConstants.SocialFeedTileTaskName, StringComparison.Ordinal))
                    .ToList();
                foreach (var registration in registrations)
                {
                    registration.Unregister(false);
                }

                AppSettings.Remove(AppConstants.SettingSocialFeedTileRegisteredMinutes);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialFeedTileService: could not unregister the background check", ex);
            }
        }

        #endregion
    }
}
