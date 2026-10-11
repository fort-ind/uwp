using System;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialUserRow : UserControl
    {
        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register("Item", typeof(SocialUserItem), typeof(SocialUserRow),
                                        new PropertyMetadata(null, OnItemChanged));

        private SocialUserItem _subscribed;

        private bool _isLoaded;

        public SocialUserRow()
        {
            this.InitializeComponent();

            Loaded += SocialUserRow_Loaded;
            Unloaded += SocialUserRow_Unloaded;
        }

        public SocialUserItem Item
        {
            get { return (SocialUserItem)GetValue(ItemProperty); }
            set { SetValue(ItemProperty, value); }
        }

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var row = d as SocialUserRow;
            if (row == null) return;

            try
            {
                row.Bind(e.NewValue as SocialUserItem);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserRow: could not show a person", ex);
            }
        }

        private void SocialUserRow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            Subscribe(Item);
            UpdateFollowButton(Item);
        }

        private void SocialUserRow_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            Subscribe(null);
        }

        private void Subscribe(SocialUserItem item)
        {
            if (ReferenceEquals(_subscribed, item)) return;

            if (_subscribed != null) _subscribed.PropertyChanged -= Item_PropertyChanged;
            _subscribed = item;
            if (_subscribed != null) _subscribed.PropertyChanged += Item_PropertyChanged;
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (!ReferenceEquals(sender, Item)) return;

                if (e.PropertyName == "FollowState" || e.PropertyName == "IsBusy" || e.PropertyName == "RequestState"
                    || e.PropertyName == "ModerationActive")
                {
                    UpdateFollowButton(Item);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserRow: could not update the follow button", ex);
            }
        }

        private void Bind(SocialUserItem item)
        {
            Subscribe(_isLoaded ? item : null);
            if (item == null) return;

            AvatarPicture.DisplayName = item.DisplayName ?? "";
            AvatarPicture.ProfilePicture = item.Avatar;
            MfmInlineBuilder.Fill(NameText, item.NameSegments, false, MfmInlineBuilder.NameEmojiSize);
            HandleText.Text = item.Handle;

            UpdateFollowButton(item);
        }

        private void UpdateFollowButton(SocialUserItem item)
        {
            if (item != null && item.Moderation.HasValue)
            {
                UpdateModerationPanel(item);
                return;
            }

            ModerationButton.Visibility = Visibility.Collapsed;
            ModerationOutcomePanel.Visibility = Visibility.Collapsed;
            DetailText.Visibility = Visibility.Collapsed;

            if (item != null && item.IsRequest)
            {
                UpdateRequestPanel(item);
                return;
            }

            RequestPanel.Visibility = Visibility.Collapsed;
            RequestOutcomeText.Visibility = Visibility.Collapsed;

            var state = item == null ? SocialFollowState.None : item.FollowState;
            if (state == SocialFollowState.None)
            {
                FollowButton.Visibility = Visibility.Collapsed;
                return;
            }

            var label = SocialFollowService.LabelFor(state);
            FollowText.Text = label;
            AutomationProperties.SetName(FollowButton, LocalizedStrings.Format("SocialUserRowFollowButtonFormat", label, item.DisplayName));

            if (SocialFollowService.IsEmphasised(state))
            {
                object style;
                if (Resources.TryGetValue("FollowAccentButtonStyle", out style)) FollowButton.Style = style as Style;
            }
            else
            {
                FollowButton.ClearValue(StyleProperty);
            }

            FollowButton.IsEnabled = !item.IsBusy;
            FollowButton.Visibility = Visibility.Visible;
        }

        private void UpdateModerationPanel(SocialUserItem item)
        {
            FollowButton.Visibility = Visibility.Collapsed;
            RequestPanel.Visibility = Visibility.Collapsed;
            RequestOutcomeText.Visibility = Visibility.Collapsed;

            var list = item.Moderation.Value;
            var detail = SocialModerationText.Detail(item);
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;

            var label = SocialModerationText.ActionLabel(list);
            ModerationButton.Content = label;
            AutomationProperties.SetName(ModerationButton, LocalizedStrings.Format("SocialUserRowFollowButtonFormat", label, item.DisplayName));
            ModerationButton.IsEnabled = !item.IsBusy;
            ModerationButton.Visibility = item.ModerationActive ? Visibility.Visible : Visibility.Collapsed;

            ModerationOutcomeText.Text = SocialModerationText.Outcome(list);
            ModerationUndoLink.IsEnabled = !item.IsBusy;
            AutomationProperties.SetName(ModerationUndoLink, LocalizedStrings.Format("SocialModerationUndoNameFormat", item.DisplayName));
            ModerationOutcomePanel.Visibility = item.ModerationActive ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ModerationButton_Click(object sender, RoutedEventArgs e)
        {
            SetModeration(false);
        }

        private void ModerationUndoLink_Click(object sender, RoutedEventArgs e)
        {
            SetModeration(true);
        }

        private async void SetModeration(bool active)
        {
            var item = Item;
            if (item == null || !item.Moderation.HasValue || item.IsBusy || item.ModerationActive == active) return;

            item.IsBusy = true;
            try
            {
                bool changed;
                switch (item.Moderation.Value)
                {
                    case SocialModerationList.Muted:
                        changed = active
                                  ? await SocialModerationService.MuteAsync(this, item.User, SocialModerationText.RemainingMute(item))
                                  : await SocialModerationService.UnmuteAsync(this, item.User);
                        break;
                    case SocialModerationList.RenoteMuted:
                        changed = await SocialModerationService.SetRenotesHiddenAsync(this, item.User, active);
                        break;
                    default:
                        changed = active
                                  ? await SocialModerationService.BlockAsync(this, item.User, false)
                                  : await SocialModerationService.UnblockAsync(this, item.User, true);
                        break;
                }

                if (changed) item.ModerationActive = active;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserRow: could not change the mute or block", ex);
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        private void UpdateRequestPanel(SocialUserItem item)
        {
            FollowButton.Visibility = Visibility.Collapsed;

            var pending = item.RequestState == SocialFollowRequestState.Pending;
            RequestPanel.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            AcceptButton.IsEnabled = !item.IsBusy;
            RejectButton.IsEnabled = !item.IsBusy;
            AutomationProperties.SetName(AcceptButton, LocalizedStrings.Format("SocialFollowRequestAcceptNameFormat", item.DisplayName));
            AutomationProperties.SetName(RejectButton, LocalizedStrings.Format("SocialFollowRequestRejectNameFormat", item.DisplayName));

            var outcome = SocialFollowRequestService.OutcomeText(item.RequestState);
            RequestOutcomeText.Text = outcome;
            RequestOutcomeText.Visibility = string.IsNullOrEmpty(outcome) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            Respond(true);
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            Respond(false);
        }

        private async void Respond(bool accept)
        {
            var item = Item;
            if (item == null || !item.IsRequest || item.IsBusy) return;

            item.IsBusy = true;
            try
            {
                var state = await SocialFollowRequestService.RespondAsync(this, item.User, accept);
                if (state.HasValue)
                {
                    item.RequestState = state.Value;
                    AutomationHelper.AnnounceStatus(this, SocialFollowRequestService.OutcomeText(state.Value), "SocialFollowRequest");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserRow: could not answer the follow request", ex);
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        private async void FollowButton_Click(object sender, RoutedEventArgs e)
        {
            var item = Item;
            if (item == null || item.IsBusy) return;

            item.IsBusy = true;
            try
            {
                var result = await SocialFollowService.ToggleWithFeedbackAsync(this, item.Detail);
                if (result.Updated != null) item.Apply(result.Updated);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserRow: follow action failed", ex);
            }
            finally
            {
                item.IsBusy = false;
            }
        }
    }
}
