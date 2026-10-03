using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private enum GateKind
        {
            None,
            SignedOut
        }

        private enum FeedState
        {
            Loading,
            Ready,
            Empty,
            Failed,
            NeedsPermission
        }

        private const int ForYouIndex = 0;

        private const int NotificationsIndex = 1;

        private static int s_lastPivotIndex = ForYouIndex;

        private readonly SocialFeedCollection<SocialNoteItem> _forYou;

        private readonly SocialFeedCollection<SocialFeedItem> _notifications;

        private readonly SocialFeedCollection<SocialFeedItem> _mentions;

        private GateKind _gate = GateKind.None;

        private string _shownUserId;

        private bool _forYouRequested;

        private bool _notificationsRequested;

        private bool _notificationsStale;

        private bool _mentionsRequested;

        private int _forYouVersion;

        private int _notificationsVersion;

        private int _mentionsVersion;

        private FeedState _forYouState = FeedState.Loading;

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

            _forYou = new SocialFeedCollection<SocialNoteItem>(LoadMoreForYouAsync, SocialFeedPaging.NonEmpty);
            _notifications = new SocialFeedCollection<SocialFeedItem>(LoadMoreNotificationsAsync, SocialFeedPaging.FullPage);
            _mentions = new SocialFeedCollection<SocialFeedItem>(LoadMoreMentionsAsync, SocialFeedPaging.FullPage);
            ForYouList.ItemsSource = _forYou;
            NotificationsList.ItemsSource = _notifications;
            MentionsList.ItemsSource = _mentions;

            Loaded += SocialPage_Loaded;
            Unloaded += SocialPage_Unloaded;
        }

        internal void ShowNotifications()
        {
            s_lastPivotIndex = NotificationsIndex;
            if (_gate == GateKind.None && SocialPivot.SelectedIndex != NotificationsIndex)
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
            RemoveForYouRows(item => item.IsGone);
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

            _keepListsOnUnload = e.SourcePageType == typeof(SocialNotePage);
        }

        private void SocialPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Release();
            if (!_keepListsOnUnload) Trim();
        }

        public void Trim()
        {
            _forYou.TrimToFirstPage();
            _notifications.TrimToFirstPage();
            _mentions.TrimToFirstPage();
        }

        private void TrimPivot(object pivotItem)
        {
            if (pivotItem == ForYouPivotItem) _forYou.TrimToFirstPage();
            else if (pivotItem == NotificationsPivotItem) _notifications.TrimToFirstPage();
            else if (pivotItem == MentionsPivotItem) _mentions.TrimToFirstPage();
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
                    Debug.WriteLine($"SocialPage: could not remove the activation handler - {ex.Message}");
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
                    && change.Kind != SocialNoteChangeKind.Posted) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (change.Kind == SocialNoteChangeKind.Posted)
                        {
                            InsertPosted(change.Note);
                            _seenPostsVersion = SocialNoteService.PostsVersion;
                        }
                        else
                        {
                            RemoveForYouRows(item => SocialNoteItem.IsRemovedBy(item, change));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialPage: could not remove a note - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: note change handler failed - {ex.Message}");
            }
        }

        private void InsertPosted(SocialNote note)
        {
            if (note == null || !string.IsNullOrEmpty(note.ReplyId) || !_forYouRequested) return;
            if (_forYouState != FeedState.Ready && _forYouState != FeedState.Empty) return;
            if (!SocialContentService.IsCurrentAccount(note.UserId)) return;

            foreach (var existing in _forYou)
            {
                if (string.Equals(existing.Note.Id, note.Id, StringComparison.Ordinal)) return;
            }

            var item = SocialNoteItem.Create(note, SocialContentService.CachedEmojiMap, false);
            if (item == null) return;

            _forYou.Insert(0, item);
            if (_forYouState != FeedState.Ready) SetForYouState(FeedState.Ready);
        }

        private void RemoveForYouRows(Func<SocialNoteItem, bool> predicate)
        {
            if (_forYou.RemoveWhere(predicate) == 0) return;

            if (_forYou.Count == 0 && _forYouState == FeedState.Ready && !_forYou.HasMoreItems) SetForYouState(FeedState.Empty);
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

            if (user == null)
            {
                ShowSignedOutGate();
            }
            else
            {
                ShowFeeds();
            }
        }

        private void ShowSignedOutGate()
        {
            _gate = GateKind.SignedOut;
            SocialPivot.Visibility = Visibility.Collapsed;
            GateScrollViewer.Visibility = Visibility.Visible;

            GateText.Text = LocalizedStrings.Get("SocialGateSignedOut");
            GateButton.Content = LocalizedStrings.Get("SocialGateSignInButton");
        }

        private void ShowFeeds()
        {
            var wasGated = _gate != GateKind.None || SocialPivot.Visibility != Visibility.Visible;
            _gate = GateKind.None;
            GateScrollViewer.Visibility = Visibility.Collapsed;
            SocialPivot.Visibility = Visibility.Visible;

            if (wasGated && SocialPivot.SelectedIndex != s_lastPivotIndex)
            {
                SocialPivot.SelectedIndex = s_lastPivotIndex;
                return;
            }

            EnsureSelectedFeedLoaded();
        }

        private void ResetFeeds()
        {
            _forYouVersion++;
            _notificationsVersion++;
            _mentionsVersion++;
            _forYouRequested = false;
            _notificationsRequested = false;
            _notificationsStale = false;
            _mentionsRequested = false;
            _unseenLiveNotifications = false;
            _forYou.ReplaceAll(new SocialNoteItem[0], false);
            _notifications.ReplaceAll(new SocialFeedItem[0], false);
            _mentions.ReplaceAll(new SocialFeedItem[0], false);
        }

        private void RefreshTimes()
        {
            var now = DateTimeOffset.Now;
            foreach (var item in _forYou) item.RefreshTime(now);
            foreach (var item in _notifications) item.RefreshTime(now);
            foreach (var item in _mentions) item.RefreshTime(now);
        }

        private async void CatchUpNotifications()
        {
            try
            {
                if (_gate != GateKind.None || !_notificationsRequested) return;

                var version = _notificationsVersion;
                var unread = await SocialNotificationService.GetUnreadCountAsync(CancellationToken.None);
                if (version != _notificationsVersion || unread <= 0) return;

                _notificationsStale = true;
                if (_gate == GateKind.None && SocialPivot.SelectedItem == NotificationsPivotItem)
                {
                    LoadNotifications();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: notifications catch-up failed - {ex.Message}");
            }
        }

        private void SocialPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_gate != GateKind.None) return;

            foreach (var removed in e.RemovedItems)
            {
                TrimPivot(removed);
            }

            s_lastPivotIndex = SocialPivot.SelectedIndex;
            EnsureSelectedFeedLoaded();

            if (IsViewingNotifications())
            {
                MarkLiveNotificationsSeen();
            }
        }

        private void EnsureSelectedFeedLoaded()
        {
            if (SocialPivot.SelectedItem == ForYouPivotItem)
            {
                if (!_forYouRequested) LoadForYou();
            }
            else if (SocialPivot.SelectedItem == NotificationsPivotItem)
            {
                if (!_notificationsRequested || _notificationsStale) LoadNotifications();
            }
            else if (SocialPivot.SelectedItem == MentionsPivotItem)
            {
                if (!_mentionsRequested) LoadMentions();
            }
        }

        private async void LoadForYou()
        {
            var version = ++_forYouVersion;
            _forYouRequested = true;
            SetForYouState(FeedState.Loading);

            try
            {
                var emojiTask = SocialContentService.GetEmojiMapAsync();
                var result = await SocialContentService.FetchTimelineAsync(null, CancellationToken.None);
                var emojis = await emojiTask;
                if (version != _forYouVersion) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetForYouState(result.Status == SocialApiStatus.PermissionDenied ? FeedState.NeedsPermission : FeedState.Failed);
                    return;
                }

                var items = SocialNoteItem.CreateAll(result.Value, emojis, false);
                _forYou.ReplaceAll(items, result.Value.Count > 0);
                SetForYouState(items.Count == 0 ? FeedState.Empty : FeedState.Ready);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: timeline load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == _forYouVersion) SetForYouState(FeedState.Failed);
            }
        }

        private async void LoadNotifications()
        {
            var version = ++_notificationsVersion;
            _notificationsRequested = true;
            _notificationsStale = false;
            _unseenLiveNotifications = false;
            SetNotificationsState(FeedState.Loading);

            try
            {
                var unreadAtOpen = await SocialNotificationService.GetUnreadCountAsync(CancellationToken.None);
                if (version != _notificationsVersion) return;

                var result = await SocialNotificationService.FetchNotificationsAsync(null, true, CancellationToken.None);
                if (version != _notificationsVersion) return;

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SetNotificationsState(FeedState.NeedsPermission);
                    return;
                }

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetNotificationsState(FeedState.Failed);
                    return;
                }

                var items = new List<SocialFeedItem>();
                foreach (var notification in result.Value)
                {
                    var item = SocialFeedItem.FromNotification(notification, items.Count < unreadAtOpen);
                    if (item != null) items.Add(item);
                }

                _notifications.ReplaceAll(items, result.Value.Count >= AppConstants.SocialFeedPageSize);
                SetNotificationsState(items.Count == 0 ? FeedState.Empty : FeedState.Ready);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: notifications load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == _notificationsVersion) SetNotificationsState(FeedState.Failed);
            }
        }

        private async void LoadMentions()
        {
            var version = ++_mentionsVersion;
            _mentionsRequested = true;
            SetMentionsState(FeedState.Loading);

            try
            {
                var result = await SocialNotificationService.FetchMentionsAsync(null, CancellationToken.None);
                if (version != _mentionsVersion) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetMentionsState(result.Status == SocialApiStatus.PermissionDenied ? FeedState.NeedsPermission : FeedState.Failed);
                    return;
                }

                var items = new List<SocialFeedItem>();
                foreach (var note in result.Value)
                {
                    var item = SocialFeedItem.FromNote(note);
                    if (item != null) items.Add(item);
                }

                _mentions.ReplaceAll(items, result.Value.Count >= AppConstants.SocialFeedPageSize);
                SetMentionsState(items.Count == 0 ? FeedState.Empty : FeedState.Ready);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: mentions load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == _mentionsVersion) SetMentionsState(FeedState.Failed);
            }
        }

        private async Task<IReadOnlyList<SocialNoteItem>> LoadMoreForYouAsync(string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialContentService.FetchTimelineAsync(untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return SocialNoteItem.CreateAll(result.Value, SocialContentService.CachedEmojiMap, false);
        }

        private async Task<IReadOnlyList<SocialFeedItem>> LoadMoreNotificationsAsync(string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialNotificationService.FetchNotificationsAsync(untilId, false, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            var items = new List<SocialFeedItem>();
            foreach (var notification in result.Value)
            {
                var item = SocialFeedItem.FromNotification(notification, false);
                if (item != null) items.Add(item);
            }
            return items;
        }

        private async Task<IReadOnlyList<SocialFeedItem>> LoadMoreMentionsAsync(string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialNotificationService.FetchMentionsAsync(untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            var items = new List<SocialFeedItem>();
            foreach (var note in result.Value)
            {
                var item = SocialFeedItem.FromNote(note);
                if (item != null) items.Add(item);
            }
            return items;
        }

        private void SetForYouState(FeedState state)
        {
            _forYouState = state;
            ApplyFeedState(state, ForYouList, ForYouLoadingRing, ForYouStatePanel,
                           ForYouStateGlyph, ForYouStateText, ForYouStateButton,
                           "SocialForYouEmpty", "SocialForYouFailed");
        }

        private void SetNotificationsState(FeedState state)
        {
            _notificationsState = state;
            ApplyFeedState(state, NotificationsList, NotificationsLoadingRing, NotificationsStatePanel,
                           NotificationsStateGlyph, NotificationsStateText, NotificationsStateButton,
                           "SocialNotificationsEmpty", "SocialNotificationsFailed");
        }

        private void SetMentionsState(FeedState state)
        {
            _mentionsState = state;
            ApplyFeedState(state, MentionsList, MentionsLoadingRing, MentionsStatePanel,
                           MentionsStateGlyph, MentionsStateText, MentionsStateButton,
                           "SocialMentionsEmpty", "SocialMentionsFailed");
        }

        private static void ApplyFeedState(FeedState state, ListView list, ProgressRing ring, StackPanel panel,
                                           FontIcon glyph, TextBlock text, Button button,
                                           string emptyKey, string failedKey)
        {
            list.Visibility = state == FeedState.Ready ? Visibility.Visible : Visibility.Collapsed;
            ring.IsActive = state == FeedState.Loading;
            ring.Visibility = state == FeedState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state == FeedState.Empty || state == FeedState.Failed || state == FeedState.NeedsPermission;
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
            }

            AutomationHelper.AnnounceLiveRegion(text);
        }

        private void ForYouStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_forYouState == FeedState.NeedsPermission)
            {
                OpenSignIn();
            }
            else
            {
                LoadForYou();
            }
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
            if (_gate != GateKind.None) return;

            if (SocialPivot.SelectedItem == ForYouPivotItem)
            {
                LoadForYou();
            }
            else if (SocialPivot.SelectedItem == MentionsPivotItem)
            {
                LoadMentions();
            }
            else
            {
                LoadNotifications();
            }
        }

        private async void NewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SocialWindows.ShowComposeAsync(this, SocialComposeMode.New, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: could not open the composer - {ex.Message}");
            }
        }

        private void GateButton_Click(object sender, RoutedEventArgs e)
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
                Debug.WriteLine($"SocialPage: could not open sign-in - {ex.GetType().Name}: {ex.Message}");
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
                Debug.WriteLine($"SocialPage: could not open the item - {ex.Message}");
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
                Debug.WriteLine($"SocialPage: could not open the note - {ex.Message}");
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
                Debug.WriteLine($"SocialPage: could not open the profile - {ex.Message}");
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
                        Debug.WriteLine($"SocialPage: gate refresh failed - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: auth state handler failed - {ex.Message}");
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
                        Debug.WriteLine($"SocialPage: live notification failed - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: notification handler failed - {ex.Message}");
            }
        }

        private void PrependLiveNotification(SocialNotification notification)
        {
            if (_gate != GateKind.None || !_notificationsRequested) return;
            if (_notificationsState != FeedState.Ready && _notificationsState != FeedState.Empty) return;

            foreach (var existing in _notifications)
            {
                if (string.Equals(existing.Id, notification.Id, StringComparison.Ordinal)) return;
            }

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
            if (_gate != GateKind.None || SocialPivot.SelectedItem != NotificationsPivotItem) return false;

            try
            {
                var window = Window.Current;
                return window != null && window.Visible &&
                       window.CoreWindow.ActivationMode == CoreWindowActivationMode.ActivatedInForeground;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: could not read the window state - {ex.Message}");
                return false;
            }
        }
    }
}
