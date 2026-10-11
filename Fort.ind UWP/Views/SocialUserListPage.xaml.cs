using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialUserListPage : Page, IReleasablePage, IReopenablePage, ITrimmablePage
    {
        private enum ListState
        {
            Loading,
            Ready,
            Empty,
            Failed,
            Private,
            NeedsPermission
        }

        private readonly ListFeed[] _feeds;

        private SocialUserListArgs _args;

        private SocialFollowList _current = SocialFollowList.Following;

        private ListState _state = ListState.Loading;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private bool _followHandlerAttached;

        private bool _requestHandlerAttached;

        private bool _released;

        public SocialUserListPage()
        {
            this.InitializeComponent();

            _feeds = new[]
            {
                new ListFeed(this, SocialFollowList.Following),
                new ListFeed(this, SocialFollowList.Followers),
                new ListFeed(this, SocialFollowList.Requests)
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                var args = e.Parameter as SocialUserListArgs;
                if (args == null) return;

                if (!SocialContentService.IsCurrentAccount(args.AccountId))
                {
                    WindowManagerService.CloseCurrentWindow();
                    return;
                }

                _args = args;
                PaintRemoteNotice();
                UpdateRequestsTab();
                SelectTab(args.List);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not show the list", ex);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (e.NavigationMode == NavigationMode.Back) Release();
        }

        public void Reopen(object parameter)
        {
            var args = parameter as SocialUserWindowArgs;
            if (args == null || _args == null) return;

            if (!SocialContentService.IsCurrentAccount(args.AccountId))
            {
                WindowManagerService.CloseCurrentWindow();
                return;
            }

            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            DetachFollowHandler();
            DetachRequestHandler();

            try
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not cancel pending loads", ex);
            }

            foreach (var feed in _feeds)
            {
                feed.Collection.ReplaceAll(new SocialUserItem[0], false);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_released) return;

                if (!_requestHandlerAttached && IsOwnList)
                {
                    SocialFollowRequestService.Changed += SocialFollowRequestService_Changed;
                    _requestHandlerAttached = true;
                    RefreshPendingRequests();
                }

                if (_followHandlerAttached) return;

                SocialFollowService.Changed += SocialFollowService_Changed;
                _followHandlerAttached = true;

                RefreshFollowStates();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not watch follow changes", ex);
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachFollowHandler();
            DetachRequestHandler();
            Trim();
        }

        public void Trim()
        {
            if (_released) return;

            foreach (var feed in _feeds)
            {
                feed.Collection.TrimToFirstPage();
            }
        }

        private void DetachFollowHandler()
        {
            if (!_followHandlerAttached) return;

            SocialFollowService.Changed -= SocialFollowService_Changed;
            _followHandlerAttached = false;
        }

        private async void SocialFollowService_Changed(object sender, SocialFollowChangedEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_released) return;

                        if (e.User == null)
                        {
                            if (LeaveIfAccountChanged()) return;

                            RefreshFollowStates();
                            return;
                        }

                        foreach (var feed in _feeds)
                        {
                            foreach (var item in feed.Collection)
                            {
                                item.Apply(e.User);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialUserListPage: could not show a follow change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: follow change handler failed", ex);
            }
        }

        private bool LeaveIfAccountChanged()
        {
            if (_args == null || SocialContentService.IsCurrentAccount(_args.AccountId)) return false;

            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                WindowManagerService.CloseCurrentWindow();
            }

            return true;
        }

        private void RefreshFollowStates()
        {
            foreach (var feed in _feeds)
            {
                foreach (var item in feed.Collection)
                {
                    item.RefreshFollowState();
                }
            }
        }

        private void PaintRemoteNotice()
        {
            var user = _args.User;
            var host = user == null ? null : user.Host;
            if (string.IsNullOrWhiteSpace(host)
                || Uri.CheckHostName(host) == UriHostNameType.Unknown
                || !SocialFollowService.ShowRemoteListNotice)
            {
                RemoteNotice.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateRemoteNoticeText();
            RemoteNotice.Visibility = Visibility.Visible;
        }

        private void UpdateRemoteNoticeText()
        {
            if (_args == null || _args.User == null || string.IsNullOrWhiteSpace(_args.User.Host)) return;

            var name = SocialNoteItem.DisplayNameOf(_args.User);
            RemoteNoticeText.Inlines.Clear();
            RemoteNoticeText.Inlines.Add(new Run
            {
                Text = LocalizedStrings.Format(_current == SocialFollowList.Following ? "SocialRemoteFollowingNoticeFormat" : "SocialRemoteFollowersNoticeFormat",
                                               name) + " "
            });

            var link = new Hyperlink();
            link.Inlines.Add(new Run { Text = LocalizedStrings.Format("SocialRemoteListLinkFormat", _args.User.Host) });
            link.Click += RemoteListLink_Click;
            RemoteNoticeText.Inlines.Add(link);
        }

        private async void RemoteListLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
        {
            try
            {
                var user = _args == null ? null : _args.User;
                if (user == null || string.IsNullOrWhiteSpace(user.Host) || Uri.CheckHostName(user.Host) == UriHostNameType.Unknown) return;

                var path = _current == SocialFollowList.Following ? "/following" : "/followers";
                await WebLauncher.LaunchAsync($"https://{user.Host}/@{Uri.EscapeDataString(user.Username)}{path}");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not open the remote list", ex);
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == FollowingTab) ShowList(SocialFollowList.Following);
            else if (sender == FollowersTab) ShowList(SocialFollowList.Followers);
            else if (sender == RequestsTab) ShowList(SocialFollowList.Requests);
        }

        private void SelectTab(SocialFollowList list)
        {
            var tab = list == SocialFollowList.Followers ? FollowersTab
                      : list == SocialFollowList.Requests && RequestsTab.IsShown() ? RequestsTab
                      : FollowingTab;
            if (tab.IsChecked.GetValueOrDefault())
            {
                ShowList(list);
            }
            else
            {
                tab.IsChecked = true;
            }
        }

        private void ShowList(SocialFollowList list)
        {
            if (_args == null || _released) return;

            if (list != _current) _feeds[(int)_current].Collection.TrimToFirstPage();
            _current = list;
            var feed = _feeds[(int)list];
            UsersList.ItemsSource = feed.Collection;
            AutomationProperties.SetName(UsersList, LocalizedStrings.Format(ListNameKey(list),
                                                                            _args.User != null ? SocialNoteItem.DisplayNameOf(_args.User) : _args.Handle));
            UpdateRemoteNoticeText();

            if (!feed.Requested)
            {
                LoadList(feed);
            }
            else
            {
                SetState(feed.State);
            }
        }

        private async void LoadList(ListFeed feed)
        {
            var version = ++feed.Version;
            feed.Requested = true;
            SetFeedState(feed, ListState.Loading);

            try
            {
                var result = await FetchAsync(feed.List, null, _cancellation.Token);
                if (version != feed.Version || _released) return;

                if (result.Status == SocialApiStatus.Refused)
                {
                    SetFeedState(feed, ListState.Private);
                    return;
                }

                if (result.Status == SocialApiStatus.PermissionDenied && feed.List == SocialFollowList.Requests)
                {
                    SocialPermissions.Forget(SocialPermissions.ReadFollowing);
                    SetFeedState(feed, ListState.NeedsPermission);
                    return;
                }

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetFeedState(feed, ListState.Failed);
                    return;
                }

                var items = SocialUserItem.CreateAll(result.Value, feed.List == SocialFollowList.Requests);
                feed.Collection.ReplaceAll(items, result.Value.Count > 0);
                SetFeedState(feed, items.Count == 0 ? ListState.Empty : ListState.Ready);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialUserListPage: list load cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: list load failed", ex);
                if (version == feed.Version && !_released) SetFeedState(feed, ListState.Failed);
            }
        }

        private async Task<SocialApiResult<IReadOnlyList<SocialFollowEntry>>> FetchAsync(SocialFollowList list, string untilId,
                                                                                         CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            if (list == SocialFollowList.Requests)
            {
                if (!SocialPermissions.Has(SocialPermissions.ReadFollowing))
                {
                    return SocialApiResult<IReadOnlyList<SocialFollowEntry>>.Failed(SocialApiStatus.PermissionDenied);
                }

                return await SocialApiService.GetFollowRequestsAsync(token, untilId, AppConstants.SocialFeedPageSize, cancellationToken);
            }

            return await SocialApiService.GetFollowListAsync(token, _args.UserId, list, untilId,
                                                             AppConstants.SocialFeedPageSize, cancellationToken);
        }

        private async Task<IReadOnlyList<SocialUserItem>> LoadMoreAsync(SocialFollowList list, string untilId, CancellationToken cancellationToken)
        {
            if (_args == null || _released) return null;

            var result = await FetchAsync(list, untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return SocialUserItem.CreateAll(result.Value, list == SocialFollowList.Requests);
        }

        private void SetFeedState(ListFeed feed, ListState state)
        {
            feed.State = state;
            if (feed.List == _current) SetState(state);
        }

        private void SetState(ListState state)
        {
            _state = state;

            LoadingRing.IsActive = state == ListState.Loading;
            LoadingRing.Visibility = state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state != ListState.Loading && state != ListState.Ready;
            StatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            FooterHost.Visibility = state == ListState.Ready ? Visibility.Collapsed : Visibility.Visible;
            if (!showPanel) return;

            switch (state)
            {
                case ListState.Empty:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get(_current == SocialFollowList.Following ? "SocialFollowingEmpty"
                                                          : _current == SocialFollowList.Requests ? "SocialFollowRequestsEmpty"
                                                          : "SocialFollowersEmpty");
                    StateButton.Visibility = Visibility.Collapsed;
                    break;
                case ListState.NeedsPermission:
                    StateGlyph.Glyph = "\uE72E";
                    StateText.Text = LocalizedStrings.Get("SocialFollowRequestsNeedPermission");
                    StateButton.Content = LocalizedStrings.Get("SocialFollowRequestsSignInButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
                case ListState.Private:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialFollowListPrivate");
                    StateButton.Visibility = Visibility.Collapsed;
                    break;
                case ListState.Failed:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialFollowListFailed");
                    StateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
            }

            AutomationHelper.AnnounceLiveRegion(StateText);
        }

        private void StateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_state == ListState.Failed) LoadList(_feeds[(int)_current]);
                else if (_state == ListState.NeedsPermission) SignInAgain();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: retry failed", ex);
            }
        }

        private bool IsOwnList
        {
            get { return _args != null && string.Equals(_args.UserId, _args.AccountId, StringComparison.Ordinal); }
        }

        private static string ListNameKey(SocialFollowList list)
        {
            switch (list)
            {
                case SocialFollowList.Following: return "SocialFollowingListNameFormat";
                case SocialFollowList.Requests: return "SocialFollowRequestsListNameFormat";
                default: return "SocialFollowersListNameFormat";
            }
        }

        private void UpdateRequestsTab()
        {
            var show = false;
            if (IsOwnList)
            {
                var privacy = SocialPrivacySettings.FromProfile(ProfileService.CurrentUser);
                show = _args.List == SocialFollowList.Requests
                       || _current == SocialFollowList.Requests
                       || SocialFollowRequestService.PendingCount.GetValueOrDefault() > 0
                       || (privacy != null && privacy.IsLocked);
            }

            RequestsTab.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RefreshPendingRequests()
        {
            try
            {
                await SocialFollowRequestService.RefreshPendingAsync(_cancellation.Token);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not check the pending follow requests", ex);
            }
        }

        private void DetachRequestHandler()
        {
            if (!_requestHandlerAttached) return;

            SocialFollowRequestService.Changed -= SocialFollowRequestService_Changed;
            _requestHandlerAttached = false;
        }

        private async void SocialFollowRequestService_Changed(object sender, SocialFollowRequestChangedEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_released) return;

                        UpdateRequestsTab();
                        if (e.UserId == null) return;

                        var feed = _feeds[(int)SocialFollowList.Requests];
                        foreach (var item in feed.Collection.Where(i => string.Equals(i.User.Id, e.UserId, StringComparison.Ordinal)))
                        {
                            item.RequestState = e.State;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialUserListPage: could not show a follow request change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: follow request change handler failed", ex);
            }
        }

        private async void SignInAgain()
        {
            try
            {
                await SocialPermissions.OfferSignInAsync(this, SocialSignInPrompt.FollowRequests);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not offer the sign-in", ex);
            }
        }

        private async void UsersList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SocialUserItem;
                if (item != null) await SocialWindows.ShowUserAsync(item.User);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialUserListPage: could not open the profile", ex);
            }
        }

        private sealed class ListFeed
        {
            public ListFeed(SocialUserListPage page, SocialFollowList list)
            {
                List = list;
                Collection = new SocialFeedCollection<SocialUserItem>(
                    (untilId, token) => page.LoadMoreAsync(list, untilId, token),
                    SocialFeedPaging.NonEmpty);
            }

            public SocialFollowList List { get; private set; }

            public SocialFeedCollection<SocialUserItem> Collection { get; private set; }

            public bool Requested { get; set; }

            public int Version { get; set; }

            public ListState State { get; set; }
        }
    }
}
