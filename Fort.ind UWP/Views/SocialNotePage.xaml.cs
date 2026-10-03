using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialNotePage : Page, IReleasablePage, IReopenablePage, ITrimmablePage
    {
        private enum ListState
        {
            Loading,
            Ready,
            Empty,
            Failed
        }

        private const int MaxReplyDepth = AppConstants.SocialThreadReplyDepthLimit - 1;

        private readonly ObservableCollection<SocialNoteItem> _ancestors = new ObservableCollection<SocialNoteItem>();

        private readonly SocialFeedCollection<SocialNoteItem> _replies;

        private readonly SocialFeedCollection<SocialNotePersonItem> _renotes;

        private readonly SocialFeedCollection<SocialNotePersonItem> _noPeople;

        private readonly TabFeed _repliesFeed = new TabFeed();

        private readonly TabFeed _renotesFeed = new TabFeed();

        private readonly Dictionary<string, ReactionFeed> _reactionFeeds = new Dictionary<string, ReactionFeed>(StringComparer.Ordinal);

        private readonly List<SocialReactionChip> _reactionFilters = new List<SocialReactionChip>();

        private readonly HashSet<string> _pendingReplies = new HashSet<string>(StringComparer.Ordinal);

        private SocialNoteArgs _args;

        private SocialNoteItem _focused;

        private SocialThreadTab _tab = SocialThreadTab.Replies;

        private string _reactionType;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private SocialNoteWatch _watch;

        private int _noteVersion;

        private bool _noteHandlerAttached;

        private bool _replyBoxReady;

        private bool _released;

        private bool IsReleased => _released;

        public SocialNotePage()
        {
            this.InitializeComponent();

            _replies = new SocialFeedCollection<SocialNoteItem>(LoadMoreRepliesAsync, SocialFeedPaging.NonEmpty);
            _renotes = new SocialFeedCollection<SocialNotePersonItem>(LoadMoreRenotesAsync, SocialFeedPaging.NonEmpty);
            _noPeople = new SocialFeedCollection<SocialNotePersonItem>((untilId, token) => Task.FromResult<IReadOnlyList<SocialNotePersonItem>>(null),
                                                                      SocialFeedPaging.NonEmpty);
            AncestorsList.ItemsSource = _ancestors;
            NoteList.ItemsSource = _replies;

            SocialNoteShortcutsBehavior.Add(FocusedHost, FocusedHost);
        }

        internal bool Shows(string noteId)
        {
            return _args != null && string.Equals(_args.NoteId, noteId, StringComparison.Ordinal);
        }

        internal void Reveal(bool focusReply, SocialThreadTab tab)
        {
            SelectTab(tab);
            if (focusReply) FocusReplyBox();
        }

        private void SetUpReplyBox()
        {
            if (_focused == null || _args == null) return;

            var canReply = SocialNoteActionService.CanAct(_focused);
            ReplyBoxSlot.Visibility = canReply ? Visibility.Visible : Visibility.Collapsed;
            if (!canReply || _replyBoxReady) return;

            _replyBoxReady = true;
            var draft = SocialDraftService.ReplyDraft(_args.AccountId, _args.NoteId);
            ReplyComposer.Load(draft, new SocialComposeContext(SocialComposerMode.Reply, _focused.Note, null, null));
            ReplyComposer.ApplyReplyDefaults(SocialPostService.ReplyDefaultsFor(_focused.Note));

            if (_args.FocusReply) FocusReplyBoxSoon();
        }

        internal void ReloadReplyDraft()
        {
            try
            {
                if (!_replyBoxReady || _args == null || _focused == null) return;

                var draft = SocialDraftService.ReplyDraft(_args.AccountId, _args.NoteId);
                if (draft == null) return;

                ReplyComposer.Load(draft, new SocialComposeContext(SocialComposerMode.Reply, _focused.Note, null, null));
                ReplyComposer.ApplyReplyDefaults(SocialPostService.ReplyDefaultsFor(_focused.Note));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not restore the reply - {ex.Message}");
            }
        }

        private async void FocusReplyBoxSoon()
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                FocusReplyBox();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not focus the reply box - {ex.Message}");
            }
        }

        private void FocusReplyBox()
        {
            if (!ReplyBoxSlot.IsShown()) return;

            ReplyComposer.FocusEditor();
            ReplyBoxSlot.StartBringIntoView();
        }

        private void SaveReplyDraft()
        {
            if (!_replyBoxReady || _args == null) return;

            SocialDraftService.SaveReplyDraft(_args.AccountId, _args.NoteId, ReplyComposer.GetDraft());
        }

        private void ReplyComposer_DraftChanged(object sender, EventArgs e)
        {
            try
            {
                SaveReplyDraft();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not keep the reply draft - {ex.Message}");
            }
        }

        private void ReplyComposer_Posted(object sender, SocialNote reply)
        {
            ResetReplyBox();
        }

        private void ReplyComposer_Discarded(object sender, EventArgs e)
        {
            ResetReplyBox();
        }

        private void ResetReplyBox()
        {
            try
            {
                if (_args == null || _focused == null) return;

                SocialDraftService.SaveReplyDraft(_args.AccountId, _args.NoteId, null);
                ReplyComposer.Load(null, new SocialComposeContext(SocialComposerMode.Reply, _focused.Note, null, null));
                ReplyComposer.ApplyReplyDefaults(SocialPostService.ReplyDefaultsFor(_focused.Note));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not reset the reply box - {ex.Message}");
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                var args = e.Parameter as SocialNoteArgs;
                if (args == null) return;

                _args = args;

                if (!SocialContentService.IsCurrentAccount(args.AccountId))
                {
                    LeaveSoon();
                    return;
                }

                if (args.Note != null)
                {
                    _focused = SocialNoteItem.Create(args.Note, SocialContentService.CachedEmojiMap, false);
                    ShowFocused();
                }

                SelectTab(args.InitialTab);
                LoadThread();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not show the note - {ex.GetType().Name}: {ex.Message}");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            Release();
        }

        public void Reopen(object parameter)
        {
            try
            {
                var args = parameter as SocialUserWindowArgs;
                if (args == null || Frame == null) return;

                if (!SocialContentService.IsCurrentAccount(args.AccountId))
                {
                    WindowManagerService.CloseCurrentWindow();
                    return;
                }

                var stack = Frame.BackStack;
                var target = -1;
                for (var i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].SourcePageType == typeof(SocialUserPage))
                    {
                        target = i;
                        break;
                    }
                }

                if (target < 0) return;

                while (stack.Count - 1 > target)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                Frame.GoBack();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not return to the profile - {ex.Message}");
            }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            DetachNoteHandler();
            WatchFocused(false);

            try
            {
                SaveReplyDraft();
                ReplyComposer.ReleaseUploads();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not keep the reply draft - {ex.Message}");
            }

            try
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not cancel pending loads - {ex.Message}");
            }

            if (_watch != null)
            {
                _watch.Dispose();
                _watch = null;
            }

            _replies.ReplaceAll(new SocialNoteItem[0], false);
            _renotes.ReplaceAll(new SocialNotePersonItem[0], false);
            foreach (var feed in _reactionFeeds.Values)
            {
                feed.Collection.ReplaceAll(new SocialNotePersonItem[0], false);
            }
            _ancestors.Clear();
        }

        public void Trim()
        {
            if (_released) return;

            _replies.TrimToFirstPage();
            _renotes.TrimToFirstPage();
            foreach (var feed in _reactionFeeds.Values)
            {
                feed.Collection.TrimToFirstPage();
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_released || _noteHandlerAttached) return;

                SocialNoteService.Changed += SocialNoteService_Changed;
                _noteHandlerAttached = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not watch note changes - {ex.Message}");
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachNoteHandler();
        }

        private void DetachNoteHandler()
        {
            if (!_noteHandlerAttached) return;

            SocialNoteService.Changed -= SocialNoteService_Changed;
            _noteHandlerAttached = false;
        }

        private async void LeaveSoon()
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => LeaveIfAccountChanged());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not leave - {ex.Message}");
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

        private void ShowFocused()
        {
            if (_focused == null) return;

            WatchFocused(true);
            FocusedView.DataContext = _focused;
            FocusedView.Item = _focused;
            FocusedView.Visibility = Visibility.Visible;
            NoteStatePanel.Visibility = Visibility.Collapsed;
            NoteLoadingRing.IsActive = false;
            NoteLoadingRing.Visibility = Visibility.Collapsed;
            TabBar.Visibility = Visibility.Visible;
            UpdateTabCounts();
            SetUpReplyBox();
        }

        private void WatchFocused(bool watch)
        {
            if (_focused == null) return;

            _focused.PropertyChanged -= Focused_PropertyChanged;
            if (watch) _focused.PropertyChanged += Focused_PropertyChanged;
        }

        private void Focused_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (_released) return;

                switch (e.PropertyName)
                {
                    case "Note":
                    case "Reactions":
                    case "RepliesCount":
                        UpdateTabCounts();
                        if (_tab == SocialThreadTab.Reactions) ShowReactions();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not follow the note's changes - {ex.Message}");
            }
        }

        private void UpdateTabCounts()
        {
            if (_focused == null) return;

            SetTabCount(RepliesTabCount, _focused.RepliesCount);
            SetTabCount(RenotesTabCount, _focused.RenoteCount);
            SetTabCount(ReactionsTabCount, _focused.ReactionCount);

            AutomationProperties.SetName(RepliesTab, TabName("SocialThreadRepliesTabName", _focused.RepliesCount));
            AutomationProperties.SetName(RenotesTab, TabName("SocialThreadRenotesTabName", _focused.RenoteCount));
            AutomationProperties.SetName(ReactionsTab, TabName("SocialThreadReactionsTabName", _focused.ReactionCount));
        }

        private static string TabName(string key, int count)
        {
            return LocalizedStrings.Format(key, CountText.Format(Math.Max(0, count)));
        }

        private static void SetTabCount(TextBlock text, int count)
        {
            text.Text = count > 0 ? CountText.Format(count) : "";
            text.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void LoadThread()
        {
            var version = ++_noteVersion;
            var token = _cancellation.Token;

            if (_focused == null)
            {
                NoteStatePanel.Visibility = Visibility.Collapsed;
                NoteLoadingRing.IsActive = true;
                NoteLoadingRing.Visibility = Visibility.Visible;
            }

            try
            {
                var emojiTask = SocialContentService.GetEmojiMapAsync();
                var showTask = SocialContentService.FetchNoteAsync(_args.NoteId, token);

                var expectsAncestors = _args.Note == null || !string.IsNullOrEmpty(_args.Note.ReplyId);
                if (expectsAncestors && _ancestors.Count == 0) AncestorsLoading.Visibility = Visibility.Visible;
                var conversationTask = expectsAncestors ? SocialContentService.FetchConversationAsync(_args.NoteId, token) : null;

                var emojis = await emojiTask;
                var shown = await showTask;
                if (version != _noteVersion || IsReleased) return;

                if (shown.Status == SocialApiStatus.Ok)
                {
                    if (_focused == null)
                    {
                        _focused = SocialNoteItem.Create(shown.Value, emojis, false);
                        ShowFocused();
                        ShowTab(_tab);
                    }
                    else
                    {
                        SocialNoteService.Raise(SocialNoteChange.Snapshot(shown.Value));
                    }

                    if (conversationTask == null && !string.IsNullOrEmpty(shown.Value.ReplyId))
                    {
                        AncestorsLoading.Visibility = Visibility.Visible;
                        conversationTask = SocialContentService.FetchConversationAsync(_args.NoteId, token);
                    }
                }
                else if (_focused == null)
                {
                    AncestorsLoading.Visibility = Visibility.Collapsed;
                    ShowNoteState(shown.Status == SocialApiStatus.Refused);
                    return;
                }

                if (conversationTask != null)
                {
                    var conversation = await conversationTask;
                    if (version != _noteVersion || IsReleased) return;

                    AncestorsLoading.Visibility = Visibility.Collapsed;
                    if (conversation.Status == SocialApiStatus.Ok) ShowAncestors(conversation.Value, emojis);
                }

                UpdateWatch();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialNotePage: note load cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: note load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == _noteVersion && !IsReleased)
                {
                    AncestorsLoading.Visibility = Visibility.Collapsed;
                    if (_focused == null) ShowNoteState(false);
                }
            }
        }

        private void ShowNoteState(bool missing)
        {
            NoteLoadingRing.IsActive = false;
            NoteLoadingRing.Visibility = Visibility.Collapsed;
            FocusedView.Visibility = Visibility.Collapsed;
            TabBar.Visibility = Visibility.Collapsed;
            FooterHost.Visibility = Visibility.Collapsed;

            NoteStateGlyph.Glyph = missing ? "" : "";
            NoteStateText.Text = LocalizedStrings.Get(missing ? "SocialThreadNotFound" : "SocialThreadFailed");
            NoteStateButton.Content = LocalizedStrings.Get("SocialRetryButton");
            NoteStateButton.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
            NoteStatePanel.Visibility = Visibility.Visible;
            AutomationHelper.AnnounceLiveRegion(NoteStateText);
        }

        private void NoteStateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_released || _args == null) return;

                FooterHost.Visibility = Visibility.Visible;
                LoadThread();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: retry failed - {ex.Message}");
            }
        }

        private void ShowAncestors(IReadOnlyList<SocialNote> conversation, IReadOnlyDictionary<string, Uri> emojis)
        {
            var ordered = new List<SocialNote>(conversation);
            ordered.Reverse();

            var hadAncestors = _ancestors.Count > 0;
            _ancestors.Clear();
            foreach (var item in SocialNoteItem.CreateAll(ordered, emojis, false))
            {
                _ancestors.Add(item);
            }

            AncestorsList.Visibility = _ancestors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!hadAncestors && _ancestors.Count > 0) KeepFocusedInView();
        }

        private void KeepFocusedInView()
        {
            try
            {
                var scroller = VisualTreeSearch.FindDescendantByName(NoteList, "ScrollViewer") as ScrollViewer;
                if (scroller == null || scroller.VerticalOffset > 1) return;

                NoteList.UpdateLayout();
                scroller.ChangeView(null, AncestorsList.ActualHeight, null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not keep the note in view - {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_released || _args == null) return;

                _repliesFeed.Requested = false;
                _renotesFeed.Requested = false;
                foreach (var feed in _reactionFeeds.Values)
                {
                    feed.Requested = false;
                }

                LoadThread();
                ShowTab(_tab);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: refresh failed - {ex.Message}");
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == RepliesTab) ShowTab(SocialThreadTab.Replies);
            else if (sender == RenotesTab) ShowTab(SocialThreadTab.Renotes);
            else if (sender == ReactionsTab) ShowTab(SocialThreadTab.Reactions);
        }

        private void SelectTab(SocialThreadTab tab)
        {
            var button = tab == SocialThreadTab.Renotes ? RenotesTab : tab == SocialThreadTab.Reactions ? ReactionsTab : RepliesTab;
            if (button.IsChecked.GetValueOrDefault())
            {
                ShowTab(tab);
            }
            else
            {
                button.IsChecked = true;
            }
        }

        private void ShowTab(SocialThreadTab tab)
        {
            if (_released || _args == null) return;

            if (tab != _tab) TrimTab(_tab);
            _tab = tab;

            switch (tab)
            {
                case SocialThreadTab.Replies:
                    ReactionFilterScroller.Visibility = Visibility.Collapsed;
                    SetSource(_replies, "SocialNoteItemTemplate", "SocialThreadRepliesListName");
                    if (!_repliesFeed.Requested) LoadReplies();
                    else SetState(_repliesFeed.State);
                    break;
                case SocialThreadTab.Renotes:
                    ReactionFilterScroller.Visibility = Visibility.Collapsed;
                    SetSource(_renotes, "SocialNotePersonTemplate", "SocialThreadRenotesListName");
                    if (!_renotesFeed.Requested) LoadRenotes();
                    else SetState(_renotesFeed.State);
                    break;
                case SocialThreadTab.Reactions:
                    ShowReactions();
                    break;
            }
        }

        private void TrimTab(SocialThreadTab tab)
        {
            switch (tab)
            {
                case SocialThreadTab.Replies:
                    _replies.TrimToFirstPage();
                    break;
                case SocialThreadTab.Renotes:
                    _renotes.TrimToFirstPage();
                    break;
                case SocialThreadTab.Reactions:
                    foreach (var feed in _reactionFeeds.Values)
                    {
                        feed.Collection.TrimToFirstPage();
                    }
                    break;
            }
        }

        private void SetSource(object source, string templateKey, string nameKey)
        {
            var template = Resources[templateKey] as DataTemplate;
            if (!ReferenceEquals(NoteList.ItemTemplate, template)) NoteList.ItemTemplate = template;
            if (!ReferenceEquals(NoteList.ItemsSource, source)) NoteList.ItemsSource = source;
            AutomationProperties.SetName(NoteList, LocalizedStrings.Get(nameKey));
        }

        private async void LoadReplies()
        {
            var feed = _repliesFeed;
            var version = ++feed.Version;
            feed.Requested = true;
            SetFeedState(feed, SocialThreadTab.Replies, ListState.Loading);

            try
            {
                await SocialContentService.GetEmojiMapAsync();
                if (version != feed.Version || IsReleased) return;

                var page = await LoadReplyPageAsync(null, _cancellation.Token);
                if (version != feed.Version || IsReleased) return;

                if (page == null)
                {
                    SetFeedState(feed, SocialThreadTab.Replies, ListState.Failed);
                    return;
                }

                _replies.ReplaceAll(page.Items, page.TopLevelCount >= AppConstants.SocialThreadPageSize);
                SetFeedState(feed, SocialThreadTab.Replies, page.Items.Count == 0 ? ListState.Empty : ListState.Ready);
                UpdateWatch();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialNotePage: replies load cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: replies load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == feed.Version && !IsReleased) SetFeedState(feed, SocialThreadTab.Replies, ListState.Failed);
            }
        }

        private async Task<IReadOnlyList<SocialNoteItem>> LoadMoreRepliesAsync(string afterId, CancellationToken cancellationToken)
        {
            if (_released || _args == null) return null;

            var page = await LoadReplyPageAsync(afterId, cancellationToken);
            if (page == null || IsReleased) return null;

            var items = page.Items.Where(item => IndexOfReply(item.Note.Id) < 0).ToList();
            UpdateWatch();
            return items;
        }

        private async Task<ReplyPage> LoadReplyPageAsync(string sinceId, CancellationToken cancellationToken)
        {
            var result = await SocialContentService.FetchChildrenAsync(_args.NoteId, sinceId, AppConstants.SocialThreadPageSize, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            var top = new List<ReplyNode>();
            foreach (var note in result.Value)
            {
                top.Add(new ReplyNode(note, 0));
            }

            await PrefetchAsync(top, cancellationToken);

            var items = new List<SocialNoteItem>();
            var emojis = SocialContentService.CachedEmojiMap;
            foreach (var node in top)
            {
                Flatten(node, node.Note.Id, emojis, items);
            }

            return new ReplyPage(items, top.Count);
        }

        private static async Task PrefetchAsync(List<ReplyNode> top, CancellationToken cancellationToken)
        {
            var budget = AppConstants.SocialThreadPrefetchBudget;
            var level = top;

            using (var gate = new SemaphoreSlim(AppConstants.SocialThreadPrefetchConcurrency))
            {
                while (level.Count > 0)
                {
                    var fetches = new List<Task>();
                    foreach (var node in level.Where(node => node.Note.RepliesCount > 0))
                    {
                        if (node.Depth >= MaxReplyDepth)
                        {
                            node.Deeper = true;
                            continue;
                        }

                        if (budget <= 0)
                        {
                            node.More = node.Note.RepliesCount;
                            continue;
                        }

                        budget--;
                        fetches.Add(FetchChildrenAsync(node, gate, cancellationToken));
                    }

                    await Task.WhenAll(fetches);

                    var next = new List<ReplyNode>();
                    foreach (var node in level)
                    {
                        next.AddRange(node.Children);
                    }

                    level = next;
                }
            }
        }

        private static async Task FetchChildrenAsync(ReplyNode node, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await SocialContentService.FetchChildrenAsync(node.Note.Id, null, AppConstants.SocialThreadChildPageSize, cancellationToken);
                if (result.Status != SocialApiStatus.Ok)
                {
                    node.More = node.Note.RepliesCount;
                    return;
                }

                foreach (var child in result.Value)
                {
                    node.Children.Add(new ReplyNode(child, node.Depth + 1));
                }

                if (result.Value.Count >= AppConstants.SocialThreadChildPageSize && node.Note.RepliesCount > result.Value.Count)
                {
                    node.More = node.Note.RepliesCount - result.Value.Count;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private static void Flatten(ReplyNode node, string pagingId, IReadOnlyDictionary<string, Uri> emojis, List<SocialNoteItem> items)
        {
            var item = SocialNoteItem.Create(node.Note, emojis, false, node.Depth, pagingId);
            if (item == null) return;

            item.SetContinuation(node.More, node.Deeper);
            items.Add(item);

            foreach (var child in node.Children)
            {
                Flatten(child, pagingId, emojis, items);
            }
        }

        private void LoadRenotes()
        {
            LoadPeople(_renotesFeed, SocialThreadTab.Renotes, _renotes,
                       token => LoadRenotePageAsync(null, token));
        }

        private async Task<IReadOnlyList<SocialNotePersonItem>> LoadMoreRenotesAsync(string untilId, CancellationToken cancellationToken)
        {
            if (_released || _args == null) return null;

            return await LoadRenotePageAsync(untilId, cancellationToken);
        }

        private async Task<IReadOnlyList<SocialNotePersonItem>> LoadRenotePageAsync(string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialContentService.FetchRenotesAsync(_args.NoteId, untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return result.Value.Select(SocialNotePersonItem.FromRenote).Where(item => item != null).ToList();
        }

        private void ShowReactions()
        {
            var reactions = _focused == null ? null : _focused.Reactions;
            BuildReactionFilters(reactions);

            if (reactions == null || reactions.Count == 0)
            {
                ReactionFilterScroller.Visibility = Visibility.Collapsed;
                SetSource(_noPeople, "SocialNotePersonTemplate", "SocialThreadReactionsListName");
                SetState(_focused == null ? ListState.Loading : ListState.Empty);
                return;
            }

            ReactionFilterScroller.Visibility = Visibility.Visible;

            var selected = _reactionType;
            var found = false;
            foreach (var reaction in reactions)
            {
                if (SocialReactions.AreSame(reaction.Key, selected))
                {
                    found = true;
                    break;
                }
            }
            if (!found) selected = reactions[0].Key;

            SelectReaction(selected);
        }

        private void BuildReactionFilters(IReadOnlyList<SocialReactionCount> reactions)
        {
            var shown = reactions ?? new SocialReactionCount[0];
            for (var i = 0; i < shown.Count; i++)
            {
                if (i >= _reactionFilters.Count)
                {
                    var chip = new SocialReactionChip();
                    chip.Button.IsHitTestVisible = true;
                    chip.Button.IsTabStop = true;
                    chip.Button.Click += ReactionFilter_Click;
                    chip.SetAccessible(true);
                    _reactionFilters.Add(chip);
                    ReactionFilters.Children.Add(chip.Button);
                }

                _reactionFilters[i].Show(_focused, shown[i]);
                AutomationProperties.SetName(_reactionFilters[i].Button,
                                             LocalizedStrings.Format("SocialThreadReactionFilterFormat",
                                                                     SocialReactions.SpokenName(shown[i].Key),
                                                                     CountText.Format(shown[i].Count)));
            }

            for (var i = shown.Count; i < _reactionFilters.Count; i++)
            {
                _reactionFilters[i].Hide();
            }
        }

        private void ReactionFilter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var chip in _reactionFilters)
                {
                    if (ReferenceEquals(chip.Button, sender) && chip.Key != null)
                    {
                        SelectReaction(chip.Key);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not filter the reactions - {ex.Message}");
            }
        }

        private void SelectReaction(string type)
        {
            _reactionType = type;

            foreach (var chip in _reactionFilters)
            {
                chip.Button.IsChecked = chip.Key != null && SocialReactions.AreSame(chip.Key, type);
            }

            ReactionFeed feed;
            if (!_reactionFeeds.TryGetValue(type, out feed))
            {
                var key = type;
                feed = new ReactionFeed(new SocialFeedCollection<SocialNotePersonItem>(
                    (untilId, token) => LoadMoreReactionsAsync(key, untilId, token), SocialFeedPaging.NonEmpty));
                _reactionFeeds[type] = feed;
            }

            SetSource(feed.Collection, "SocialNotePersonTemplate", "SocialThreadReactionsListName");

            if (!feed.Requested)
            {
                var key = type;
                LoadPeople(feed, SocialThreadTab.Reactions, feed.Collection, token => LoadReactionPageAsync(key, null, token));
            }
            else
            {
                SetState(feed.State);
            }
        }

        private async Task<IReadOnlyList<SocialNotePersonItem>> LoadMoreReactionsAsync(string type, string untilId, CancellationToken cancellationToken)
        {
            if (_released || _args == null) return null;

            return await LoadReactionPageAsync(type, untilId, cancellationToken);
        }

        private async Task<IReadOnlyList<SocialNotePersonItem>> LoadReactionPageAsync(string type, string untilId, CancellationToken cancellationToken)
        {
            var result = await SocialContentService.FetchReactionsAsync(_args.NoteId, SocialReactions.ToRequestKey(type), untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            var reactionEmojis = _focused == null ? SocialJson.EmptyMap : _focused.Note.ReactionEmojis;
            var localEmojis = _focused == null ? SocialContentService.CachedEmojiMap : _focused.LocalEmojis;

            var items = new List<SocialNotePersonItem>();
            foreach (var entry in result.Value)
            {
                var item = SocialNotePersonItem.FromReaction(entry, reactionEmojis, localEmojis);
                if (item != null) items.Add(item);
            }

            return items;
        }

        private async void LoadPeople(TabFeed feed, SocialThreadTab tab, SocialFeedCollection<SocialNotePersonItem> collection,
                                      Func<CancellationToken, Task<IReadOnlyList<SocialNotePersonItem>>> load)
        {
            var version = ++feed.Version;
            feed.Requested = true;
            SetFeedState(feed, tab, ListState.Loading);

            try
            {
                var items = await load(_cancellation.Token);
                if (version != feed.Version || IsReleased) return;

                if (items == null)
                {
                    SetFeedState(feed, tab, ListState.Failed);
                    return;
                }

                collection.ReplaceAll(items, items.Count >= AppConstants.SocialReactionsTabPageSize);
                SetFeedState(feed, tab, items.Count == 0 ? ListState.Empty : ListState.Ready);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialNotePage: list load cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: list load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == feed.Version && !IsReleased) SetFeedState(feed, tab, ListState.Failed);
            }
        }

        private void SetFeedState(TabFeed feed, SocialThreadTab tab, ListState state)
        {
            feed.State = state;
            if (tab != _tab) return;

            var reaction = feed as ReactionFeed;
            if (reaction != null && !ReferenceEquals(NoteList.ItemsSource, reaction.Collection)) return;

            SetState(state);
        }

        private void SetState(ListState state)
        {
            TabLoadingRing.IsActive = state == ListState.Loading;
            TabLoadingRing.Visibility = state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state == ListState.Empty || state == ListState.Failed;
            TabStatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            FooterHost.Visibility = state == ListState.Ready ? Visibility.Collapsed : Visibility.Visible;
            if (!showPanel) return;

            if (state == ListState.Failed)
            {
                TabStateGlyph.Glyph = "";
                TabStateText.Text = LocalizedStrings.Get("SocialThreadTabFailed");
                TabStateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                TabStateButton.Visibility = Visibility.Visible;
            }
            else
            {
                TabStateGlyph.Glyph = _tab == SocialThreadTab.Replies ? "" : _tab == SocialThreadTab.Renotes ? "" : "";
                TabStateText.Text = LocalizedStrings.Get(_tab == SocialThreadTab.Replies ? "SocialThreadRepliesEmpty"
                                                         : _tab == SocialThreadTab.Renotes ? "SocialThreadRenotesEmpty"
                                                         : "SocialThreadReactionsEmpty");
                TabStateButton.Visibility = Visibility.Collapsed;
            }

            AutomationHelper.AnnounceLiveRegion(TabStateText);
        }

        private void TabStateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_released || _args == null) return;

                switch (_tab)
                {
                    case SocialThreadTab.Replies:
                        LoadReplies();
                        break;
                    case SocialThreadTab.Renotes:
                        LoadRenotes();
                        break;
                    case SocialThreadTab.Reactions:
                        ReactionFeed feed;
                        if (_reactionType != null && _reactionFeeds.TryGetValue(_reactionType, out feed))
                        {
                            feed.Requested = false;
                            SelectReaction(_reactionType);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: retry failed - {ex.Message}");
            }
        }

        private async void NoteList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var person = e.ClickedItem as SocialNotePersonItem;
                if (person != null)
                {
                    await SocialWindows.ShowUserAsync(person.User);
                    return;
                }

                var item = e.ClickedItem as SocialNoteItem;
                if (item == null) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                if (MfmInlineBuilder.WasLinkJustInvoked || _released) return;

                if (!SocialThreads.Open(this, item.Note, item.Note.Id, false, SocialThreadTab.Replies))
                {
                    await WebLauncher.LaunchAsync(item.NoteUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not open the item - {ex.Message}");
            }
        }

        private async void SocialNoteService_Changed(object sender, SocialNoteChange change)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_released || change == null) return;

                        if (change.Kind == SocialNoteChangeKind.Reset)
                        {
                            LeaveIfAccountChanged();
                        }
                        else if (change.Kind == SocialNoteChangeKind.Replied)
                        {
                            InsertReply(change.NoteId, change.ChildId, change.Note);
                        }
                        else if (_args != null && string.Equals(change.NoteId, _args.NoteId, StringComparison.Ordinal))
                        {
                            ApplyFocusedChange(change);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialNotePage: could not show a note change - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: note change handler failed - {ex.Message}");
            }
        }

        private void ApplyFocusedChange(SocialNoteChange change)
        {
            switch (change.Kind)
            {
                case SocialNoteChangeKind.Deleted:
                    ReplyBoxSlot.Visibility = Visibility.Collapsed;
                    if (change.IsMine && Frame != null && Frame.CanGoBack) Frame.GoBack();
                    break;
                case SocialNoteChangeKind.Renoted:
                case SocialNoteChangeKind.Unrenoted:
                    _renotesFeed.Requested = false;
                    if (_tab == SocialThreadTab.Renotes) LoadRenotes();
                    break;
                case SocialNoteChangeKind.Reacted:
                case SocialNoteChangeKind.Unreacted:
                    foreach (var feed in _reactionFeeds.Values)
                    {
                        feed.Requested = false;
                    }
                    if (_tab == SocialThreadTab.Reactions) ShowReactions();
                    break;
            }
        }

        private async void InsertReply(string parentId, string childId, SocialNote known)
        {
            var claimed = false;
            try
            {
                if (_args == null || string.IsNullOrEmpty(childId) || !_repliesFeed.Requested) return;
                if (IndexOfReply(childId) >= 0 || _pendingReplies.Contains(childId)) return;

                var mine = known != null && SocialContentService.IsCurrentAccount(known.UserId);
                var toFocused = string.Equals(parentId, _args.NoteId, StringComparison.Ordinal);
                if (!toFocused)
                {
                    var parentIndex = IndexOfReply(parentId);
                    if (parentIndex < 0 || _replies[parentIndex].Depth >= MaxReplyDepth) return;
                }
                else if (_replies.HasMoreItems && !mine)
                {
                    return;
                }

                _pendingReplies.Add(childId);
                claimed = true;

                var note = known;
                if (note == null)
                {
                    var result = await SocialContentService.FetchNoteAsync(childId, _cancellation.Token);
                    if (IsReleased || result.Status != SocialApiStatus.Ok) return;
                    note = result.Value;
                }

                if (IsReleased || IndexOfReply(childId) >= 0) return;

                SocialNoteItem item;
                int index;
                if (toFocused)
                {
                    item = SocialNoteItem.Create(note, SocialContentService.CachedEmojiMap, false, 0, null);
                    index = _replies.HasMoreItems ? 0 : _replies.Count;
                }
                else
                {
                    var parentIndex = IndexOfReply(parentId);
                    if (parentIndex < 0) return;

                    var parent = _replies[parentIndex];
                    if (parent.Depth >= MaxReplyDepth) return;

                    item = SocialNoteItem.Create(note, SocialContentService.CachedEmojiMap, false, parent.Depth + 1, parent.PagingId);
                    index = parentIndex + 1;
                    while (index < _replies.Count && _replies[index].Depth > parent.Depth) index++;
                }

                if (item == null) return;

                _replies.Insert(index, item);
                if (_repliesFeed.State == ListState.Empty) SetFeedState(_repliesFeed, SocialThreadTab.Replies, ListState.Ready);
                UpdateWatch();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialNotePage: live reply cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNotePage: could not insert a live reply - {ex.Message}");
            }
            finally
            {
                if (claimed) _pendingReplies.Remove(childId);
            }
        }

        private int IndexOfReply(string noteId)
        {
            for (var i = 0; i < _replies.Count; i++)
            {
                if (string.Equals(_replies[i].Note.Id, noteId, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        private void UpdateWatch()
        {
            if (_released || _args == null) return;

            var ids = new List<string> { _args.NoteId };
            foreach (var ancestor in _ancestors)
            {
                ids.Add(ancestor.Note.Id);
            }

            for (var i = 0; i < _replies.Count && ids.Count < AppConstants.SocialNoteCaptureLimit; i++)
            {
                ids.Add(_replies[i].Note.Id);
            }

            if (_watch == null) _watch = SocialNoteCapture.Watch();
            _watch.Set(ids);
        }

        private sealed class ReplyNode
        {
            public ReplyNode(SocialNote note, int depth)
            {
                Note = note;
                Depth = depth;
                Children = new List<ReplyNode>();
            }

            public SocialNote Note { get; private set; }

            public int Depth { get; private set; }

            public List<ReplyNode> Children { get; private set; }

            public int More { get; set; }

            public bool Deeper { get; set; }
        }

        private sealed class ReplyPage
        {
            public ReplyPage(IReadOnlyList<SocialNoteItem> items, int topLevelCount)
            {
                Items = items;
                TopLevelCount = topLevelCount;
            }

            public IReadOnlyList<SocialNoteItem> Items { get; private set; }

            public int TopLevelCount { get; private set; }
        }

        private class TabFeed
        {
            public bool Requested { get; set; }

            public int Version { get; set; }

            public ListState State { get; set; }
        }

        private sealed class ReactionFeed : TabFeed
        {
            public ReactionFeed(SocialFeedCollection<SocialNotePersonItem> collection)
            {
                Collection = collection;
            }

            public SocialFeedCollection<SocialNotePersonItem> Collection { get; private set; }
        }
    }
}
