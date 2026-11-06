using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialComposer
    {
        private bool SchedulingOffered
        {
            get { return _context.Mode == SocialComposerMode.Window && _context.Editing == null; }
        }

        private bool LocalOnlyBlocksScheduling
        {
            get { return _localOnly && _visibility != SocialPostService.DirectVisibility; }
        }

        private bool CanSchedule()
        {
            return SchedulingOffered && !LocalOnlyBlocksScheduling && CanPost();
        }

        private void UpdateScheduleAvailability()
        {
            ScheduleItem.IsEnabled = CanSchedule();
            ScheduleMenuButton.IsEnabled = !_posting;
            ToolTipService.SetToolTip(ScheduleItem,
                                      LocalOnlyBlocksScheduling ? LocalizedStrings.Get("SocialComposeScheduleLocalOnlyTip") : null);
        }

        private async void ScheduleItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ScheduleAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: scheduling failed", ex);
                SetPosting(false);
            }
        }

        public async Task<bool> ScheduleAsync()
        {
            if (!CanSchedule()) return false;

            Suggestions.Hide();
            SetPosting(true);
            try
            {
                if (_visibility == SocialPostService.DirectVisibility)
                {
                    _mentionDebounce.Cancel();
                    foreach (var user in await SocialPostService.ResolveMentionsAsync(Editor.Text, RecipientBox.Recipients))
                    {
                        RecipientBox.Add(user);
                    }
                }

                if (!await ConfirmDescriptionsAsync()) return false;

                var when = await PickScheduleTimeAsync();
                if (!when.HasValue) return false;

                var result = await SocialScheduleService.ScheduleAsync(this, GetDraft(), when.Value);
                if (!result.Succeeded) return false;

                await SocialScheduleService.RemoveReplacedAsync(this, _context.ReplacesScheduledId);

                Scheduled?.Invoke(this, when.Value);
                return true;
            }
            finally
            {
                SetPosting(false);
            }
        }

        private async Task<DateTimeOffset?> PickScheduleTimeAsync()
        {
            var now = DateTimeOffset.Now;
            var suggested = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
            var earliest = new DateTimeOffset(suggested.Date);

            var datePicker = new DatePicker
            {
                Header = LocalizedStrings.Get("SocialScheduleDateHeader"),
                MinYear = earliest,
                MaxYear = earliest.AddYears(AppConstants.SocialScheduleMaxYearsAhead),
                Date = new DateTimeOffset(suggested.Date)
            };

            var timePicker = new TimePicker
            {
                Header = LocalizedStrings.Get("SocialScheduleTimeHeader"),
                MinuteIncrement = AppConstants.SocialScheduleMinuteIncrement,
                Time = suggested.TimeOfDay
            };

            var pastNotice = new TextBlock
            {
                Text = LocalizedStrings.Get("SocialScheduleInPast"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            object captionStyle;
            if (Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out captionStyle))
            {
                pastNotice.Style = captionStyle as Style;
            }

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = LocalizedStrings.Format("SocialScheduleSendsAsFormat", LocalizedStrings.Get(NameKeyFor(_visibility))),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(datePicker);
            panel.Children.Add(timePicker);
            panel.Children.Add(pastNotice);

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.Get("SocialScheduleDialogTitle"),
                PrimaryButtonText = LocalizedStrings.Get("SocialScheduleDialogConfirm"),
                CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = panel
            };

            Func<DateTimeOffset> chosen = () => new DateTimeOffset(datePicker.Date.Date.Add(timePicker.Time));
            Action refresh = () =>
            {
                var valid = chosen() > DateTimeOffset.Now.AddMinutes(AppConstants.SocialScheduleMinimumLeadMinutes);
                dialog.IsPrimaryButtonEnabled = valid;
                pastNotice.Visibility = Shown(!valid);
            };

            datePicker.DateChanged += (s, args) => refresh();
            timePicker.TimeChanged += (s, args) => refresh();
            refresh();

            var answer = await DialogService.ShowAsync(this, dialog);
            return answer == ContentDialogResult.Primary ? chosen() : (DateTimeOffset?)null;
        }
    }
}
