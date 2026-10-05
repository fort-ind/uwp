using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Fort.ind_UWP
{
    public sealed partial class SocialNoteView
    {
        private readonly List<SocialReactionChip> _chips = new List<SocialReactionChip>();

        private ToggleButton _overflowChip;

        private TextBlock _overflowText;

        private int _shownReactions;

        private void RenderReactions(SocialNoteItem item)
        {
            var reactions = item.IsHidden || item.IsDeleted ? null : item.Reactions;
            if (reactions == null || reactions.Count == 0)
            {
                _shownReactions = 0;
                if (ReactionChips != null) ReactionChips.Visibility = Visibility.Collapsed;
                return;
            }

            if (ReactionChips == null) FindName("ReactionChips");
            EnsureOverflowChip();

            ReactionChips.SingleLine = !IsFocused;
            _overflowChip.Visibility = IsFocused ? Visibility.Collapsed : Visibility.Visible;
            _overflowChip.IsHitTestVisible = !item.IsReadOnly;
            _shownReactions = reactions.Count;

            for (var i = 0; i < reactions.Count; i++)
            {
                if (i >= _chips.Count)
                {
                    var chip = new SocialReactionChip();
                    chip.Button.IsHitTestVisible = true;
                    chip.Button.Click += ReactionChip_Click;
                    _chips.Add(chip);
                    ReactionChips.Children.Insert(ReactionChips.Children.Count - 1, chip.Button);
                }

                var shown = _chips[i];
                shown.Show(item, reactions[i]);
                shown.Button.IsChecked = SocialReactions.AreSame(reactions[i].Key, item.MyReaction);
                shown.Button.IsHitTestVisible = !item.IsReadOnly;
                shown.Button.IsTabStop = IsFocused && !item.IsReadOnly;
                shown.SetAccessible(IsFocused);
            }

            for (var i = reactions.Count; i < _chips.Count; i++)
            {
                _chips[i].Hide();
            }

            UpdateOverflowText(reactions.Count);
            ReactionChips.Visibility = Visibility.Visible;
            ReactionChips.InvalidateMeasure();
        }

        private void EnsureOverflowChip()
        {
            if (_overflowChip != null) return;

            _overflowText = new TextBlock
            {
                Style = StyleOf("CaptionTextBlockStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };

            _overflowChip = new ToggleButton
            {
                Style = StyleOf("SocialReactionChipStyle"),
                Content = _overflowText
            };
            _overflowChip.Click += OverflowChip_Click;

            ReactionChips.Children.Add(_overflowChip);
            ReactionChips.OverflowElement = _overflowChip;
        }

        private void ReactionChips_VisibleCountChanged(object sender, int visible)
        {
            try
            {
                UpdateOverflowText(visible);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not count the hidden reactions - {ex.Message}");
            }
        }

        private void UpdateOverflowText(int visible)
        {
            if (_overflowText == null) return;

            var hidden = Math.Max(0, _shownReactions - visible);
            var item = Item;
            _overflowChip.IsTabStop = !IsFocused && hidden > 0 && item != null && !item.IsReadOnly;

            var text = LocalizedStrings.Format("SocialReactionsOverflowFormat", CountText.Format(Math.Max(1, hidden)));
            if (!string.Equals(_overflowText.Text, text, StringComparison.Ordinal)) _overflowText.Text = text;

            var name = LocalizedStrings.Format("SocialReactionsOverflowAutomationFormat", CountText.Format(Math.Max(1, hidden)));
            AutomationProperties.SetName(_overflowChip, name);
            ToolTipService.SetToolTip(_overflowChip, name);
        }

        private void OverflowChip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_overflowChip != null) _overflowChip.IsChecked = false;

                var item = Item;
                if (item != null) SocialThreads.Open(this, item.Note, item.Note.Id, false, SocialThreadTab.Reactions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the reactions - {ex.Message}");
            }
        }
    }
}
