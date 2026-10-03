using System;
using System.Diagnostics;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialNoteShortcut
    {
        Reply,
        Like,
        React,
        Renote,
        Quote,
        Delete
    }

    public sealed partial class SocialNoteView
    {
        private const int MyReactionImageSize = 16;

        private Uri _myReactionUri;

        private void RenderCounts(SocialNoteItem item)
        {
            var hidden = item.IsHidden;
            var canAct = SocialNoteActionService.CanAct(item);

            SetCount(RepliesCountText, hidden ? 0 : item.RepliesCount);
            ReplyButton.IsEnabled = !item.IsDeleted;
            var replies = LocalizedStrings.Format("SocialActionReplyAutomationFormat", CountText.Format(hidden ? 0 : item.RepliesCount));
            AutomationProperties.SetName(ReplyButton, replies);
            ToolTipService.SetToolTip(ReplyButton, LocalizedStrings.Get("SocialActionReplyTooltip"));

            SetCount(RenotesCountText, item.RenoteCount);
            RenoteButton.IsEnabled = SocialNoteActionService.CanRenote(item);
            SetActive(RenoteIcon, item.IsRenotedByMe);
            SetActive(RenotesCountText, item.IsRenotedByMe);
            AutomationProperties.SetName(RenoteButton,
                                         LocalizedStrings.Format(item.IsRenotedByMe ? "SocialActionRenotedAutomationFormat" : "SocialActionRenoteAutomationFormat",
                                                                 CountText.Format(item.RenoteCount)));
            ToolTipService.SetToolTip(RenoteButton, LocalizedStrings.Get("SocialActionRenoteTooltip"));

            RenderReactButton(item, canAct);
        }

        private void RenderReactButton(SocialNoteItem item, bool canAct)
        {
            var mine = item.MyReaction;
            var liked = mine != null && SocialReactions.IsLike(mine);
            var other = mine != null && !liked;

            LikeIcon.Visibility = mine == null ? Visibility.Visible : Visibility.Collapsed;
            LikedIcon.Visibility = liked ? Visibility.Visible : Visibility.Collapsed;

            var image = other ? item.ReactionImageUri(mine) : null;
            if (image != null)
            {
                if (!Equals(image, _myReactionUri))
                {
                    _myReactionUri = image;
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.DecodePixelType = DecodePixelType.Logical;
                    bitmap.DecodePixelHeight = MyReactionImageSize;
                    bitmap.UriSource = image;
                    MyReactionImage.Source = bitmap;
                }

                MyReactionImage.Visibility = Visibility.Visible;
                MyReactionText.Visibility = Visibility.Collapsed;
            }
            else
            {
                _myReactionUri = null;
                MyReactionImage.Source = null;
                MyReactionImage.Visibility = Visibility.Collapsed;
                MyReactionText.Text = other ? SocialReactions.SpokenName(mine) : "";
                MyReactionText.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
            }

            SetCount(ReactionsCountText, item.IsHidden ? 0 : item.ReactionCount);
            SetActive(ReactionsCountText, mine != null);

            ReactButton.IsEnabled = canAct;
            SyncReactChecked(item);

            string name;
            if (mine == null)
            {
                name = LocalizedStrings.Format("SocialActionLikeAutomationFormat", CountText.Format(item.ReactionCount));
            }
            else if (liked)
            {
                name = LocalizedStrings.Format("SocialActionLikedAutomationFormat", CountText.Format(item.ReactionCount));
            }
            else
            {
                name = LocalizedStrings.Format("SocialActionReactedAutomationFormat", SocialReactions.SpokenName(mine), CountText.Format(item.ReactionCount));
            }

            AutomationProperties.SetName(ReactButton, name);
            ToolTipService.SetToolTip(ReactButton, LocalizedStrings.Get(mine == null ? "SocialActionLikeTooltip" : "SocialActionUnlikeTooltip"));
        }

        private void SyncReactChecked(SocialNoteItem item)
        {
            var reacted = item != null && item.MyReaction != null;
            if (ReactButton.IsChecked != reacted) ReactButton.IsChecked = reacted;
        }

        private static void SetCount(TextBlock text, int count)
        {
            text.Text = count > 0 ? CountText.Format(count) : "";
            text.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetActive(FrameworkElement element, bool active)
        {
            var property = element is TextBlock ? TextBlock.ForegroundProperty : IconElement.ForegroundProperty;
            if (active)
            {
                BindingOperations.SetBinding(element, property, new Binding { Source = ReactedBrushSource, Path = new PropertyPath("Background") });
            }
            else
            {
                element.ClearValue(property);
            }
        }

        private void ReplyButton_Click(object sender, RoutedEventArgs e)
        {
            Reply();
        }

        private async void Reply()
        {
            try
            {
                var item = Item;
                if (item == null || item.IsDeleted) return;

                if (!SocialThreads.Open(this, item.Note, item.Note.Id, true, SocialThreadTab.Replies))
                {
                    await WebLauncher.LaunchAsync(item.NoteUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the reply - {ex.Message}");
            }
        }

        private void RenoteButton_Click(object sender, RoutedEventArgs e)
        {
            OpenRenoteMenu();
        }

        private void OpenRenoteMenu()
        {
            try
            {
                var item = Item;
                if (!SocialNoteActionService.CanRenote(item)) return;

                var flyout = SocialMenus.BuildRenote(item, this);
                SocialMenus.ShowAt(flyout, RenoteButton, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the renote menu - {ex.Message}");
            }
        }

        private async void ReactButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
        {
            try
            {
                var item = Item;
                SyncReactChecked(item);
                if (item != null) await SocialNoteActionService.ToggleLikeAsync(this, item);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: like failed - {ex.Message}");
            }
        }

        private void ReactFlyout_Opening(object sender, object e)
        {
            try
            {
                var item = Item;
                if (item == null) return;

                var picker = SocialEmojiPicker.ForCurrentView();
                picker.AttachTo(ReactFlyout);
                picker.Configure(SocialEmojiPickerMode.Reaction, item.MyReaction, item.Note.ReactionAcceptance,
                                 key => React(item, key),
                                 () => React(item, SocialReactions.Like),
                                 () => Unreact(item));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the emoji picker - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async void React(SocialNoteItem item, string key)
        {
            try
            {
                await SocialNoteActionService.ReactAsync(this, item, key);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: reaction failed - {ex.Message}");
            }
        }

        private async void Unreact(SocialNoteItem item)
        {
            try
            {
                await SocialNoteActionService.UnreactAsync(this, item);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not remove the reaction - {ex.Message}");
            }
        }

        private void ReactionChip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item == null) return;

                var chip = _chips.FirstOrDefault(candidate => ReferenceEquals(candidate.Button, sender));
                if (chip == null) return;

                chip.Button.IsChecked = SocialReactions.AreSame(chip.Key, item.MyReaction);
                if (chip.Key != null && SocialReactions.CanReactWith(chip.Key)) React(item, chip.Key);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: chip reaction failed - {ex.Message}");
            }
        }

        private async void Quote(SocialNoteItem item)
        {
            try
            {
                await SocialWindows.ShowComposeAsync(this, SocialComposeMode.Quote, item.Note);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the quote - {ex.Message}");
            }
        }

        private async void DeleteNote()
        {
            try
            {
                var item = Item;
                if (item != null && item.IsMine) await SocialNoteActionService.DeleteAsync(this, item);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: delete failed - {ex.Message}");
            }
        }

        internal bool InvokeShortcut(SocialNoteShortcut shortcut)
        {
            var item = Item;
            if (item == null || item.IsDeleted) return false;

            switch (shortcut)
            {
                case SocialNoteShortcut.Reply:
                    Reply();
                    return true;
                case SocialNoteShortcut.Like:
                    if (!SocialNoteActionService.CanAct(item)) return false;
                    React(item, item.MyReaction != null ? item.MyReaction : SocialReactions.Like);
                    return true;
                case SocialNoteShortcut.React:
                    if (!SocialNoteActionService.CanAct(item)) return false;
                    ReactFlyout.ShowAt(ReactButton);
                    return true;
                case SocialNoteShortcut.Renote:
                    if (!SocialNoteActionService.CanRenote(item)) return false;
                    OpenRenoteMenu();
                    return true;
                case SocialNoteShortcut.Quote:
                    if (!SocialNoteActionService.CanRenote(item)) return false;
                    Quote(item);
                    return true;
                case SocialNoteShortcut.Delete:
                    if (!item.IsMine) return false;
                    DeleteNote();
                    return true;
                default:
                    return false;
            }
        }
    }
}
