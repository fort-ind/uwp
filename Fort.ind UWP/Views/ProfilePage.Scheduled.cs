using System;
using System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class ProfilePage
    {
        private bool _scheduledHandlerAttached;

        private int _scheduledCountVersion;

        private void AttachScheduled()
        {
            if (!_scheduledHandlerAttached)
            {
                SocialScheduleService.Changed += OnScheduledChanged;
                _scheduledHandlerAttached = true;
            }

            RefreshScheduledCount();
        }

        private void DetachScheduled()
        {
            if (!_scheduledHandlerAttached) return;

            SocialScheduleService.Changed -= OnScheduledChanged;
            _scheduledHandlerAttached = false;
        }

        private async void OnScheduledChanged(object sender, EventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!_released) RefreshScheduledCount();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("ProfilePage: could not refresh the scheduled count", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("ProfilePage: scheduled change handler failed", ex);
            }
        }

        private async void RefreshScheduledCount()
        {
            try
            {
                var version = ++_scheduledCountVersion;
                ShowScheduledCount(null);

                if (ProfileService.CurrentUser == null || !SocialPermissions.Has(SocialPermissions.ReadNotesSchedule)) return;

                var result = await SocialScheduleService.FetchAsync(CancellationToken.None);
                if (_released || version != _scheduledCountVersion) return;

                if (result.Status == SocialApiStatus.Ok) ShowScheduledCount(result.Value.Count);
            }
            catch (Exception ex)
            {
                AppLog.Error("ProfilePage: could not count the scheduled notes", ex);
            }
        }

        private void ShowScheduledCount(int? count)
        {
            var label = count.HasValue && count.Value > 0
                        ? LocalizedStrings.Format("SocialProfileScheduledCountFormat", count.Value)
                        : LocalizedStrings.Get("SocialProfileScheduledButtonContent");

            var limit = SocialPolicies.Current.ScheduledNoteMax;
            var detail = count.HasValue && count.Value > 0 && limit.HasValue
                         ? LocalizedStrings.Format("SocialProfileScheduledCountOfFormat", count.Value, limit.Value)
                         : null;

            ScheduledButton.Content = label;
            ToolTipService.SetToolTip(ScheduledButton, detail);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ScheduledButton, detail ?? label);
        }

        private void ScheduledButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                if (account == null || Frame == null) return;

                Frame.Navigate(typeof(SocialScheduledPage), new SocialScheduledArgs(account));
            }
            catch (Exception ex)
            {
                AppLog.Error("ProfilePage: could not open the scheduled notes", ex);
            }
        }
    }
}
