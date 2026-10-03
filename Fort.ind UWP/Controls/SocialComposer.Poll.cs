using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialComposer
    {
        private const int PollChoiceMinimum = 2;

        private const int PollChoiceMaximum = 10;

        private const int PollChoiceMaxLength = 150;

        private readonly List<TextBox> _choiceBoxes = new List<TextBox>();

        private SocialDraftPoll _initialPoll;

        private void BuildExpiryOptions()
        {
            foreach (SocialPollExpiry expiry in Enum.GetValues(typeof(SocialPollExpiry)))
            {
                ExpiryBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizedStrings.Get("SocialComposePollExpiry" + expiry),
                    Tag = expiry
                });
            }

            SelectExpiry(SocialPollExpiry.OneDay);
        }

        private void SelectExpiry(SocialPollExpiry expiry)
        {
            foreach (var option in ExpiryBox.Items)
            {
                var item = option as ComboBoxItem;
                if (item != null && item.Tag is SocialPollExpiry && (SocialPollExpiry)item.Tag == expiry)
                {
                    ExpiryBox.SelectedItem = item;
                    break;
                }
            }

            CustomExpiryPanel.Visibility = Shown(expiry == SocialPollExpiry.Custom);
        }

        private SocialPollExpiry SelectedExpiry
        {
            get
            {
                var item = ExpiryBox.SelectedItem as ComboBoxItem;
                return item != null && item.Tag is SocialPollExpiry ? (SocialPollExpiry)item.Tag : SocialPollExpiry.OneDay;
            }
        }

        private void LoadPoll(SocialDraftPoll poll, SocialDraftPoll initial)
        {
            _initialPoll = initial;
            PollChoices.Children.Clear();
            _choiceBoxes.Clear();

            if (poll == null)
            {
                PollToggle.IsChecked = false;
                PollPanel.Visibility = Visibility.Collapsed;
                MultipleBox.IsChecked = false;
                SelectExpiry(SocialPollExpiry.OneDay);
                SetCustomExpiry(DateTimeOffset.Now.AddDays(1));
                return;
            }

            PollToggle.IsChecked = true;
            PollPanel.Visibility = Visibility.Visible;
            foreach (var choice in poll.Choices)
            {
                AddChoice(choice);
            }
            while (_choiceBoxes.Count < PollChoiceMinimum) AddChoice("");

            MultipleBox.IsChecked = poll.Multiple;
            SelectExpiry(poll.Expiry);
            SetCustomExpiry(poll.ExpiresAt ?? DateTimeOffset.Now.AddDays(1));
        }

        private void SetCustomExpiry(DateTimeOffset when)
        {
            var local = when.ToLocalTime();
            ExpiryDate.Date = local.Date;
            ExpiryTime.Time = new TimeSpan(local.Hour, local.Minute, 0);
        }

        private DateTimeOffset CustomExpiry
        {
            get
            {
                var date = ExpiryDate.Date;
                var local = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Local) + ExpiryTime.Time;
                return new DateTimeOffset(local);
            }
        }

        private SocialDraftPoll GetPoll()
        {
            if (!PollPanel.IsShown()) return null;

            var choices = new List<string>();
            foreach (var box in _choiceBoxes)
            {
                choices.Add(box.Text ?? "");
            }

            var expiry = SelectedExpiry;
            DateTimeOffset? expiresAt = null;
            if (expiry == SocialPollExpiry.Custom)
            {
                var custom = CustomExpiry;
                var original = _initialPoll == null ? null : _initialPoll.ExpiresAt;
                expiresAt = original.HasValue && Math.Abs((original.Value - custom).TotalMinutes) < 1 ? original.Value : custom;
            }

            return new SocialDraftPoll(choices, MultipleBox.IsChecked.GetValueOrDefault(), expiry, expiresAt);
        }

        private bool IsPollValid()
        {
            var poll = GetPoll();
            if (poll == null) return true;
            if (poll.FilledChoices < PollChoiceMinimum) return false;

            return poll.Expiry != SocialPollExpiry.Custom || (poll.ExpiresAt.HasValue && poll.ExpiresAt.Value > DateTimeOffset.Now);
        }

        private void AddChoice(string text)
        {
            if (_choiceBoxes.Count >= PollChoiceMaximum) return;

            var box = new TextBox
            {
                Text = text ?? "",
                MaxLength = PollChoiceMaxLength,
                PlaceholderText = LocalizedStrings.Format("SocialComposePollChoiceFormat", _choiceBoxes.Count + 1)
            };
            AutomationProperties.SetName(box, box.PlaceholderText);
            box.TextChanged += PollChoice_TextChanged;

            var remove = new Button
            {
                Style = StyleOf("SocialActionButtonStyle"),
                Width = 40,
                Padding = new Thickness(0),
                Tag = box,
                Content = new FontIcon { Glyph = "", FontSize = 12, IsTextScaleFactorEnabled = false }
            };
            var label = LocalizedStrings.Format("SocialComposePollRemoveChoiceFormat", _choiceBoxes.Count + 1);
            AutomationProperties.SetName(remove, label);
            ToolTipService.SetToolTip(remove, label);
            remove.Click += RemoveChoice_Click;

            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(remove, 1);
            row.Children.Add(box);
            row.Children.Add(remove);

            _choiceBoxes.Add(box);
            PollChoices.Children.Add(row);
            RenumberChoices();
        }

        private void RenumberChoices()
        {
            for (var i = 0; i < _choiceBoxes.Count; i++)
            {
                var box = _choiceBoxes[i];
                box.PlaceholderText = LocalizedStrings.Format("SocialComposePollChoiceFormat", i + 1);
                AutomationProperties.SetName(box, box.PlaceholderText);

                var row = box.Parent as Grid;
                var remove = row == null || row.Children.Count < 2 ? null : row.Children[1] as Button;
                if (remove != null) remove.Visibility = Shown(_choiceBoxes.Count > PollChoiceMinimum);
            }

            AddChoiceButton.Visibility = Shown(_choiceBoxes.Count < PollChoiceMaximum);
        }

        private void RemoveChoice_Click(object sender, RoutedEventArgs e)
        {
            var box = (sender as FrameworkElement)?.Tag as TextBox;
            if (box == null || _choiceBoxes.Count <= PollChoiceMinimum) return;

            var index = _choiceBoxes.IndexOf(box);
            _choiceBoxes.Remove(box);
            PollChoices.Children.Remove(box.Parent as UIElement);
            RenumberChoices();
            OnEdited();

            if (_choiceBoxes.Count > 0) _choiceBoxes[Math.Min(index, _choiceBoxes.Count - 1)].Focus(FocusState.Programmatic);
        }

        private void AddChoiceButton_Click(object sender, RoutedEventArgs e)
        {
            AddChoice("");
            OnEdited();
            if (_choiceBoxes.Count > 0) _choiceBoxes[_choiceBoxes.Count - 1].Focus(FocusState.Programmatic);
        }

        private void PollToggle_Click(object sender, RoutedEventArgs e)
        {
            var on = PollToggle.IsChecked.GetValueOrDefault();
            PollPanel.Visibility = Shown(on);

            if (on)
            {
                while (_choiceBoxes.Count < PollChoiceMinimum) AddChoice("");
                _choiceBoxes[0].Focus(FocusState.Programmatic);
            }

            OnEdited();
        }

        private void PollChoice_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loading) OnEdited();
        }

        private void PollField_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading) OnEdited();
        }

        private void ExpiryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CustomExpiryPanel.Visibility = Shown(SelectedExpiry == SocialPollExpiry.Custom);
            if (!_loading) OnEdited();
        }

        private void ExpiryDate_DateChanged(object sender, DatePickerValueChangedEventArgs e)
        {
            if (!_loading) OnEdited();
        }

        private void ExpiryTime_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
        {
            if (!_loading) OnEdited();
        }

        private bool PollWillReset
        {
            get
            {
                if (_context.Editing == null || _context.Editing.Poll == null || _context.Editing.Poll.TotalVotes == 0) return false;

                var poll = GetPoll();
                return poll != null && (_initialPoll == null || !poll.SameAs(_initialPoll));
            }
        }

        private void UpdatePollWarning()
        {
            PollResetWarning.Visibility = Shown(PollWillReset);
        }

        private async Task<bool> ConfirmPollResetAsync()
        {
            if (!PollWillReset) return true;

            return await DialogService.ShowConfirmAsync(this,
                                                        LocalizedStrings.Get("SocialComposePollResetDialogTitle"),
                                                        LocalizedStrings.Get("SocialComposePollResetDialogBody"),
                                                        LocalizedStrings.Get("SocialComposePollResetDialogConfirm"),
                                                        LocalizedStrings.Get("DialogCancel"),
                                                        ContentDialogButton.Close);
        }
    }
}
