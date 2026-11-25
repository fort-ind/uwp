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

        private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(AppConstants.SocialChatGroupMinutes);

        private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(700);

        private readonly ObservableCollection<SocialChatMessageItem> _items = new ObservableCollection<SocialChatMessageItem>();

        private readonly Debouncer _readDebounce = new Debouncer();

        private CancellationTokenSource _loadCts;

        private SocialChatWatch _watch;

        private ScrollViewer _scroller;

        private Flyout _reactionFlyout;

        private string _account;

        private string _otherId;

        private SocialUser _other;

        private SocialUserDetail _detail;

        private IReadOnlyDictionary<string, Uri> _emojis;

        private bool _isLoaded;

        private bool _handlersAttached;

        private bool _windowActive = true;

        private bool _presented = true;

        private bool _loadingOlder;

        private bool _reachedOldest;

        private bool _theyMessaged;

        private int _viewId = -1;

        public SocialChatView()
        {
            this.InitializeComponent();

            MessageList.ItemsSource = _items;
            Loaded += SocialChatView_Loaded;
            Unloaded += SocialChatView_Unloaded;
            AttachDropHandlers();

            PaintHeader();
            UpdateComposer();
        }

        public event EventHandler<SocialUser> PersonResolved;

        public bool CanPopOut { get; set; } = true;

        public string OtherUserId
        {
            get { return _otherId; }
        }

        public bool HasConversation
        {
            get { return _otherId != null; }
        }

        public void Open(SocialChatArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.UserId)) return;

            if (string.Equals(args.UserId, _otherId, StringComparison.Ordinal)
                && string.Equals(args.Account, _account, StringComparison.Ordinal))
            {
                if (_other == null && args.User != null)
                {
                    _other = args.User;
                    PaintHeader();
                }
                return;
            }

            SaveDraft();
            ResetConversation();

            _account = args.Account;
            _otherId = args.UserId;
            _other = args.User;

            PaintHeader();
            HeaderBar.Visibility = Visibility.Visible;
            ComposerBar.Visibility = Visibility.Visible;

            SetMessageText(SocialChatService.DraftFor(_otherId) ?? "");
            if (_watch != null) _watch.Set(_otherId);
            UpdateActive();
            UpdateComposer();

            LoadInitialInBackground();
            LoadDetailInBackground();
        }

        public void Close()
        {
            SaveDraft();
            ResetConversation();
            _otherId = null;
            _other = null;
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
            Relayout();
        }

        public void Release()
        {
            SaveDraft();
            Detach();
            CancelLoad();
            _readDebounce.Cancel();
            ClearAttachment(true);
        }

        private void ResetConversation()
        {
            CancelLoad();
            _readDebounce.Cancel();
            ClearAttachment(true);
            _items.Clear();
            _detail = null;
            _theyMessaged = false;
            _reachedOldest = false;
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
                    _watch.Set(_otherId);
                }

                UpdateActive();
                if (_otherId != null && _items.Count > 0) CatchUpInBackground(false);
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
            get { return _isLoaded && _presented && _windowActive && _otherId != null; }
        }

        private void UpdateActive()
        {
            if (_viewId < 0) return;

            var active = IsActiveConversation;
            SocialChatService.SetActive(_viewId, active ? _otherId : null);
            if (active && SocialChatService.IsUnread(_otherId)) MarkReadSoon();
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

        private async void LoadInitialInBackground()
        {
            var other = _otherId;
            try
            {
                var token = BeginLoad();
                ShowLoading(true);
                ShowState(null, false);

                var emojis = await EmojisAsync();
                var result = await SocialChatService.FetchTimelineAsync(other, null, null, token);
                if (token.IsCancellationRequested || !string.Equals(other, _otherId, StringComparison.Ordinal)) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    ShowLoadFailure(result.Status);
                    return;
                }

                var loaded = result.Value.OrderBy(message => message.CreatedAt)
                                         .Select(message => SocialChatMessageItem.Create(message, _account, _other, emojis))
                                         .ToList();
                foreach (var item in loaded)
                {
                    Track(item);
                    _items.Add(item);
                }

                _reachedOldest = result.Value.Count < AppConstants.SocialChatPageSize;
                _theyMessaged = _items.Any(entry => !entry.IsMine);
                Relayout();
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
                if (string.Equals(other, _otherId, StringComparison.Ordinal)) ShowLoadFailure(SocialApiStatus.Failed);
            }
            finally
            {
                if (string.Equals(other, _otherId, StringComparison.Ordinal)) ShowLoading(false);
            }
        }

        private async void LoadDetailInBackground()
        {
            var other = _otherId;
            try
            {
                var result = await SocialContentService.FetchUserAsync(other, CancellationToken.None);
                if (!string.Equals(other, _otherId, StringComparison.Ordinal) || result.Status != SocialApiStatus.Ok) return;

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
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not load the person", ex);
            }
        }

        private async void LoadOlderInBackground()
        {
            var other = _otherId;
            var started = false;
            try
            {
                if (_loadingOlder || _reachedOldest || other == null) return;

                var oldest = _items.FirstOrDefault(item => item.Id != null);
                if (oldest == null) return;

                started = true;
                _loadingOlder = true;
                OlderLoading.Visibility = Visibility.Visible;
                OlderLoading.IsActive = true;

                var token = _loadCts == null ? CancellationToken.None : _loadCts.Token;
                var result = await SocialChatService.FetchTimelineAsync(other, null, oldest.Id, token);
                if (!string.Equals(other, _otherId, StringComparison.Ordinal)) return;

                if (result.Status != SocialApiStatus.Ok) return;

                var emojis = await EmojisAsync();
                var older = result.Value.Where(message => !_items.Any(item => string.Equals(item.Id, message.Id, StringComparison.Ordinal)))
                                        .OrderByDescending(message => message.CreatedAt)
                                        .Select(message => SocialChatMessageItem.Create(message, _account, _other, emojis))
                                        .ToList();
                foreach (var item in older)
                {
                    Track(item);
                    _items.Insert(0, item);
                }

                _reachedOldest = result.Value.Count < AppConstants.SocialChatPageSize;
                if (!_theyMessaged) _theyMessaged = _items.Any(item => !item.IsMine);
                Relayout();
                UpdateComposer();
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
            var other = _otherId;
            try
            {
                var token = _readDebounce.Restart();
                await Task.Delay(ReadDelay, token);
                if (!string.Equals(other, _otherId, StringComparison.Ordinal) || other == null) return;
                if (onlyWhileActive && !IsActiveConversation) return;

                var newest = _items.LastOrDefault(item => item.Id != null);
                var result = await SocialChatService.FetchTimelineAsync(other, newest == null ? null : newest.Id, null, token);
                if (!string.Equals(other, _otherId, StringComparison.Ordinal) || result.Status != SocialApiStatus.Ok) return;

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

            if (_otherId == null || !string.Equals(change.OtherUserId, _otherId, StringComparison.Ordinal)) return;

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
                            AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialChatNewMessageAnnouncementFormat",
                                                                                          MfmText.PlainName(DisplayName)),
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
                    if (target != null) target.Replace(target.Message.WithReaction(change.Reaction, change.ReactionAdded));
                    break;
            }
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
            get { return _other == null ? "" : SocialNoteItem.DisplayNameOf(_other); }
        }

        private void PaintHeader()
        {
            if (_other == null)
            {
                HeaderName.Blocks.Clear();
                HeaderHandle.Text = "";
                HeaderPicture.DisplayName = "";
                HeaderPicture.ProfilePicture = null;
                AutomationProperties.SetName(PersonButton, LocalizedStrings.Get("HeaderChat"));
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

        private void ShowLoadFailure(SocialApiStatus status)
        {
            if (status == SocialApiStatus.PermissionDenied)
            {
                ShowState(LocalizedStrings.Get("SocialChatSignInAgainBody"), false);
                return;
            }

            ShowState(LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(status) ?? "SocialChatLoadFailed"), true);
        }

        private void UpdateEmptyState()
        {
            if (_otherId == null || LoadingRing.IsActive) return;

            if (_items.Count == 0)
            {
                ShowState(LocalizedStrings.Format("SocialChatEmptyFormat", MfmText.PlainName(DisplayName)), false);
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
            if (_otherId == null) return;

            ShowState(null, false);
            LoadInitialInBackground();
        }

        private async void PersonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_other != null) await SocialWindows.ShowUserAsync(_other);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the profile", ex);
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_other == null) return;

                var flyout = SocialMenus.BuildForUser(_other, null, this) ?? new MenuFlyout();
                if (CanPopOut) SocialMenus.AddChatWindowItem(flyout, _otherId, _other);
                if (flyout.Items.Count > 0) flyout.ShowAt(MoreButton);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the person menu", ex);
            }
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
                var flyout = SocialMenus.BuildForChatMessage(item, this, () => DeleteAsync(item),
                                                             () =>
                                                             {
                                                                 ShowReactionPicker(item, anchor);
                                                                 return Task.CompletedTask;
                                                             },
                                                             key => SocialChatService.ReactAsync(this, _otherId, item.Message, key, false));
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

        private async void ReactInBackground(SocialChatMessageItem item, string key, bool add)
        {
            try
            {
                if (item.Message == null || !SocialReactions.CanReactWith(key)) return;

                await SocialChatService.ReactAsync(this, _otherId, item.Message, key, add);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: the reaction failed", ex);
            }
        }

        private async Task DeleteAsync(SocialChatMessageItem item)
        {
            if (item == null || item.Message == null) return;

            await SocialChatService.DeleteAsync(this, _otherId, item.Message);
        }
    }
}
