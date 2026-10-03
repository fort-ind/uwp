using System;
using System.ComponentModel;
using System.Diagnostics;
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
                Debug.WriteLine($"SocialUserRow: could not show a person - {ex.GetType().Name}: {ex.Message}");
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

                if (e.PropertyName == "FollowState" || e.PropertyName == "IsBusy")
                {
                    UpdateFollowButton(Item);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialUserRow: could not update the follow button - {ex.Message}");
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
                Debug.WriteLine($"SocialUserRow: follow action failed - {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                item.IsBusy = false;
            }
        }
    }
}
