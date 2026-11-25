using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatPage : Page, IReleasablePage, ITrimmablePage
    {
        private const int SuggestionLimit = 8;

        private const double TwoPaneMinimumWidth = 760;

        private static readonly TimeSpan TimeRefreshInterval = TimeSpan.FromMinutes(1);

        private readonly ObservableCollection<SocialChatConversation> _people = new ObservableCollection<SocialChatConversation>();

        private readonly ObservableCollection<SocialChatConversation> _rooms = new ObservableCollection<SocialChatConversation>();

        private readonly Debouncer _searchDebounce = new Debouncer();

        private readonly DispatcherTimer _timeTimer = new DispatcherTimer();

        private CancellationTokenSource _loadCts;

        private bool _handlersAttached;

        private bool _twoPanes = true;

        private string _account;

        private SocialChatArgs _selected;

        private bool _loaded;

        public SocialChatPage()
        {
            this.InitializeComponent();

            ChatView.PersonResolved += ChatView_PersonResolved;
            ChatView.Left += ChatView_Left;
            _timeTimer.Interval = TimeRefreshInterval;
            _timeTimer.Tick += TimeTimer_Tick;

            Loaded += SocialChatPage_Loaded;
            Unloaded += SocialChatPage_Unloaded;

            InitializeTabs();
        }

        public void OpenConversation(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            EnsureAccount();
            var row = Find(key);
            OpenConversation(row != null ? row.Args : SocialChatArgs.ForKey(_account, key));
        }

        private void EnsureAccount()
        {
            if (_loaded) return;

            _twoPanes = IsTwoPane();
            if (_account == null) _account = SocialContentService.CurrentAccountId();
        }

        private void OpenConversation(SocialChatArgs args)
        {
            try
            {
                EnsureAccount();
                if (args == null || string.IsNullOrEmpty(args.Key) || _account == null) return;

                _selected = args;
                SelectTab(args.IsRoom);

                var row = Find(args.Key);
                if (_twoPanes)
                {
                    ChatView.Open(args);
                    ShowPicker(false);
                    ConversationList.SelectedItem = row;
                    ChatView.FocusComposer();
                    return;
                }

                ConversationList.SelectedItem = null;
                if (Frame != null) Frame.Navigate(typeof(SocialChatConversationPage), args);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not open the conversation", ex);
            }
        }

        public void Release()
        {
            Detach();
            CancelLoad();
            _searchDebounce.Cancel();
            ChatView.Release();
        }

        public void Trim()
        {
            ChatView.Trim();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var args = e.Parameter as SocialChatArgs;
            if (args != null && e.NavigationMode == NavigationMode.New) _selected = args;
        }

        private void SocialChatPage_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;

            try
            {
                Attach();
                _twoPanes = IsTwoPane();
                PaintTabs();
                UpdateInvitations();
                Refresh(true);
                _timeTimer.Start();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not load", ex);
            }
        }

        private void SocialChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;

            try
            {
                Detach();
                _timeTimer.Stop();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not unload", ex);
            }
        }

        private void Attach()
        {
            if (_handlersAttached) return;

            ProfileService.AuthStateChanged += OnAuthStateChanged;
            SocialPermissions.Changed += OnPermissionsChanged;
            SocialChatService.Changed += OnChatChanged;
            SocialChatService.UnreadChanged += OnUnreadChanged;
            _handlersAttached = true;
        }

        private void Detach()
        {
            if (!_handlersAttached) return;

            ProfileService.AuthStateChanged -= OnAuthStateChanged;
            SocialPermissions.Changed -= OnPermissionsChanged;
            SocialChatService.Changed -= OnChatChanged;
            SocialChatService.UnreadChanged -= OnUnreadChanged;
            _handlersAttached = false;
        }

        private bool IsTwoPane()
        {
            var state = PaneStates.CurrentState;
            return state == null ? Window.Current.Bounds.Width >= TwoPaneMinimumWidth : ReferenceEquals(state, TwoPaneState);
        }

        private void PaneStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        {
            try
            {
                var twoPanes = ReferenceEquals(e.NewState, TwoPaneState);
                if (twoPanes == _twoPanes) return;

                _twoPanes = twoPanes;
                ChatView.SetPresented(_twoPanes);

                if (_twoPanes && _selected != null && _account != null)
                {
                    ChatView.Open(_selected);
                    ShowPicker(false);
                    ConversationList.SelectedItem = Find(_selected.Key);
                }
                else if (!_twoPanes)
                {
                    ConversationList.SelectedItem = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not switch the pane layout", ex);
            }
        }

        private void Refresh(bool reload)
        {
            var account = SocialContentService.CurrentAccountId();
            var signedIn = account != null;
            var permitted = signedIn && SocialPermissions.Has(SocialPermissions.ReadChat);
            var available = permitted && SocialPolicies.Current.AllowsChatReading;

            Show(SignedOutInfoBar, !signedIn);
            Show(SignInAgainInfoBar, signedIn && !permitted);
            Show(UnavailableInfoBar, permitted && !available);

            ContentGrid.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            NewRoomLink.Visibility = SocialPolicies.Current.AllowsChatWriting ? Visibility.Visible : Visibility.Collapsed;
            ChatView.SetPresented(_twoPanes);

            if (!string.Equals(account, _account, StringComparison.Ordinal))
            {
                _account = account;
                _people.Clear();
                _rooms.Clear();
                _selected = null;
                ChatView.Close();
                reload = true;
            }

            if (!available)
            {
                ChatView.Close();
                return;
            }

            if (_selected == null)
            {
                ShowPicker(true);
            }
            else if (_twoPanes && !string.Equals(ChatView.ConversationKey, _selected.Key, StringComparison.Ordinal))
            {
                ChatView.Open(_selected);
                ShowPicker(false);
            }

            if (reload || (_people.Count == 0 && _rooms.Count == 0)) LoadInBackground();
        }

        private static void Show(Microsoft.UI.Xaml.Controls.InfoBar bar, bool shown)
        {
            bar.IsOpen = shown;
            bar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowPicker(bool shown)
        {
            PickConversationText.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (shown && ChatView.HasConversation) ChatView.Close();
        }

        private void CancelLoad()
        {
            var cts = _loadCts;
            _loadCts = null;
            if (cts == null) return;

            cts.Cancel();
            cts.Dispose();
        }

        private async void LoadInBackground()
        {
            CancellationTokenSource cts = null;
            try
            {
                CancelLoad();
                cts = new CancellationTokenSource();
                _loadCts = cts;
                var account = _account;

                var empty = CurrentList.Count == 0;
                ListLoading.IsActive = empty;
                ListLoading.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                ShowListState(null, false);

                var people = await SocialChatService.FetchHistoryAsync(false, cts.Token);
                if (cts.IsCancellationRequested || !string.Equals(account, _account, StringComparison.Ordinal)) return;

                if (people.Status == SocialApiStatus.PermissionDenied)
                {
                    Refresh(false);
                    return;
                }

                if (people.Status == SocialApiStatus.Ok) ApplyHistory(people.Value);

                var rooms = await SocialChatService.FetchRoomsAsync(cts.Token);
                var roomHistory = await SocialChatService.FetchHistoryAsync(true, cts.Token);
                if (cts.IsCancellationRequested || !string.Equals(account, _account, StringComparison.Ordinal)) return;

                if (rooms.Status == SocialApiStatus.Ok)
                {
                    ApplyRooms(rooms.Value, roomHistory.Status == SocialApiStatus.Ok ? roomHistory.Value : new SocialChatMessage[0]);
                }

                await SocialChatService.FetchInvitationsAsync(cts.Token);
                if (cts.IsCancellationRequested) return;

                UpdateInvitations();
                OpenPendingInvitations();

                var failed = ShowingRooms ? rooms.Status : people.Status;
                if (failed != SocialApiStatus.Ok && CurrentList.Count == 0)
                {
                    ShowListState(LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(failed) ?? "SocialChatLoadFailed"), true);
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatPage: the history load was cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not load the conversations", ex);
                if (CurrentList.Count == 0) ShowListState(LocalizedStrings.Get("SocialChatLoadFailed"), true);
            }
            finally
            {
                if (cts != null && ReferenceEquals(_loadCts, cts))
                {
                    ListLoading.IsActive = false;
                    ListLoading.Visibility = Visibility.Collapsed;
                    UpdateListState();
                }
            }
        }

        private void ApplyHistory(IReadOnlyList<SocialChatMessage> history)
        {
            var rows = history.Where(message => !message.IsRoomMessage)
                              .OrderByDescending(message => message.CreatedAt)
                              .Select(message => new
                              {
                                  Other = message.OtherUserId(_account),
                                  Message = message
                              })
                              .Where(entry => !string.IsNullOrEmpty(entry.Other))
                              .GroupBy(entry => entry.Other, StringComparer.Ordinal)
                              .Select(group => group.First())
                              .Select(entry => new SocialChatConversation(_account, entry.Other, entry.Message.OtherUser(_account),
                                                                          entry.Message, SocialChatService.IsUnread(entry.Other)))
                              .ToList();

            Replace(_people, rows);
        }

        private void Replace(ObservableCollection<SocialChatConversation> list, IEnumerable<SocialChatConversation> rows)
        {
            list.Clear();
            foreach (var row in rows)
            {
                list.Add(row);
            }

            if (_selected != null && _twoPanes) ConversationList.SelectedItem = Find(_selected.Key);
            UpdateListState();
        }

        private ObservableCollection<SocialChatConversation> ListFor(string key)
        {
            return SocialChatKeys.IsRoom(key) ? _rooms : _people;
        }

        private ObservableCollection<SocialChatConversation> CurrentList
        {
            get { return ShowingRooms ? _rooms : _people; }
        }

        private void UpdateListState()
        {
            if (CurrentList.Count == 0 && !ListLoading.IsActive)
            {
                ShowListState(LocalizedStrings.Get(ShowingRooms ? "SocialChatNoRooms" : "SocialChatNoConversations"), false);
            }
            else if (CurrentList.Count > 0 || !ListRetryButton.IsShown())
            {
                ShowListState(null, false);
            }
        }

        private void ShowListState(string text, bool retry)
        {
            ListStatePanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            ListStateText.Text = text ?? "";
            ListRetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        }

        private SocialChatConversation Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            return ListFor(key).FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal));
        }

        private async void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            try
            {
                await RunOnPageAsync(() => Refresh(false));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: auth state handler failed", ex);
            }
        }

        private async void OnPermissionsChanged(object sender, EventArgs e)
        {
            try
            {
                await RunOnPageAsync(() => Refresh(false));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: permission handler failed", ex);
            }
        }

        private async void OnUnreadChanged(object sender, EventArgs e)
        {
            try
            {
                await RunOnPageAsync(SyncUnread);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: unread handler failed", ex);
            }
        }

        private void SyncUnread()
        {
            foreach (var row in _people.Concat(_rooms))
            {
                row.IsUnread = SocialChatService.IsUnread(row.Key);
            }

            PaintTabs();
        }

        private async void OnChatChanged(object sender, SocialChatChange change)
        {
            try
            {
                await RunOnPageAsync(() => Apply(change));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: chat change handler failed", ex);
            }
        }

        private async Task RunOnPageAsync(Action action)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_loaded) action();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialChatPage: could not apply a change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: a change handler failed", ex);
            }
        }

        private void Apply(SocialChatChange change)
        {
            if (change == null || _account == null) return;

            switch (change.Kind)
            {
                case SocialChatChangeKind.Reset:
                    Refresh(true);
                    break;
                case SocialChatChangeKind.Message:
                    ApplyMessage(change.Key, change.Message);
                    break;
                case SocialChatChangeKind.Read:
                    var read = Find(change.Key);
                    if (read != null) read.IsUnread = false;
                    break;
                case SocialChatChangeKind.Deleted:
                    var row = Find(change.Key);
                    if (row != null && row.LastMessage != null && string.Equals(row.LastMessage.Id, change.MessageId, StringComparison.Ordinal))
                    {
                        LoadInBackground();
                    }
                    break;
                case SocialChatChangeKind.Rooms:
                    ApplyRoom(change.Key, change.Room);
                    break;
                case SocialChatChangeKind.Invitations:
                    UpdateInvitations();
                    PaintTabs();
                    break;
            }
        }

        private void ApplyMessage(string key, SocialChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(key)) return;

            var unread = SocialChatService.IsUnread(key);
            var list = ListFor(key);
            var row = Find(key);
            if (row == null)
            {
                if (SocialChatKeys.IsRoom(key))
                {
                    row = SocialChatConversation.ForRoom(_account, message.ToRoom ?? SocialChatService.RoomFor(message.ToRoomId), message, unread);
                    if (row == null) return;
                }
                else
                {
                    row = new SocialChatConversation(_account, key, message.OtherUser(_account), message, unread);
                    if (row.Other == null) ResolvePersonInBackground(row);
                }

                list.Insert(0, row);
                UpdateListState();
                return;
            }

            if (!row.Apply(message, unread)) return;

            MoveToTop(list, row);
        }

        private void MoveToTop(ObservableCollection<SocialChatConversation> list, SocialChatConversation row)
        {
            var index = list.IndexOf(row);
            if (index <= 0) return;

            var wasSelected = ReferenceEquals(ConversationList.SelectedItem, row);
            list.Move(index, 0);
            if (wasSelected) ConversationList.SelectedItem = row;
        }

        private async void ResolvePersonInBackground(SocialChatConversation row)
        {
            try
            {
                var result = await SocialContentService.FetchUserAsync(row.Key, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok) row.SetOther(result.Value.User);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not resolve the person", ex);
            }
        }

        private void ChatView_PersonResolved(object sender, SocialUser user)
        {
            if (user == null) return;

            if (_selected != null && !_selected.IsRoom && _selected.User == null
                && string.Equals(user.Id, _selected.UserId, StringComparison.Ordinal))
            {
                _selected = new SocialChatArgs(_selected.Account, user.Id, user);
            }

            var row = Find(user.Id);
            if (row != null) row.SetOther(user);
        }

        private void ChatView_Left(object sender, string key)
        {
            try
            {
                if (_selected == null || !string.Equals(_selected.Key, key, StringComparison.Ordinal)) return;

                _selected = null;
                ConversationList.SelectedItem = null;
                ShowPicker(true);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not leave the conversation", ex);
            }
        }

        private void TimeTimer_Tick(object sender, object e)
        {
            foreach (var row in _people.Concat(_rooms))
            {
                row.RefreshTime();
            }
        }

        private void ConversationList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var row = e.ClickedItem as SocialChatConversation;
            if (row != null) OpenConversation(row.Args);
        }

        private void ConversationList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            try
            {
                if (args.Handled) return;

                var target = args.OriginalSource as FrameworkElement;
                var row = target == null ? null : RowFor(target);
                if (row == null) return;

                MenuFlyout flyout;
                if (row.IsRoom)
                {
                    flyout = new MenuFlyout();
                }
                else
                {
                    if (row.Other == null) return;
                    flyout = SocialMenus.BuildForUser(row.Other, null, this) ?? new MenuFlyout();
                }

                SocialMenus.AddChatWindowItem(flyout, row.Args);

                Point point;
                SocialMenus.ShowContextAt(flyout, target, args.TryGetPosition(target, out point) ? point : (Point?)null);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not open the conversation menu", ex);
            }
        }

        private static SocialChatConversation RowFor(DependencyObject element)
        {
            while (element != null)
            {
                var framework = element as FrameworkElement;
                var row = framework == null ? null : framework.DataContext as SocialChatConversation;
                if (row != null) return row;

                var container = element as ListViewItem;
                if (container != null) return container.Content as SocialChatConversation;

                element = Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private void ListRetryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadInBackground();
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WindowManagerService.ShowSignInInMainWindowAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not open sign-in", ex);
            }
        }

        private void NewChatFlyout_Opened(object sender, object e)
        {
            PersonSearch.Text = "";
            PersonSearch.ItemsSource = null;
            PersonSearchStatus.Visibility = Visibility.Collapsed;
            PersonSearch.Focus(FocusState.Programmatic);
        }

        private async void PersonSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

                var token = _searchDebounce.Restart();
                if (string.IsNullOrWhiteSpace(sender.Text))
                {
                    sender.ItemsSource = null;
                    PersonSearchStatus.Visibility = Visibility.Collapsed;
                    return;
                }

                if (!await Debouncer.WaitAsync(AppConstants.SocialSuggestDelayMilliseconds, token)) return;

                var result = await SocialChatService.SearchPeopleAsync(sender.Text, SuggestionLimit, CancellationToken.None);
                if (token.IsCancellationRequested) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    sender.ItemsSource = null;
                    PersonSearchStatus.Text = LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialChatSearchFailed");
                    PersonSearchStatus.Visibility = Visibility.Visible;
                    return;
                }

                var people = result.Value;
                sender.ItemsSource = people;
                PersonSearchStatus.Text = people.Count == 0 ? LocalizedStrings.Get("SocialChatSearchNoOne") : "";
                PersonSearchStatus.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatPage: the person search was superseded");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: the person search failed", ex);
            }
        }

        private void PersonSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                var user = args.ChosenSuggestion as SocialUser;
                if (user == null)
                {
                    var people = sender.ItemsSource as IReadOnlyList<SocialUser>;
                    user = people == null ? null : people.FirstOrDefault();
                }

                if (user == null) return;

                NewChatFlyout.Hide();
                OpenConversation(new SocialChatArgs(_account, user.Id, user));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not start the chat", ex);
            }
        }
    }
}
