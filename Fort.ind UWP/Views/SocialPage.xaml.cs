using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage : Page
    {
        private enum GateKind
        {
            None,
            LabOff,
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

        private const int NotificationsIndex = 0;

        private static int s_lastPivotIndex = NotificationsIndex;

        private readonly SocialFeedCollection _notifications;

        private readonly SocialFeedCollection _mentions;

        private GateKind _gate = GateKind.None;

        private string _shownUserId;

        private bool _notificationsRequested;

        private bool _mentionsRequested;

        private int _notificationsVersion;

        private int _mentionsVersion;

        private FeedState _notificationsState = FeedState.Loading;

        private FeedState _mentionsState = FeedState.Loading;

        private bool _unseenLiveNotifications;

        private bool _authHandlerAttached;

        private bool _arrivalHandlerAttached;

        private bool _activationHandlerAttached;

        public SocialPage()
        {
            this.InitializeComponent();

            _notifications = new SocialFeedCollection(LoadMoreNotificationsAsync);
            _mentions = new SocialFeedCollection(LoadMoreMentionsAsync);
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

            RefreshGate();
        }

        private void SocialPage_Unloaded(object sender, RoutedEventArgs e)
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

            if (!LabsService.SocialNotificationsEnabled)
            {
                ShowGate(GateKind.LabOff);
            }
            else if (user == null)
            {
                ShowGate(GateKind.SignedOut);
            }
            else
            {
                ShowFeeds();
            }
        }

        private void ShowGate(GateKind gate)
        {
            _gate = gate;
            SocialPivot.Visibility = Visibility.Collapsed;
            GateScrollViewer.Visibility = Visibility.Visible;

            if (gate == GateKind.LabOff)
            {
                GateText.Text = LocalizedStrings.Get("SocialGateLabOff");
                GateButton.Content = LocalizedStrings.Get("SocialGateLabOffButton");
            }
            else
            {
                GateText.Text = LocalizedStrings.Get("SocialGateSignedOut");
                GateButton.Content = LocalizedStrings.Get("SocialGateSignInButton");
            }
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
            _notificationsVersion++;
            _mentionsVersion++;
            _notificationsRequested = false;
            _mentionsRequested = false;
            _unseenLiveNotifications = false;
            _notifications.ReplaceAll(new SocialFeedItem[0], false);
            _mentions.ReplaceAll(new SocialFeedItem[0], false);
        }

        private void SocialPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_gate != GateKind.None) return;

            s_lastPivotIndex = SocialPivot.SelectedIndex;
            EnsureSelectedFeedLoaded();

            if (IsViewingNotifications())
            {
                MarkLiveNotificationsSeen();
            }
        }

        private void EnsureSelectedFeedLoaded()
        {
            if (SocialPivot.SelectedItem == NotificationsPivotItem)
            {
                if (!_notificationsRequested) LoadNotifications();
            }
            else if (SocialPivot.SelectedItem == MentionsPivotItem)
            {
                if (!_mentionsRequested) LoadMentions();
            }
        }

        private async void LoadNotifications()
        {
            var version = ++_notificationsVersion;
            _notificationsRequested = true;
            _unseenLiveNotifications = false;
            SetNotificationsState(FeedState.Loading);

            try
            {
                var unreadAtOpen = SocialNotificationService.UnreadCount;
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

            if (SocialPivot.SelectedItem == MentionsPivotItem)
            {
                LoadMentions();
            }
            else
            {
                LoadNotifications();
            }
        }

        private async void GateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_gate == GateKind.SignedOut)
                {
                    OpenSignIn();
                    return;
                }

                if (WindowManagerService.IsSecondaryView)
                {
                    await WindowManagerService.ShowInMainWindowAsync(AppConstants.NavigationBetas);
                    return;
                }

                var shell = MainPage.Current;
                if (shell != null)
                {
                    shell.NavigateToTag(AppConstants.NavigationBetas);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: gate action failed - {ex.Message}");
            }
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
                if (item == null || string.IsNullOrEmpty(item.TargetUrl)) return;

                await WebLauncher.LaunchAsync(item.TargetUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPage: could not open the item - {ex.Message}");
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
