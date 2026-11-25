using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatView : UserControl
    {
        private const double OlderLoadThreshold = 120;

        private const double InlinePanelMinWidth = 680;

        private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(AppConstants.SocialChatGroupMinutes);

        private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(700);

        [ThreadStatic]
        private static SocialChatView t_overlayOwner;

        private readonly ObservableCollection<SocialChatMessageItem> _items = new ObservableCollection<SocialChatMessageItem>();

        private readonly Debouncer _readDebounce = new Debouncer();

        private CancellationTokenSource _loadCts;

        private SocialChatWatch _watch;

        private ScrollViewer _scroller;

        private Flyout _reactionFlyout;

        private string _account;

        private string _key;

        private string _otherId;

        private string _roomId;

        private SocialUser _other;

        private SocialChatRoom _room;

        private SocialUserDetail _detail;

        private IReadOnlyDictionary<string, Uri> _emojis;

        private bool _isLoaded;

        private bool _handlersAttached;

        private bool _windowActive = true;

        private bool _presented = true;

        private bool _loadingOlder;

        private bool _reachedOldest;

        private bool _olderUnavailable;

        private bool _theyMessaged;

        private int _memberCount;

        private int _viewId = -1;

        public SocialChatView()
        {
            this.InitializeComponent();

            MessageList.ItemsSource = _items;
            Loaded += SocialChatView_Loaded;
            Unloaded += SocialChatView_Unloaded;
            RoomPanel.CloseRequested += RoomPanel_CloseRequested;
            RoomPanel.MembersCounted += RoomPanel_MembersCounted;
            AttachDropHandlers();

            PaintHeader();
            UpdateComposer();
        }

        public event EventHandler<SocialUser> PersonResolved;

        public event EventHandler<string> TitleChanged;

        public event EventHandler<string> Left;

        public bool CanPopOut { get; set; } = true;

        public string ConversationKey
        {
            get { return _key; }
        }

        public bool HasConversation
        {
            get { return _key != null; }
        }

        private bool IsRoom
        {
            get { return _roomId != null; }
        }

        private static SocialChatView OverlayOwner
        {
            get { return t_overlayOwner; }
            set { t_overlayOwner = value; }
        }

        public static bool CloseOverlayInCurrentWindow()
        {
            var owner = OverlayOwner;
            if (owner == null) return false;

            OverlayOwner = null;
            if (!owner.RoomSplit.IsPaneOpen) return false;

            owner.CloseRoomPanel(true);
            return true;
        }

        public void Open(SocialChatArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.Key)) return;

            if (string.Equals(args.Key, _key, StringComparison.Ordinal)
                && string.Equals(args.Account, _account, StringComparison.Ordinal))
            {
                if (_other == null && args.User != null)
                {
                    _other = args.User;
                    PaintHeader();
                }

                if (_room == null && args.Room != null)
                {
                    _room = args.Room;
                    RoomPanel.Show(_room, _account);
                    PaintHeader();
                }
                return;
            }

            SaveDraft();
            ResetConversation();

            _account = args.Account;
            _key = args.Key;
            _otherId = args.UserId;
            _roomId = args.RoomId;
            _other = args.User;
            _room = args.IsRoom ? args.Room ?? SocialChatService.RoomFor(args.RoomId) : null;
            if (_room != null) RoomPanel.Show(_room, _account);

            PaintHeader();
            HeaderBar.Visibility = Visibility.Visible;
            ComposerBar.Visibility = Visibility.Visible;

            SetMessageText(SocialChatService.DraftFor(_key) ?? "");
            if (_watch != null) _watch.Set(_key);
            UpdateActive();
            UpdateComposer();

            LoadInitialInBackground();
            LoadDetailInBackground();
        }

        public void Close()
        {
            SaveDraft();
            ResetConversation();
            _key = null;
            _otherId = null;
            _roomId = null;
            _other = null;
            _room = null;
            if (_watch != null) _watch.Set(null);
            UpdateActive();
            PaintHeader();
            UpdateComposer();
            HeaderBar.Visibility = Visibility.Collapsed;
            ComposerBar.Visibility = Visibility.Collapsed;
        }

        public void SetPresented(bool presented)
        {
            if (_presented == presented) return;

            _presented = presented;
            UpdateActive();
        }

        public void FocusComposer()
        {
            if (ChatInput.IsEnabled) ChatInput.Focus(FocusState.Programmatic);
        }

        public void Trim()
        {
            var excess = _items.Count - AppConstants.SocialChatPageSize;
            if (excess <= 0) return;

            for (var i = 0; i < excess; i++)
            {
                if (_items[0].State != SocialChatSendState.Sent) break;
                _items.RemoveAt(0);
            }

            _reachedOldest = false;
            _olderUnavailable = false;
            OlderUnavailableText.Visibility = Visibility.Collapsed;
            Relayout();
        }

        public void Release()
        {
            SaveDraft();
            Detach();
            CancelLoad();
            _readDebounce.Cancel();
            ClearAttachment(true);
            CloseRoomPanel(false);
        }

        private void ResetConversation()
        {
            CancelLoad();
            _readDebounce.Cancel();
            ClearAttachment(true);
            CloseRoomPanel(false);
            RoomPanel.Reset();
            _items.Clear();
            _detail = null;
            _memberCount = 0;
            _theyMessaged = false;
            _reachedOldest = false;
            _olderUnavailable = false;
            OlderUnavailableText.Visibility = Visibility.Collapsed;
            _loadingOlder = false;
            ShowState(null, false);
            ShowLoading(false);
        }

        private void SocialChatView_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            try
            {
                _viewId = ApplicationView.GetForCurrentView().Id;
                Attach();

                if (_watch == null)
                {
                    _watch = SocialChatCapture.Watch();
                    _watch.Set(_key);
                }

                UpdateActive();
                UpdatePanelMode(ActualWidth);
                if (_key != null && _items.Count > 0) CatchUpInBackground(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not attach", ex);
            }
        }

        private void SocialChatView_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;

            try
            {
                SaveDraft();
                Detach();
                if (ReferenceEquals(OverlayOwner, this)) OverlayOwner = null;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not detach", ex);
            }
        }

        private void Attach()
        {
            if (_handlersAttached) return;

            SocialChatService.Changed += OnChatChanged;
            Window.Current.Activated += Window_Activated;
            _handlersAttached = true;
        }

        private void Detach()
        {
            if (_handlersAttached)
            {
                SocialChatService.Changed -= OnChatChanged;
                try
                {
                    Window.Current.Activated -= Window_Activated;
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialChatView: could not remove the activation handler", ex);
                }
                _handlersAttached = false;
            }

            if (_viewId >= 0) SocialChatService.SetActive(_viewId, null);

            if (_watch != null)
            {
                _watch.Dispose();
                _watch = null;
            }
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs e)
        {
            _windowActive = e.WindowActivationState != CoreWindowActivationState.Deactivated;
            UpdateActive();
        }

        private bool IsActiveConversation
        {
            get { return _isLoaded && _presented && _windowActive && _key != null; }
        }

        private void UpdateActive()
        {
            if (_viewId < 0) return;

            var active = IsActiveConversation;
            SocialChatService.SetActive(_viewId, active ? _key : null);
            if (active && SocialChatService.IsUnread(_key)) MarkReadSoon();
        }

        private void CancelLoad()
        {
            var cts = _loadCts;
            _loadCts = null;
            if (cts == null) return;

            cts.Cancel();
            cts.Dispose();
        }

        private CancellationToken BeginLoad()
        {
            CancelLoad();
            _loadCts = new CancellationTokenSource();
            return _loadCts.Token;
        }

        private async Task<IReadOnlyDictionary<string, Uri>> EmojisAsync()
        {
            if (_emojis != null) return _emojis;

            _emojis = SocialContentService.CachedEmojiMap;
            if (_emojis != null) return _emojis;

            try
            {
                _emojis = await SocialContentService.GetEmojiMapAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not load the emoji list", ex);
            }

            return _emojis;
        }

        private bool Still(string key)
        {
            return key != null && string.Equals(key, _key, StringComparison.Ordinal);
        }

        private async void LoadInitialInBackground()
        {
            var key = _key;
            try
            {
                var token = BeginLoad();
                ShowLoading(true);
                ShowState(null, false);

                var emojis = await EmojisAsync();
                var result = await SocialChatService.FetchPageAsync(key, null, token);
                if (token.IsCancellationRequested || !Still(key)) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    ShowLoadFailure(result.Status, result.ErrorCode);
                    return;
                }

                var loaded = result.Value.Messages.OrderBy(message => message.CreatedAt)
                                                  .Select(message => SocialChatMessageItem.Create(message, _account, _other, emojis))
                                                  .ToList();
                foreach (var item in loaded)
                {
                    Track(item);
                    _items.Add(item);
                }

                _reachedOldest = result.Value.ReachedOldest;
                _olderUnavailable = result.Value.OlderUnavailable;
                _theyMessaged = _items.Any(entry => !entry.IsMine);
                Relayout();
                ShowLoading(false);
                UpdateEmptyState();
                UpdateComposer();
                ScrollToBottomSoon();
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatView: the conversation load was cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not load the conversation", ex);
                if (Still(key)) ShowLoadFailure(SocialApiStatus.Failed, null);
            }
            finally
            {
                if (Still(key)) ShowLoading(false);
            }
        }

        private void LoadDetailInBackground()
        {
            if (IsRoom) LoadRoomInBackground();
            else LoadPersonInBackground();
        }

        private async void LoadPersonInBackground()
        {
            var key = _key;
            try
            {
                var result = await SocialContentService.FetchUserAsync(_otherId, CancellationToken.None);
                if (!Still(key) || result.Status != SocialApiStatus.Ok) return;

                _detail = result.Value;
                if (_other == null)
                {
                    _other = _detail.User;
                    foreach (var item in _items.Where(entry => !entry.IsMine && entry.Sender == null))
                    {
                        item.SetSender(_other);
                    }
                }

                PaintHeader();
                UpdateComposer();
                PersonResolved?.Invoke(this, _detail.User);
                TitleChanged?.Invoke(this, MfmText.PlainName(DisplayName));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not load the person", ex);
            }
        }

        private async void LoadRoomInBackground()
        {
            var key = _key;
            try
            {
                var result = await SocialChatService.FetchRoomAsync(_roomId, CancellationToken.None);
                if (!Still(key) || result.Status != SocialApiStatus.Ok) return;

                _room = result.Value;
                RoomPanel.Show(_room, _account);
                PaintHeader();
                UpdateComposer();
                TitleChanged?.Invoke(this, _room.DisplayName);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not load the room", ex);
            }
        }

        private async void LoadOlderInBackground()
        {
            var key = _key;
            var started = false;
            try
            {
                if (_loadingOlder || _reachedOldest || key == null) return;

                var oldest = _items.FirstOrDefault(item => item.Id != null);
                if (oldest == null) return;

                started = true;
                _loadingOlder = true;
                OlderLoading.Visibility = Visibility.Visible;
                OlderLoading.IsActive = true;

                var token = _loadCts == null ? CancellationToken.None : _loadCts.Token;
                var result = await SocialChatService.FetchPageAsync(key, oldest.Id, token);
                if (!Still(key)) return;

                if (result.Status != SocialApiStatus.Ok) return;

                var emojis = await EmojisAsync();
                var older = result.Value.Messages
                                  .Where(message => !_items.Any(item => string.Equals(item.Id, message.Id, StringComparison.Ordinal)))
                                  .OrderByDescending(message => message.CreatedAt)
                                  .Select(message => SocialChatMessageItem.Create(message, _account, _other, emojis))
                                  .ToList();
                foreach (var item in older)
                {
                    Track(item);
                    _items.Insert(0, item);
                }

                _reachedOldest = result.Value.ReachedOldest;
                _olderUnavailable = result.Value.OlderUnavailable;
                if (!_theyMessaged) _theyMessaged = _items.Any(item => !item.IsMine);
                Relayout();
                UpdateComposer();
                UpdateEmptyState();
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatView: loading older messages was cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not load older messages", ex);
            }
            finally
            {
                if (started)
                {
                    _loadingOlder = false;
                    OlderLoading.IsActive = false;
                    OlderLoading.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void MarkReadSoon()
        {
            CatchUpInBackground(true);
        }

        private async void CatchUpInBackground(bool onlyWhileActive)
        {
            var key = _key;
            try
            {
                var token = _readDebounce.Restart();
                if (!await Debouncer.WaitAsync(ReadDelay, token)) return;
                if (!Still(key)) return;
                if (onlyWhileActive && !IsActiveConversation) return;

                var newest = _items.LastOrDefault(item => item.Id != null);
                var result = await SocialChatService.FetchTimelineAsync(key, newest == null ? null : newest.Id, null, CancellationToken.None);
                if (!Still(key) || result.Status != SocialApiStatus.Ok) return;

                foreach (var message in result.Value.OrderBy(message => message.CreatedAt))
                {
                    AddMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatView: marking read was superseded");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not mark the conversation read", ex);
            }
        }

        private async void OnChatChanged(object sender, SocialChatChange change)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        Apply(change);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialChatView: could not apply a chat change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: chat change handler failed", ex);
            }
        }

        private void Apply(SocialChatChange change)
        {
            if (change == null) return;

            if (change.Kind == SocialChatChangeKind.Reset)
            {
                if (!SocialContentService.IsCurrentAccount(_account) || !LabsService.ChatEnabled) Close();
                return;
            }

            if (!Still(change.Key)) return;

            switch (change.Kind)
            {
                case SocialChatChangeKind.Message:
                    var added = AddMessage(change.Message);
                    if (added && !change.Message.IsFrom(_account))
                    {
                        if (!_theyMessaged)
                        {
                            _theyMessaged = true;
                            UpdateComposer();
                        }

                        if (IsActiveConversation)
                        {
                            MarkReadSoon();
                            var sender = change.Message.FromUser ?? _other;
                            AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialChatNewMessageAnnouncementFormat",
                                                                                          sender == null
                                                                                          ? MfmText.PlainName(DisplayName)
                                                                                          : MfmText.PlainName(SocialNoteItem.DisplayNameOf(sender))),
                                                            "SocialChatNewMessage");
                        }
                    }
                    break;
                case SocialChatChangeKind.Deleted:
                    var gone = _items.FirstOrDefault(item => string.Equals(item.Id, change.MessageId, StringComparison.Ordinal));
                    if (gone != null)
                    {
                        _items.Remove(gone);
                        Relayout();
                        UpdateEmptyState();
                    }
                    break;
                case SocialChatChangeKind.Reacted:
                    var target = _items.FirstOrDefault(item => string.Equals(item.Id, change.MessageId, StringComparison.Ordinal));
                    if (target != null && target.Message != null)
                    {
                        target.Replace(target.Message.WithReaction(change.Reaction, change.ReactionAdded, change.ReactionUserId));
                    }
                    break;
                case SocialChatChangeKind.Rooms:
                    ApplyRoom(change.Room);
                    break;
            }
        }

        private void ApplyRoom(SocialChatRoom room)
        {
            if (!IsRoom) return;

            if (room == null)
            {
                var key = _key;
                Close();
                Left?.Invoke(this, key);
                return;
            }

            _room = room;
            RoomPanel.Update(room);
            PaintHeader();
            TitleChanged?.Invoke(this, room.DisplayName);
        }

        private bool AddMessage(SocialChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Id)) return false;
            if (_items.Any(existing => string.Equals(existing.Id, message.Id, StringComparison.Ordinal))) return false;

            var item = SocialChatMessageItem.Create(message, _account, _other, _emojis ?? SocialContentService.CachedEmojiMap);
            Track(item);

            var index = _items.Count;
            while (index > 0)
            {
                var previous = _items[index - 1];
                if (previous.State == SocialChatSendState.Sent && previous.CreatedAt <= message.CreatedAt) break;
                index--;
            }

            _items.Insert(index, item);
            Relayout();
            UpdateEmptyState();
            return true;
        }

        private void Relayout()
        {
            SocialChatMessageItem previous = null;
            var starts = new bool[_items.Count];
            var days = new bool[_items.Count];

            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var startsDay = previous == null || previous.CreatedAt.ToLocalTime().Date != item.CreatedAt.ToLocalTime().Date;
                days[i] = startsDay;
                starts[i] = startsDay
                            || !item.IsSameSenderAs(previous)
                            || item.CreatedAt - previous.CreatedAt > GroupWindow;
                previous = item;
            }

            for (var i = 0; i < _items.Count; i++)
            {
                var endsGroup = i == _items.Count - 1 || starts[i + 1];
                _items[i].SetLayout(days[i], starts[i], endsGroup);
            }
        }

        private string DisplayName
        {
            get
            {
                if (IsRoom) return _room == null ? LocalizedStrings.Get("HeaderChat") : _room.DisplayName;
                return _other == null ? "" : SocialNoteItem.DisplayNameOf(_other);
            }
        }

        private void PaintHeader()
        {
            var room = IsRoom;
            HeaderPicture.Visibility = room ? Visibility.Collapsed : Visibility.Visible;
            HeaderRoomPlate.Visibility = room ? Visibility.Visible : Visibility.Collapsed;
            InfoButton.Visibility = room && _key != null ? Visibility.Visible : Visibility.Collapsed;
            MoreButton.Visibility = !room || CanPopOut ? Visibility.Visible : Visibility.Collapsed;

            if (room)
            {
                PaintRoomHeader();
                return;
            }

            if (_other == null)
            {
                HeaderName.Blocks.Clear();
                HeaderHandle.Text = "";
                HeaderPicture.DisplayName = "";
                HeaderPicture.ProfilePicture = null;
                AutomationProperties.SetName(PersonButton, LocalizedStrings.Get("HeaderChat"));
                ToolTipService.SetToolTip(PersonButton, null);
                return;
            }

            MfmInlineBuilder.Fill(HeaderName, MfmText.ParseName(DisplayName, _other.Emojis), false, MfmInlineBuilder.NameEmojiSize);
            HeaderHandle.Text = SocialLinks.FormatHandle(_other.Username, MisskeyAuthService.InstanceHost);
            HeaderPicture.DisplayName = DisplayName ?? "";
            HeaderPicture.ProfilePicture = AvatarFor(_other);

            var label = LocalizedStrings.Format("SocialMenuViewUserFormat", _other.Handle);
            AutomationProperties.SetName(PersonButton, label);
            ToolTipService.SetToolTip(PersonButton, label);
        }

        private void PaintRoomHeader()
        {
            var name = DisplayName;
            MfmInlineBuilder.Fill(HeaderName, MfmText.ParseName(name, null), false, MfmInlineBuilder.NameEmojiSize);
            HeaderHandle.Text = _memberCount > 0
                                ? LocalizedStrings.Format(_memberCount == 1 ? "SocialChatRoomPeopleOne" : "SocialChatRoomPeopleFormat", _memberCount)
                                : (_room == null ? "" : FirstLine(_room.Description));

            var label = LocalizedStrings.Format("SocialChatRoomInfoForFormat", name);
            AutomationProperties.SetName(PersonButton, label);
            ToolTipService.SetToolTip(PersonButton, label);
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var trimmed = text.Trim();
            var end = trimmed.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? trimmed : trimmed.Substring(0, end);
        }

        private static BitmapImage AvatarFor(SocialUser user)
        {
            var uri = WebLauncher.TryCreateFetchUri(user == null ? null : user.AvatarUrl);
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = 32;
            bitmap.DecodePixelHeight = 32;
            RemoteImage.SetUri(bitmap, uri);
            return bitmap;
        }

        private void ShowLoading(bool loading)
        {
            LoadingRing.IsActive = loading;
            LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowState(string text, bool retry)
        {
            StatePanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            StateText.Text = text ?? "";
            StateButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowLoadFailure(SocialApiStatus status, string errorCode)
        {
            if (status == SocialApiStatus.PermissionDenied)
            {
                ShowState(LocalizedStrings.Get("SocialChatSignInAgainBody"), false);
                return;
            }

            if (IsRoom && string.Equals(errorCode, "NO_SUCH_ROOM", StringComparison.Ordinal))
            {
                ShowState(LocalizedStrings.Get("SocialChatRoomGone"), false);
                return;
            }

            ShowState(LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(status) ?? "SocialChatLoadFailed"), true);
        }

        private void UpdateEmptyState()
        {
            if (_key == null || LoadingRing.IsActive) return;

            OlderUnavailableText.Visibility = _olderUnavailable && _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_items.Count == 0)
            {
                ShowState(_olderUnavailable
                          ? LocalizedStrings.Get("SocialChatMessagesUnavailable")
                          : IsRoom
                            ? LocalizedStrings.Get("SocialChatRoomEmpty")
                            : LocalizedStrings.Format("SocialChatEmptyFormat", MfmText.PlainName(DisplayName)),
                          false);
            }
            else if (!StateButton.IsShown())
            {
                ShowState(null, false);
            }
        }

        private void ScrollToBottomSoon()
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                try
                {
                    if (_items.Count == 0) return;
                    MessageList.ScrollIntoView(_items[_items.Count - 1]);
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialChatView: could not scroll to the newest message", ex);
                }
            });
        }

        private void MessageList_Loaded(object sender, RoutedEventArgs e)
        {
            if (_scroller != null) return;

            _scroller = VisualTreeSearch.FindDescendant<ScrollViewer>(MessageList);
            if (_scroller != null) _scroller.ViewChanged += Scroller_ViewChanged;
        }

        private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_scroller == null || _scroller.ScrollableHeight <= 0) return;

            if (_scroller.VerticalOffset < OlderLoadThreshold) LoadOlderInBackground();
        }

        private void StateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_key == null) return;

            ShowState(null, false);
            LoadInitialInBackground();
        }

        private async void PersonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsRoom)
                {
                    ToggleRoomPanel();
                    return;
                }

                if (_other != null) await SocialWindows.ShowUserAsync(_other);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the profile", ex);
            }
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleRoomPanel();
        }

        private SocialChatArgs CurrentArgs
        {
            get
            {
                if (_key == null || _account == null) return null;
                return IsRoom ? SocialChatArgs.ForRoom(_account, _roomId, _room) : new SocialChatArgs(_account, _otherId, _other);
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MenuFlyout flyout;
                if (IsRoom)
                {
                    flyout = new MenuFlyout();
                }
                else
                {
                    if (_other == null) return;
                    flyout = SocialMenus.BuildForUser(_other, null, this) ?? new MenuFlyout();
                }

                if (CanPopOut) SocialMenus.AddChatWindowItem(flyout, CurrentArgs);
                if (flyout.Items.Count > 0) flyout.ShowAt(MoreButton);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the conversation menu", ex);
            }
        }

        private void SocialChatView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePanelMode(e.NewSize.Width);
        }

        private bool IsOverlay
        {
            get
            {
                var mode = RoomSplit.DisplayMode;
                return mode == SplitViewDisplayMode.Overlay;
            }
        }

        private void UpdatePanelMode(double width)
        {
            if (width <= 0) return;

            var overlay = width < InlinePanelMinWidth;
            if (overlay == IsOverlay) return;

            RoomSplit.DisplayMode = overlay ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
            if (RoomSplit.IsPaneOpen) OverlayOwner = overlay ? this : null;
        }

        private void ToggleRoomPanel()
        {
            if (RoomSplit.IsPaneOpen)
            {
                CloseRoomPanel(true);
                return;
            }

            if (!IsRoom || _room == null) return;

            RoomPanel.Show(_room, _account);
            RoomPanel.Refresh();
            RoomSplit.IsPaneOpen = true;
            if (IsOverlay) OverlayOwner = this;
            RoomPanel.FocusFirst();
        }

        private void CloseRoomPanel(bool restoreFocus)
        {
            if (ReferenceEquals(OverlayOwner, this)) OverlayOwner = null;
            if (!RoomSplit.IsPaneOpen) return;

            RoomSplit.IsPaneOpen = false;
            if (restoreFocus && InfoButton.IsShown()) InfoButton.Focus(FocusState.Programmatic);
        }

        private void RoomSplit_PaneClosed(SplitView sender, object args)
        {
            if (ReferenceEquals(OverlayOwner, this)) OverlayOwner = null;
        }

        private void RoomPanel_CloseRequested(object sender, EventArgs e)
        {
            CloseRoomPanel(true);
        }

        private void RoomPanel_MembersCounted(object sender, int count)
        {
            if (!IsRoom) return;

            _memberCount = count;
            PaintHeader();
        }

        private void MessageList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            try
            {
                if (args.Handled) return;

                var target = args.OriginalSource as FrameworkElement;
                var item = ItemFor(target);
                if (item == null) return;

                var anchor = (FrameworkElement)VisualTreeSearch.FindAncestor<SocialChatBubble>(target) ?? target;
                var key = _key;
                var flyout = SocialMenus.BuildForChatMessage(item, this, () => DeleteAsync(item),
                                                             () =>
                                                             {
                                                                 ShowReactionPicker(item, anchor);
                                                                 return Task.CompletedTask;
                                                             },
                                                             reaction => SocialChatService.ReactAsync(this, key, item.Message, reaction, false));
                if (flyout == null) return;

                Point point;
                SocialMenus.ShowContextAt(flyout, target, args.TryGetPosition(target, out point) ? point : (Point?)null);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the message menu", ex);
            }
        }

        private static SocialChatMessageItem ItemFor(FrameworkElement element)
        {
            DependencyObject current = element;
            while (current != null)
            {
                var framework = current as FrameworkElement;
                var item = framework == null ? null : framework.DataContext as SocialChatMessageItem;
                if (item != null) return item;

                var bubble = current as SocialChatBubble;
                if (bubble != null) return bubble.Item;

                var container = current as ListViewItem;
                if (container != null) return container.Content as SocialChatMessageItem;

                current = Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void Track(SocialChatMessageItem item)
        {
            if (item == null) return;

            item.ReactRequested += Item_ReactRequested;
            item.UnreactRequested += Item_UnreactRequested;
            item.ReactWithRequested += Item_ReactWithRequested;
        }

        private void Item_ReactRequested(object sender, FrameworkElement anchor)
        {
            var item = sender as SocialChatMessageItem;
            if (item != null) ShowReactionPicker(item, anchor);
        }

        private void Item_UnreactRequested(object sender, string key)
        {
            var item = sender as SocialChatMessageItem;
            if (item != null) ReactInBackground(item, key, false);
        }

        private void Item_ReactWithRequested(object sender, string key)
        {
            var item = sender as SocialChatMessageItem;
            if (item != null) ReactInBackground(item, key, true);
        }

        private void ShowReactionPicker(SocialChatMessageItem item, FrameworkElement anchor)
        {
            try
            {
                if (item == null || anchor == null || !item.CanReact) return;

                var flyout = _reactionFlyout ?? (_reactionFlyout = new Flyout());
                var picker = SocialEmojiPicker.ForCurrentView();
                picker.AttachTo(flyout);
                picker.Configure(SocialEmojiPickerMode.Insert, null, null, key =>
                {
                    flyout.Hide();
                    ReactInBackground(item, key, true);
                }, null, null);
                flyout.ShowAt(anchor);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the reaction picker", ex);
            }
        }

        private async void ReactInBackground(SocialChatMessageItem item, string reaction, bool add)
        {
            try
            {
                if (item.Message == null || !SocialReactions.CanReactWith(reaction)) return;

                await SocialChatService.ReactAsync(this, _key, item.Message, reaction, add);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: the reaction failed", ex);
            }
        }

        private async Task DeleteAsync(SocialChatMessageItem item)
        {
            if (item == null || item.Message == null) return;

            await SocialChatService.DeleteAsync(this, _key, item.Message);
        }
    }
}
