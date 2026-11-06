using System;
using Windows.Globalization.DateTimeFormatting;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage
    {
        private bool _scheduledHandlerAttached;

        private DispatcherTimer _scheduledInfoTimer;

        private void AttachScheduledHandler()
        {
            if (_scheduledHandlerAttached) return;

            SocialScheduleService.Scheduled += OnNoteScheduled;
            _scheduledHandlerAttached = true;
        }

        private void DetachScheduledHandler()
        {
            if (!_scheduledHandlerAttached) return;

            SocialScheduleService.Scheduled -= OnNoteScheduled;
            _scheduledHandlerAttached = false;
            StopScheduledInfoTimer();
        }

        private async void OnNoteScheduled(object sender, DateTimeOffset when)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        ShowScheduledInfoBar(when);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("MainPage: could not show the scheduled note banner", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: scheduled note handler failed", ex);
            }
        }

        private void ShowScheduledInfoBar(DateTimeOffset when)
        {
            var formatter = new DateTimeFormatter("shortdate shorttime");
            SocialScheduledInfoBar.Message = LocalizedStrings.Format("SocialScheduledInfoBarMessageFormat", formatter.Format(when));
            SocialScheduledInfoBar.IsOpen = true;

            StopScheduledInfoTimer();
            _scheduledInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppConstants.SocialScheduledInfoBarSeconds) };
            _scheduledInfoTimer.Tick += ScheduledInfoTimer_Tick;
            _scheduledInfoTimer.Start();
        }

        private void ScheduledInfoTimer_Tick(object sender, object e)
        {
            StopScheduledInfoTimer();
            SocialScheduledInfoBar.IsOpen = false;
        }

        private void StopScheduledInfoTimer()
        {
            var timer = _scheduledInfoTimer;
            if (timer == null) return;

            timer.Stop();
            timer.Tick -= ScheduledInfoTimer_Tick;
            _scheduledInfoTimer = null;
        }

        private void SocialScheduledInfoBar_Closed(Microsoft.UI.Xaml.Controls.InfoBar sender,
                                                   Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs args)
        {
            StopScheduledInfoTimer();
        }

        private void SocialScheduledViewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SocialScheduledInfoBar.IsOpen = false;

                var account = SocialContentService.CurrentAccountId();
                if (account == null) return;

                NavigateToTag(AppConstants.NavigationProfile);

                if (ContentFrame.Content is ProfilePage)
                {
                    ContentFrame.Navigate(typeof(SocialScheduledPage), new SocialScheduledArgs(account));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not open the scheduled notes from the banner", ex);
            }
        }
    }
}
