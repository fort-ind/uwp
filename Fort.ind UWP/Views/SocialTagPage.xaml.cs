using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialTagPage : Page, IReleasablePage, IReopenablePage, ITrimmablePage
    {
        private enum ListState
        {
            Loading,
            Ready,
            Empty,
            Failed
        }

        private readonly SocialFeedCollection<SocialNoteItem> _notes;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private SocialTagArgs _args;

        private string _loadedAccountId;

        private ListState _state = ListState.Loading;

        private int _version;

        private bool _noteHandlerAttached;

        private bool _released;

        private bool IsReleased => _released;

        public SocialTagPage()
        {
            this.InitializeComponent();

            _notes = new SocialFeedCollection<SocialNoteItem>(LoadMoreAsync, SocialFeedPaging.NonEmpty, true);
            NoteList.ItemsSource = _notes;
        }

        internal bool Shows(string tag)
        {
            return _args != null && string.Equals(_args.Tag, tag, StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                var args = e.Parameter as SocialTagArgs;
                if (args == null) return;

                _args = args;
                var title = LocalizedStrings.Format("SocialTagTitleFormat", args.Tag);
                CaptionText.Text = LocalizedStrings.Format("SocialTagCaptionFormat", args.Tag);
                AutomationProperties.SetName(NoteList, title);
                Load();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: could not show the tag", ex);
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
                SocialThreads.ReturnToProfile(this, parameter);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: could not return to the profile", ex);
            }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            DetachNoteHandler();

            try
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: could not cancel pending loads", ex);
            }

            _notes.ReplaceAll(new SocialNoteItem[0], false);
        }

        public void Trim()
        {
            if (_released) return;

            _notes.TrimToFirstPage();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_released) return;

                if (!_noteHandlerAttached)
                {
                    SocialNoteService.Changed += SocialNoteService_Changed;
                    _noteHandlerAttached = true;
                }

                RemoveRows(item => item.IsGone || SocialNoteItem.IsHiddenPerson(item, null));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: could not watch note changes", ex);
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

        private async void SocialNoteService_Changed(object sender, SocialNoteChange change)
        {
            try
            {
                if (change == null) return;

                switch (change.Kind)
                {
                    case SocialNoteChangeKind.Reset:
                    case SocialNoteChangeKind.Deleted:
                    case SocialNoteChangeKind.Unrenoted:
                        break;
                    case SocialNoteChangeKind.RelationChanged:
                        if (!change.Flag) return;
                        break;
                    default:
                        return;
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_released) return;

                        if (change.Kind == SocialNoteChangeKind.Reset)
                        {
                            if (!string.Equals(_loadedAccountId, SocialContentService.CurrentAccountId(), StringComparison.Ordinal)) Load();
                        }
                        else if (change.Kind == SocialNoteChangeKind.RelationChanged)
                        {
                            RemoveRows(item => SocialNoteItem.IsHiddenPerson(item, null));
                        }
                        else
                        {
                            RemoveRows(item => SocialNoteItem.IsRemovedBy(item, change));
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialTagPage: could not apply a note change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: note change handler failed", ex);
            }
        }

        private void RemoveRows(Func<SocialNoteItem, bool> predicate)
        {
            if (_notes.RemoveWhere(predicate) == 0) return;
            if (_notes.Count > 0 || _state != ListState.Ready) return;

            if (_notes.HasMoreItems)
            {
                Load();
            }
            else
            {
                SetState(ListState.Empty);
            }
        }

        private async void Load()
        {
            if (_args == null || _released) return;

            var version = ++_version;
            _loadedAccountId = SocialContentService.CurrentAccountId();
            SetState(ListState.Loading);

            try
            {
                var emojiTask = SocialContentService.GetEmojiMapAsync();
                var result = await SocialContentService.FetchTagNotesAsync(_args.Tag, null, _cancellation.Token);
                var emojis = await emojiTask;
                if (version != _version || IsReleased) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetState(ListState.Failed);
                    return;
                }

                var items = SocialNoteItem.CreateAll(result.Value, emojis, false);
                _notes.ReplaceAll(items, result.Value.Count > 0);
                SetState(items.Count == 0 ? ListState.Empty : ListState.Ready);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialTagPage: tag load cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: tag load failed", ex);
                if (version == _version && !IsReleased) SetState(ListState.Failed);
            }
        }

        private async Task<IReadOnlyList<SocialNoteItem>> LoadMoreAsync(string untilId, CancellationToken cancellationToken)
        {
            if (_args == null || _released) return null;

            var result = await SocialContentService.FetchTagNotesAsync(_args.Tag, untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return SocialNoteItem.CreateAll(result.Value, SocialContentService.CachedEmojiMap, false);
        }

        private void SetState(ListState state)
        {
            _state = state;

            LoadingRing.IsActive = state == ListState.Loading;
            LoadingRing.Visibility = state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state == ListState.Empty || state == ListState.Failed;
            StatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            FooterHost.Visibility = state == ListState.Ready ? Visibility.Collapsed : Visibility.Visible;
            if (!showPanel) return;

            if (state == ListState.Empty)
            {
                StateGlyph.Glyph = "";
                StateText.Text = LocalizedStrings.Format("SocialTagEmptyFormat", _args == null ? "" : _args.Tag);
                StateButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                StateGlyph.Glyph = "";
                StateText.Text = LocalizedStrings.Get("SocialTagFailed");
                StateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                StateButton.Visibility = Visibility.Visible;
            }

            AutomationHelper.AnnounceLiveRegion(StateText);
        }

        private void StateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_state == ListState.Failed) Load();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: retry failed", ex);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTagPage: refresh failed", ex);
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
                AppLog.Error("SocialTagPage: could not open the note", ex);
            }
        }
    }
}
