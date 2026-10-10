using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage : Page, IReleasablePage, ITrimmablePage
    {
        private enum FeedState
        {
            Loading,
            Ready,
            Empty,
            Failed,
            NeedsPermission,
            Unavailable
        }

        private const int ForYouIndex = 0;

        private const int NotificationsIndex = 4;

        private static int LastPivotIndex { get; set; } = ForYouIndex;

        private static bool LastShowedMentions { get; set; }

        private readonly TimelineFeed _forYou;

        private readonly TimelineFeed _following;

        private readonly TimelineFeed _local;

        private readonly TimelineFeed _global;

        private readonly TimelineFeed[] _timelines;

        private readonly PivotItem[] _signedInPivotItems;

        private readonly SocialFeedCollection<SocialFeedItem> _notifications;

        private readonly SocialFeedCollection<SocialFeedItem> _mentions;

        private bool _signedOut;

        private bool IsSignedOut => _signedOut;

        private bool _changingPivotItems;

        private bool _showingMentions;

        private bool _tabsReady;

        private string _shownUserId;

        private bool _notificationsRequested;

        private bool _notificationsStale;

        private bool _mentionsRequested;

        private int _notificationsVersion;

        private int _mentionsVersion;

        private FeedState _notificationsState = FeedState.Loading;

        private FeedState _mentionsState = FeedState.Loading;

        private bool _unseenLiveNotifications;

        private bool _loadedBefore;

        private bool _authHandlerAttached;

        private bool _arrivalHandlerAttached;

        private bool _activationHandlerAttached;

        private bool _noteHandlerAttached;

        private int _seenPostsVersion = SocialNoteService.PostsVersion;

        private bool _keepListsOnUnload;

        public SocialPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            _forYou = CreateTimeline(SocialTimeline.Home, ForYouPivotItem, ForYouList,
                                    ForYouRefreshContainer, ForYouRefreshFailedInfoBar, ForYouLoadingRing, ForYouStatePanel,
                                     ForYouStateGlyph, ForYouStateText, ForYouStateButton,
                                     "SocialForYouEmpty", "SocialForYouFailed");
            _following = CreateTimeline(SocialTimeline.Following, FollowingPivotItem, FollowingList,
                                    FollowingRefreshContainer, FollowingRefreshFailedInfoBar, FollowingLoadingRing, FollowingStatePanel,
                                        FollowingStateGlyph, FollowingStateText, FollowingStateButton,
                                        "SocialFollowingFeedEmpty", "SocialFollowingFeedFailed");
            _local = CreateTimeline(SocialTimeline.Local, LocalPivotItem, LocalList,
                                    LocalRefreshContainer, LocalRefreshFailedInfoBar, LocalLoadingRing, LocalStatePanel,
                                    LocalStateGlyph, LocalStateText, LocalStateButton,
                                    "SocialLocalEmpty", "SocialLocalFailed");
            _global = CreateTimeline(SocialTimeline.Global, GlobalPivotItem, GlobalList,
                                    GlobalRefreshContainer, GlobalRefreshFailedInfoBar, GlobalLoadingRing, GlobalStatePanel,
                                     GlobalStateGlyph, GlobalStateText, GlobalStateButton,
                                     "SocialGlobalEmpty", "SocialGlobalFailed");
            _timelines = new[] { _forYou, _following, _local, _global };
            InitializeRefreshes();
            _signedInPivotItems = new[] { ForYouPivotItem, FollowingPivotItem, LocalPivotItem, GlobalPivotItem, NotificationsPivotItem };

            _notifications = new SocialFeedCollection<SocialFeedItem>(LoadMoreNotificationsAsync, SocialFeedPaging.FullPage, true);
            _mentions = new SocialFeedCollection<SocialFeedItem>(LoadMoreMentionsAsync, SocialFeedPaging.FullPage, true);
            NotificationsList.ItemsSource = _notifications;
            MentionsList.ItemsSource = _mentions;

            ApplyNotificationsTab(LastShowedMentions);
            _tabsReady = true;

            Loaded += SocialPage_Loaded;
            Unloaded += SocialPage_Unloaded;
        }

        internal void ShowNotifications()
        {
            LastPivotIndex = NotificationsIndex;
            LastShowedMentions = false;
            if (IsSignedOut) return;

            ShowNotificationsTab(false);
            if (SocialPivot.Items.Count > NotificationsIndex && SocialPivot.SelectedIndex != NotificationsIndex)
            {
                SocialPivot.SelectedIndex = NotificationsIndex;
            }
        }

        private void SocialPage_Loaded(object sender, RoutedEventArgs e)
        {
            _keepListsOnUnload = false;

            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }

            if (!_arrivalHandlerAttached)
            {
                SocialNotificationService.NotificationArrived += OnNotificationArrived;
                _arrivalHandlerAttached = true;
            }

            if (!_activationHandlerAttached)
            {
                Window.Current.Activated += OnWindowActivated;
                _activationHandlerAttached = true;
            }

            if (!_noteHandlerAttached)
            {
                SocialNoteService.Changed += OnNoteChanged;
                _noteHandlerAttached = true;
            }

            RefreshGate();
            RemoveTimelineRows(item => item.IsGone || SocialNoteItem.IsHiddenPerson(item, null));
            foreach (var note in SocialNoteService.PostsSince(_seenPostsVersion))
            {
                InsertPosted(note);
            }
            _seenPostsVersion = SocialNoteService.PostsVersion;

            if (_loadedBefore)
            {
                RefreshTimes();
                CatchUpNotifications();
            }
            _loadedBefore = true;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            _keepListsOnUnload = SocialThreads.IsInPlacePage(e.SourcePageType);
        }

        private void SocialPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Release();
            if (!_keepListsOnUnload) Trim();
        }

        public void Trim()
        {
            foreach (var feed in _timelines)
            {
                feed.Items.TrimToFirstPage();
            }
            _notifications.TrimToFirstPage();
            _mentions.TrimToFirstPage();
        }

        private void TrimPivot(object pivotItem)
        {
            var feed = TimelineFor(pivotItem);
            if (feed != null)
            {
                feed.Items.TrimToFirstPage();
            }
            else if (pivotItem == NotificationsPivotItem)
            {
                _notifications.TrimToFirstPage();
                _mentions.TrimToFirstPage();
            }
        }

        public void Release()
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }

            if (_arrivalHandlerAttached)
            {
                SocialNotificationService.NotificationArrived -= OnNotificationArrived;
                _arrivalHandlerAttached = false;
            }

            if (_activationHandlerAttached)
            {
                try
                {
                    Window.Current.Activated -= OnWindowActivated;
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialPage: could not remove the activation handler", ex);
                }
                _activationHandlerAttached = false;
            }

            if (_noteHandlerAttached)
            {
                SocialNoteService.Changed -= OnNoteChanged;
                _noteHandlerAttached = false;
            }
        }

        private async void OnNoteChanged(object sender, SocialNoteChange change)
        {
            try
            {
                if (change == null) return;
                if (change.Kind != SocialNoteChangeKind.Deleted && change.Kind != SocialNoteChangeKind.Unrenoted
                    && change.Kind != SocialNoteChangeKind.Posted && change.Kind != SocialNoteChangeKind.RelationChanged) return;
                if (change.Kind == SocialNoteChangeKind.RelationChanged && !change.Flag) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (change.Kind == SocialNoteChangeKind.Posted)
                        {
                            InsertPosted(change.Note);
                            _seenPostsVersion = SocialNoteService.PostsVersion;
                        }
                        else if (change.Kind == SocialNoteChangeKind.RelationChanged)
                        {
                            RemoveTimelineRows(item => SocialNoteItem.IsHiddenPerson(item, null));
                        }
                        else
                        {
                            RemoveTimelineRows(item => SocialNoteItem.IsRemovedBy(item, change));
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialPage: could not remove a note", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: note change handler failed", ex);
            }
        }

        private void RefreshGate()
        {
            var user = ProfileService.CurrentUser;
            var userId = user == null ? null : user.UserId;
            if (!string.Equals(userId, _shownUserId, StringComparison.Ordinal))
            {
                _shownUserId = userId;
                ResetFeeds();
            }

            ShowFeeds(user == null);
        }

        private void ShowFeeds(bool signedOut)
        {
            var modeChanged = signedOut != _signedOut || !SocialPivot.IsShown();
            _signedOut = signedOut;

            SignedOutInfoBar.IsOpen = signedOut;
            SignedOutInfoBar.Visibility = signedOut ? Visibility.Visible : Visibility.Collapsed;
            NewNoteButton.Visibility = signedOut ? Visibility.Collapsed : Visibility.Visible;
            ApplyPivotItems(signedOut);
            SocialPivot.Visibility = Visibility.Visible;

            if (modeChanged)
            {
                var target = signedOut ? GlobalPivotItem : PivotItemAt(LastPivotIndex);
                if (SocialPivot.SelectedItem != target) SocialPivot.SelectedItem = target;
            }

            EnsureSelectedFeedLoaded();
        }

        private void ApplyPivotItems(bool signedOut)
        {
            var wanted = signedOut ? new[] { GlobalPivotItem } : _signedInPivotItems;
            var items = SocialPivot.Items;
            if (items.Count == wanted.Length && wanted.Select((item, index) => items[index] == item).All(same => same)) return;

            _changingPivotItems = true;
            try
            {
                items.Clear();
                foreach (var item in wanted)
                {
                    items.Add(item);
                }
            }
            finally
            {
                _changingPivotItems = false;
            }
        }

        private PivotItem PivotItemAt(int index)
        {
            return index >= 0 && index < _signedInPivotItems.Length ? _signedInPivotItems[index] : ForYouPivotItem;
        }

        private void ResetFeeds()
        {
            foreach (var feed in _timelines)
            {
                feed.Version++;
                feed.Requested = false;
                feed.Items.ReplaceAll(new SocialNoteItem[0], false);
            }
            _notificationsVersion++;
            _mentionsVersion++;
            _notificationsRequested = false;
            _notificationsStale = false;
            _mentionsRequested = false;
            _unseenLiveNotifications = false;
            _notifications.ReplaceAll(new SocialFeedItem[0], false);
            _mentions.ReplaceAll(new SocialFeedItem[0], false);
        }

        private void RefreshTimes()
        {
            var now = DateTimeOffset.Now;
            foreach (var item in _timelines.SelectMany(feed => feed.Items)) item.RefreshTime(now);
            foreach (var item in _notifications) item.RefreshTime(now);
            foreach (var item in _mentions) item.RefreshTime(now);
        }

        private async void CatchUpNotifications()
        {
            try
            {
                if (IsSignedOut || !_notificationsRequested) return;

                var version = _notificationsVersion;
                var unread = await SocialNotificationService.GetUnreadCountAsync(CancellationToken.None);
                if (version != _notificationsVersion || unread <= 0) return;

                _notificationsStale = true;
                if (!IsSignedOut && SocialPivot.SelectedItem == NotificationsPivotItem && !_showingMentions)
                {
                    LoadNotifications();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: notifications catch-up failed", ex);
            }
        }

        private void SocialPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_changingPivotItems) return;

            foreach (var removed in e.RemovedItems)
            {
                TrimPivot(removed);
            }

            if (!IsSignedOut) LastPivotIndex = SocialPivot.SelectedIndex;
            EnsureSelectedFeedLoaded();

            if (IsViewingNotifications())
            {
                MarkLiveNotificationsSeen();
            }
        }

        private void NotificationsTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_tabsReady) return;

            ShowNotificationsTab(sender == MentionsTab);
        }

        private void ShowNotificationsTab(bool mentions)
        {
            if (mentions == _showingMentions) return;

            if (mentions) _notifications.TrimToFirstPage();
            else _mentions.TrimToFirstPage();

            LastShowedMentions = mentions;
            ApplyNotificationsTab(mentions);

            if (IsSignedOut || SocialPivot.SelectedItem != NotificationsPivotItem) return;

            EnsureSelectedFeedLoaded();
            if (IsViewingNotifications()) MarkLiveNotificationsSeen();
        }

        private void ApplyNotificationsTab(bool mentions)
        {
            _showingMentions = mentions;
            AllNotificationsHost.Visibility = mentions ? Visibility.Collapsed : Visibility.Visible;
            MentionsHost.Visibility = mentions ? Visibility.Visible : Visibility.Collapsed;

            var tab = mentions ? MentionsTab : AllNotificationsTab;
            if (!tab.IsChecked.GetValueOrDefault()) tab.IsChecked = true;
        }

        private void EnsureSelectedFeedLoaded()
        {
            var selected = SocialPivot.SelectedItem;
            var feed = TimelineFor(selected);
            if (feed != null)
            {
                if (!feed.Requested) LoadTimeline(feed);
                return;
            }

            if (selected != NotificationsPivotItem) return;

            if (_showingMentions)
            {
                if (!_mentionsRequested) LoadMentions();
            }
            else if (!_notificationsRequested || _notificationsStale)
            {
                LoadNotifications();
            }
        }

        private void LoadNotifications()
        {
            var ignored = LoadNotificationsAsync(false);
        }

        private async Task<bool> LoadNotificationsAsync(bool keepList)
        {
            var version = ++_notificationsVersion;
            _notificationsRequested = true;
            _notificationsStale = false;
            _unseenLiveNotifications = false;
            if (!keepList) SetNotificationsState(FeedState.Loading);

            try
            {
                var unreadAtOpen = await SocialNotificationService.GetUnreadCountAsync(CancellationToken.None);
                if (version != _notificationsVersion) return true;

                var result = await SocialNotificationService.FetchNotificationsAsync(null, true, CancellationToken.None);
                if (version != _notificationsVersion) return true;

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SetNotificationsState(FeedState.NeedsPermission);
                    return true;
                }

                if (result.Status != SocialApiStatus.Ok)
                {
                    if (keepList) return false;

                    SetNotificationsState(FeedState.Failed);
                    return true;
                }

                var items = new List<SocialFeedItem>();
                foreach (var notification in result.Value)
                {
                    var item = SocialFeedItem.FromNotification(notification, items.Count < unreadAtOpen);
                    if (item != null) items.Add(item);
                }

                _notifications.ReplaceAll(items, result.Value.Count >= AppConstants.SocialFeedPageSize);
                SetNotificationsState(items.Count == 0 ? FeedState.Empty : FeedState.Ready);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: notifications load failed", ex);
                if (version != _notificationsVersion) return true;
                if (keepList) return false;

                SetNotificationsState(FeedState.Failed);
                return true;
            }
        }

        private void LoadMentions()
        {
            var ignored = LoadMentionsAsync(false);
        }

        private async Task<bool> LoadMentionsAsync(bool keepList)
        {
            var version = ++_mentionsVersion;
            _mentionsRequested = true;
            if (!keepList) SetMentionsState(FeedState.Loading);

            try
            {
                var result = await SocialNotificationService.FetchMentionsAsync(null, CancellationToken.None);
                if (version != _mentionsVersion) return true;

                if (result.Status != SocialApiStatus.Ok)
                {
                    var denied = result.Status == SocialApiStatus.PermissionDenied;
                    if (keepList && !denied) return false;

                    SetMentionsState(denied ? FeedState.NeedsPermission : FeedState.Failed);
                    return true;
                }

                var items = FeedItemsFromNotes(result.Value);
                _mentions.ReplaceAll(items, result.Value.Count >= AppConstants.SocialFeedPageSize);
                SetMentionsState(items.Count == 0 ? FeedState.Empty : FeedState.Ready);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: mentions load failed", ex);
                if (version != _mentionsVersion) return true;
                if (keepList) return false;

                SetMentionsState(FeedState.Failed);
                return true;
            }
        }

        private async Task<IReadOnlyList<SocialFeedItem>> LoadMoreNotificationsAsync(string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialNotificationService.FetchNotificationsAsync(untilId, false, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return result.Value.Select(notification => SocialFeedItem.FromNotification(notification, false))
                               .Where(item => item != null)
                               .ToList();
        }

        private async Task<IReadOnlyList<SocialFeedItem>> LoadMoreMentionsAsync(string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialNotificationService.FetchMentionsAsync(untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return FeedItemsFromNotes(result.Value);
        }

        private static List<SocialFeedItem> FeedItemsFromNotes(IEnumerable<SocialNote> notes)
        {
            return notes.Select(SocialFeedItem.FromNote).Where(item => item != null).ToList();
        }

        private void SetNotificationsState(FeedState state)
        {
            _notificationsState = state;
            ApplyFeedState(state, _notificationsRefresh, NotificationsLoadingRing, NotificationsStatePanel,
                           NotificationsStateGlyph, NotificationsStateText, NotificationsStateButton,
                           "SocialNotificationsEmpty", "SocialNotificationsFailed");
        }

        private void SetMentionsState(FeedState state)
        {
            _mentionsState = state;
            ApplyFeedState(state, _mentionsRefresh, MentionsLoadingRing, MentionsStatePanel,
                           MentionsStateGlyph, MentionsStateText, MentionsStateButton,
                           "SocialMentionsEmpty", "SocialMentionsFailed");
        }

        private static void ApplyFeedState(FeedState state, ListRefresh refresh, ProgressRing ring, StackPanel panel,
                                           FontIcon glyph, TextBlock text, Button button,
                                           string emptyKey, string failedKey)
        {
            refresh.Container.Visibility = state == FeedState.Ready ? Visibility.Visible : Visibility.Collapsed;
            if (state != FeedState.Ready) HideRefreshFailed(refresh.FailedBar);
            ring.IsActive = state == FeedState.Loading;
            ring.Visibility = state == FeedState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state != FeedState.Loading && state != FeedState.Ready;
            panel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            if (!showPanel) return;

            switch (state)
            {
                case FeedState.Empty:
                    glyph.Glyph = "";
                    text.Text = LocalizedStrings.Get(emptyKey);
                    button.Visibility = Visibility.Collapsed;
                    break;
                case FeedState.Failed:
                    glyph.Glyph = "";
                    text.Text = LocalizedStrings.Get(failedKey);
                    button.Content = LocalizedStrings.Get("SocialRetryButton");
                    button.Visibility = Visibility.Visible;
                    break;
                case FeedState.NeedsPermission:
                    glyph.Glyph = "";
                    text.Text = LocalizedStrings.Get("SocialNeedsPermission");
                    button.Content = LocalizedStrings.Get("SocialSignInAgainButton");
                    button.Visibility = Visibility.Visible;
                    break;
                case FeedState.Unavailable:
                    glyph.Glyph = "";
                    text.Text = LocalizedStrings.Get("SocialTimelineUnavailable");
                    button.Visibility = Visibility.Collapsed;
                    break;
            }

            AutomationHelper.AnnounceLiveRegion(text);
        }

        private void NotificationsStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_notificationsState == FeedState.NeedsPermission)
            {
                OpenSignIn();
            }
            else
            {
                LoadNotifications();
            }
        }

        private void MentionsStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mentionsState == FeedState.NeedsPermission)
            {
                OpenSignIn();
            }
            else
            {
                LoadMentions();
            }
        }

        private void SocialRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryRequestRefresh(SelectedRefresh())) return;

            var selected = SocialPivot.SelectedItem;
            var feed = TimelineFor(selected);
            if (feed != null)
            {
                LoadTimeline(feed);
            }
            else if (selected == NotificationsPivotItem && !IsSignedOut)
            {
                if (_showingMentions) LoadMentions();
                else LoadNotifications();
            }
        }

        private async void NewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsSignedOut) return;

                await SocialWindows.ShowComposeAsync(this, SocialComposeMode.New, null);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not open the composer", ex);
            }
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSignIn();
        }

        private void OpenSignIn()
        {
            try
            {
                if (Frame != null)
                {
                    Frame.Navigate(typeof(LoginPage));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not open sign-in", ex);
            }
        }

        private async void FeedList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SocialFeedItem;
                if (item == null) return;

                if (item.ThreadNote != null
                    && SocialThreads.Open(this, item.ThreadNote, item.ThreadNote.Id, false, SocialThreadTab.Replies))
                {
                    return;
                }

                if (item.OpensActor && item.HasActorProfile)
                {
                    await SocialWindows.ShowUserAsync(item.Actor);
                    return;
                }

                if (!string.IsNullOrEmpty(item.TargetUrl)) await WebLauncher.LaunchAsync(item.TargetUrl);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not open the item", ex);
            }
        }

        private async void NoteList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SocialNoteItem;
                if (item == null) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                if (MfmInlineBuilder.WasLinkJustInvoked) return;

                if (!SocialThreads.Open(this, item.Note, item.Note.Id, false, SocialThreadTab.Replies))
                {
                    await WebLauncher.LaunchAsync(item.NoteUrl);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not open the note", ex);
            }
        }

        private async void FeedActorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var element = sender as FrameworkElement;
                var item = element == null ? null : element.DataContext as SocialFeedItem;
                if (item == null || !item.HasActorProfile) return;

                await SocialWindows.ShowUserAsync(item.Actor);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not open the profile", ex);
            }
        }

        private async void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        RefreshGate();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialPage: gate refresh failed", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: auth state handler failed", ex);
            }
        }

        private async void OnNotificationArrived(object sender, SocialNotification notification)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        PrependLiveNotification(notification);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialPage: live notification failed", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: notification handler failed", ex);
            }
        }

        private void PrependLiveNotification(SocialNotification notification)
        {
            if (IsSignedOut || !_notificationsRequested) return;
            if (_notificationsState != FeedState.Ready && _notificationsState != FeedState.Empty) return;

            if (_notifications.HasDroppedHead) return;
            if (_notifications.Any(existing => string.Equals(existing.Id, notification.Id, StringComparison.Ordinal))) return;

            var item = SocialFeedItem.FromNotification(notification, true);
            if (item == null) return;

            _notifications.Insert(0, item);
            if (_notificationsState != FeedState.Ready) SetNotificationsState(FeedState.Ready);

            if (IsViewingNotifications())
            {
                var ignored = SocialNotificationService.MarkAllReadAsync();
            }
            else
            {
                _unseenLiveNotifications = true;
            }
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == CoreWindowActivationState.Deactivated) return;

            if (IsViewingNotifications())
            {
                MarkLiveNotificationsSeen();
            }
        }

        private void MarkLiveNotificationsSeen()
        {
            if (!_unseenLiveNotifications) return;
            _unseenLiveNotifications = false;

            var ignored = SocialNotificationService.MarkAllReadAsync();
        }

        private bool IsViewingNotifications()
        {
            if (IsSignedOut || _showingMentions || SocialPivot.SelectedItem != NotificationsPivotItem) return false;

            try
            {
                var window = Window.Current;
                return window != null && window.Visible &&
                       window.CoreWindow.ActivationMode == CoreWindowActivationMode.ActivatedInForeground;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not read the window state", ex);
                return false;
            }
        }
    }
}
