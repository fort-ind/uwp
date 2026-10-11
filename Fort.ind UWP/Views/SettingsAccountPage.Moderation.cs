using System;
using System.Collections.ObjectModel;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsAccountPage
    {
        private const int ModerationFirstPage = 10;

        private const int ModerationMorePage = 30;

        private enum ModerationState
        {
            Loading,
            Ready,
            Empty,
            Failed,
            NeedsPermission
        }

        private sealed class ModerationFeed
        {
            public ModerationFeed(SocialModerationList list)
            {
                List = list;
            }

            public SocialModerationList List { get; private set; }

            public ObservableCollection<SocialUserItem> Items { get; } = new ObservableCollection<SocialUserItem>();

            public bool Requested { get; set; }

            public bool HasMore { get; set; }

            public bool LastLoadWasMore { get; set; }

            public int Version { get; set; }

            public ModerationState State { get; set; }
        }

        private readonly ModerationFeed[] _moderationFeeds =
        {
            new ModerationFeed(SocialModerationList.Muted),
            new ModerationFeed(SocialModerationList.RenoteMuted),
            new ModerationFeed(SocialModerationList.Blocked)
        };

        private SocialModerationList _moderationCurrent = SocialModerationList.Muted;

        private string _moderationAccount;

        private ModerationFeed CurrentModerationFeed
        {
            get { return _moderationFeeds[(int)_moderationCurrent]; }
        }

        private void LoadModeration()
        {
            var account = SocialContentService.CurrentAccountId();
            MutedBlockedSection.Visibility = account == null ? Visibility.Collapsed : Visibility.Visible;

            if (!string.Equals(account, _moderationAccount, StringComparison.Ordinal))
            {
                _moderationAccount = account;
                foreach (var feed in _moderationFeeds)
                {
                    feed.Version++;
                    feed.Requested = false;
                    feed.HasMore = false;
                    feed.Items.Clear();
                }
            }

            if (account == null) return;

            var tab = TabFor(_moderationCurrent);
            if (tab.IsChecked.GetValueOrDefault())
            {
                ShowModerationList(_moderationCurrent);
            }
            else
            {
                tab.IsChecked = true;
            }
        }

        private SocialTabButton TabFor(SocialModerationList list)
        {
            switch (list)
            {
                case SocialModerationList.RenoteMuted: return RenoteMutedTab;
                case SocialModerationList.Blocked: return BlockedTab;
                default: return MutedTab;
            }
        }

        private void ModerationTab_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender == RenoteMutedTab) ShowModerationList(SocialModerationList.RenoteMuted);
                else if (sender == BlockedTab) ShowModerationList(SocialModerationList.Blocked);
                else ShowModerationList(SocialModerationList.Muted);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not switch the muted and blocked list", ex);
            }
        }

        private void ShowModerationList(SocialModerationList list)
        {
            if (_moderationAccount == null) return;

            _moderationCurrent = list;
            var feed = CurrentModerationFeed;
            ModerationList.ItemsSource = feed.Items;
            AutomationProperties.SetName(ModerationList, LocalizedStrings.Get(ListNameKey(list)));

            if (!feed.Requested)
            {
                LoadModerationFeed(feed, false);
            }
            else
            {
                ApplyModerationState(feed);
            }
        }

        private static string ListNameKey(SocialModerationList list)
        {
            switch (list)
            {
                case SocialModerationList.RenoteMuted: return "SocialModerationRenoteMutedListName";
                case SocialModerationList.Blocked: return "SocialModerationBlockedListName";
                default: return "SocialModerationMutedListName";
            }
        }

        private static string PermissionFor(SocialModerationList list)
        {
            return list == SocialModerationList.Blocked ? SocialPermissions.ReadBlocks : SocialPermissions.ReadMutes;
        }

        private async void LoadModerationFeed(ModerationFeed feed, bool more)
        {
            var version = ++feed.Version;
            feed.Requested = true;
            feed.LastLoadWasMore = more;

            try
            {
                var permission = PermissionFor(feed.List);
                if (!SocialPermissions.Has(permission))
                {
                    SetModerationState(feed, ModerationState.NeedsPermission);
                    return;
                }

                SetModerationState(feed, ModerationState.Loading);

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var untilId = more && feed.Items.Count > 0 ? feed.Items[feed.Items.Count - 1].PagingId : null;
                var limit = more ? ModerationMorePage : ModerationFirstPage;
                var result = await SocialApiService.GetModerationListAsync(token, feed.List, untilId, limit, CancellationToken.None);
                if (version != feed.Version) return;

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SocialPermissions.Forget(permission);
                    SetModerationState(feed, ModerationState.NeedsPermission);
                    return;
                }

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetModerationState(feed, ModerationState.Failed);
                    return;
                }

                if (!more) feed.Items.Clear();
                foreach (var item in SocialUserItem.CreateAll(result.Value, feed.List))
                {
                    feed.Items.Add(item);
                }

                feed.HasMore = result.Value.Count >= limit;
                SetModerationState(feed, feed.Items.Count == 0 ? ModerationState.Empty : ModerationState.Ready);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not load the muted and blocked list", ex);
                if (version == feed.Version) SetModerationState(feed, ModerationState.Failed);
            }
        }

        private void SetModerationState(ModerationFeed feed, ModerationState state)
        {
            feed.State = state;
            if (feed.List == _moderationCurrent) ApplyModerationState(feed);
        }

        private void ApplyModerationState(ModerationFeed feed)
        {
            var state = feed.State;
            ModerationLoadingRing.IsActive = state == ModerationState.Loading;
            ModerationLoadingRing.Visibility = state == ModerationState.Loading ? Visibility.Visible : Visibility.Collapsed;
            ModerationMoreButton.Visibility = state == ModerationState.Ready && feed.HasMore ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state == ModerationState.Empty || state == ModerationState.Failed || state == ModerationState.NeedsPermission;
            ModerationStatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            if (!showPanel) return;

            switch (state)
            {
                case ModerationState.Empty:
                    ModerationStateText.Text = LocalizedStrings.Get(EmptyKey(feed.List));
                    ModerationStateButton.Visibility = Visibility.Collapsed;
                    break;
                case ModerationState.Failed:
                    ModerationStateText.Text = LocalizedStrings.Get("SocialModerationFailed");
                    ModerationStateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                    ModerationStateButton.Visibility = Visibility.Visible;
                    break;
                default:
                    ModerationStateText.Text = LocalizedStrings.Get("SocialModerationNeedsPermission");
                    ModerationStateButton.Content = LocalizedStrings.Get("SocialFollowRequestsSignInButton");
                    ModerationStateButton.Visibility = Visibility.Visible;
                    break;
            }

            AutomationHelper.AnnounceLiveRegion(ModerationStateText);
        }

        private static string EmptyKey(SocialModerationList list)
        {
            switch (list)
            {
                case SocialModerationList.RenoteMuted: return "SocialModerationRenoteMutedEmpty";
                case SocialModerationList.Blocked: return "SocialModerationBlockedEmpty";
                default: return "SocialModerationMutedEmpty";
            }
        }

        private async void ModerationStateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var feed = CurrentModerationFeed;
                if (feed.State == ModerationState.NeedsPermission)
                {
                    await SocialPermissions.OfferSignInAsync(this, SocialSignInPrompt.MutedBlocked);
                }
                else if (feed.State == ModerationState.Failed)
                {
                    LoadModerationFeed(feed, feed.LastLoadWasMore && feed.Items.Count > 0);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: muted and blocked retry failed", ex);
            }
        }

        private void ModerationMoreButton_Click(object sender, RoutedEventArgs e)
        {
            var feed = CurrentModerationFeed;
            if (feed.State == ModerationState.Ready && feed.HasMore) LoadModerationFeed(feed, true);
        }

        private async void ModerationList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SocialUserItem;
                if (item != null) await SocialWindows.ShowUserAsync(item.User);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not open the profile", ex);
            }
        }
    }
}
