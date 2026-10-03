using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace Fort.ind_UWP
{
    public sealed partial class SocialNoteView
    {
        private readonly HashSet<int> _pollSelection = new HashSet<int>();

        private string _pollNoteId;

        private Button _pollVoteButton;

        private void RenderPoll(SocialNoteItem item)
        {
            var poll = item.IsHidden || item.IsDeleted ? null : item.Poll;
            if (poll == null || poll.Choices.Count == 0)
            {
                if (PollPanel != null)
                {
                    PollPanel.Children.Clear();
                    PollPanel.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (PollPanel == null) FindName("PollPanel");
            PollPanel.Children.Clear();
            _pollVoteButton = null;

            if (!string.Equals(_pollNoteId, item.Note.Id, StringComparison.Ordinal))
            {
                _pollNoteId = item.Note.Id;
                _pollSelection.Clear();
            }

            var now = DateTimeOffset.Now;
            var expired = poll.IsExpired(now);
            var showResults = poll.HasVoted || expired || item.IsMine || item.IsPollRevealed;
            var canVote = !expired && SocialNoteActionService.CanAct(item) && (poll.Multiple || !poll.HasVoted);
            var total = poll.TotalVotes;

            for (var i = 0; i < poll.Choices.Count; i++)
            {
                var choice = poll.Choices[i];
                if (choice.IsVoted) _pollSelection.Remove(i);
                PollPanel.Children.Add(BuildPollChoice(choice, i, total, showResults, poll.Multiple, canVote && !choice.IsVoted));
            }

            PollPanel.Children.Add(BuildPollFooter(item, poll, total, expired, showResults, canVote, now));
            PollPanel.Visibility = Visibility.Visible;
        }

        private FrameworkElement BuildPollChoice(SocialPollChoice choice, int index, int total, bool showResults, bool multiple, bool canVote)
        {
            var root = new Grid { MinHeight = 32 };

            var frame = new Border { BorderThickness = new Thickness(1) };
            BindingOperations.SetBinding(frame, Border.BorderBrushProperty, TilePlate());
            root.Children.Add(frame);

            var share = showResults && total > 0 ? (double)choice.Votes / total : 0;
            if (showResults && share > 0)
            {
                var bars = new Grid();
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - share), GridUnitType.Star) });

                var bar = new Rectangle();
                BindingOperations.SetBinding(bar, Shape.FillProperty, TilePlate());
                bars.Children.Add(bar);
                root.Children.Add(bars);
            }

            var line = new Grid { Padding = new Thickness(10, 6, 10, 6), ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            CheckBox check = null;
            if (multiple && canVote)
            {
                check = new CheckBox
                {
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsChecked = _pollSelection.Contains(index),
                    Tag = index
                };
                AutomationProperties.SetName(check, choice.Text ?? "");
                check.Checked += PollCheck_Changed;
                check.Unchecked += PollCheck_Changed;
                line.Children.Add(check);
            }
            else if (choice.IsVoted && showResults)
            {
                line.Children.Add(PollGlyph(""));
            }
            else if (!showResults || canVote)
            {
                line.Children.Add(PollGlyph(multiple ? "" : ""));
            }

            var text = new TextBlock
            {
                Text = choice.Text ?? "",
                Style = StyleOf("BodyTextBlockStyle"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (showResults && choice.IsVoted) text.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(text, 1);
            line.Children.Add(text);

            string spoken;
            if (showResults)
            {
                var percent = total > 0 ? (int)Math.Round(share * 100, MidpointRounding.AwayFromZero) : 0;
                var label = new TextBlock
                {
                    Text = LocalizedStrings.Format("SocialPollPercentFormat", percent),
                    Style = StyleOf("CaptionTextBlockStyle"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                BindingOperations.SetBinding(label, TextBlock.ForegroundProperty,
                                             new Binding { Source = TimeTextBlock, Path = new PropertyPath("Foreground") });
                Grid.SetColumn(label, 2);
                line.Children.Add(label);

                spoken = LocalizedStrings.Format(choice.IsVoted ? "SocialPollChoiceVotedAutomationFormat" : "SocialPollChoiceResultAutomationFormat",
                                                 choice.Text ?? "", percent, CountText.Format(choice.Votes));
            }
            else
            {
                spoken = choice.Text ?? "";
            }

            root.Children.Add(line);

            if (canVote && !multiple)
            {
                var button = new Button
                {
                    Style = StyleOf("SocialBareButtonStyle"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = root,
                    Tag = index
                };
                AutomationProperties.SetName(button, LocalizedStrings.Format("SocialPollVoteForAutomationFormat", spoken));
                button.Click += PollChoice_Click;
                return button;
            }

            if (check != null) AutomationProperties.SetName(check, spoken);
            AutomationProperties.SetName(root, spoken);
            return root;
        }

        private static FontIcon PollGlyph(string glyph)
        {
            return new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                IsTextScaleFactorEnabled = false
            };
        }

        private FrameworkElement BuildPollFooter(SocialNoteItem item, SocialPoll poll, int total, bool expired, bool showResults,
                                                 bool canVote, DateTimeOffset now)
        {
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            if (canVote && poll.Multiple)
            {
                _pollVoteButton = new Button
                {
                    Content = LocalizedStrings.Get("SocialPollVoteButton"),
                    IsEnabled = _pollSelection.Count > 0,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _pollVoteButton.Click += PollVoteButton_Click;
                footer.Children.Add(_pollVoteButton);
            }

            var votes = total == 1
                        ? LocalizedStrings.Get("SocialPollVoteOne")
                        : LocalizedStrings.Format("SocialPollVotesFormat", CountText.Format(total));

            string status;
            if (expired)
            {
                status = LocalizedStrings.Get("RelativeTimeEnded");
            }
            else if (poll.ExpiresAt.HasValue)
            {
                status = RelativeTime.Remaining(poll.ExpiresAt.Value, now);
            }
            else
            {
                status = null;
            }

            var summary = new TextBlock
            {
                Text = status == null ? votes : LocalizedStrings.Format("SocialPollFooterFormat", votes, status),
                Style = StyleOf("CaptionTextBlockStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            BindingOperations.SetBinding(summary, TextBlock.ForegroundProperty,
                                         new Binding { Source = TimeTextBlock, Path = new PropertyPath("Foreground") });
            footer.Children.Add(summary);

            if (!showResults)
            {
                var reveal = new HyperlinkButton
                {
                    Content = new TextBlock
                    {
                        Text = LocalizedStrings.Get("SocialPollShowResults"),
                        Style = StyleOf("CaptionTextBlockStyle")
                    },
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                reveal.Click += PollRevealButton_Click;
                footer.Children.Add(reveal);
            }

            return footer;
        }

        private void PollRevealButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null) item.IsPollRevealed = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not reveal the poll results - {ex.Message}");
            }
        }

        private void PollCheck_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var check = sender as CheckBox;
                if (check == null || !(check.Tag is int)) return;

                var index = (int)check.Tag;
                if (check.IsChecked.GetValueOrDefault()) _pollSelection.Add(index);
                else _pollSelection.Remove(index);

                if (_pollVoteButton != null) _pollVoteButton.IsEnabled = _pollSelection.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not select a poll choice - {ex.Message}");
            }
        }

        private void PollChoice_Click(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null && element.Tag is int) Vote(new[] { (int)element.Tag });
        }

        private void PollVoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pollSelection.Count > 0) Vote(new List<int>(_pollSelection));
        }

        private async void Vote(IReadOnlyList<int> choices)
        {
            try
            {
                var item = Item;
                if (item == null) return;

                var sorted = new List<int>(choices);
                sorted.Sort();
                await SocialNoteActionService.VoteAsync(this, item, sorted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: vote failed - {ex.Message}");
            }
        }
    }
}
