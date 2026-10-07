using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    public enum SocialFeedState
    {
        Loading,
        Ready,
        Empty,
        Failed,
        NeedsSignIn
    }

    public sealed class SocialProfileFeeds
    {
        private static readonly IReadOnlyList<SocialNote> NoNotes = new SocialNote[0];

        private readonly Feed[] _feeds;

        private CancellationTokenSource _cancellation = new CancellationTokenSource();

        private bool _released;

        private int _favoritesVersion;

        public SocialProfileFeeds()
        {
            _feeds = new[]
            {
                new Feed(this, SocialUserNotesTab.Notes),
                new Feed(this, SocialUserNotesTab.Replies),
                new Feed(this, SocialUserNotesTab.Media),
                new Feed(this, SocialUserNotesTab.Favorites)
            };

            Current = SocialUserNotesTab.Notes;
            PinnedNotes = NoNotes;
        }

        public event EventHandler<SocialUserNotesTab> StateChanged;

        public string UserId { get; private set; }

        public SocialUserNotesTab Current { get; private set; }

        public IReadOnlyList<SocialNote> PinnedNotes { get; set; }

        public IReadOnlyDictionary<string, Uri> Emojis { get; set; }

        public CancellationToken Token
        {
            get { return _cancellation.Token; }
        }

        public SocialFeedState CurrentState
        {
            get { return StateOf(Current); }
        }

        public SocialFeedCollection<SocialNoteItem> CollectionOf(SocialUserNotesTab tab)
        {
            return _feeds[(int)tab].Collection;
        }

        public SocialFeedState StateOf(SocialUserNotesTab tab)
        {
            return _feeds[(int)tab].State;
        }

        public void Reset(string userId)
        {
            if (_released) return;

            try
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileFeeds: could not cancel the previous loads - {ex.Message}");
            }
            _cancellation = new CancellationTokenSource();

            foreach (var feed in _feeds)
            {
                feed.Version++;
                feed.Requested = false;
                feed.State = SocialFeedState.Loading;
                feed.Collection.ReplaceAll(new SocialNoteItem[0], false);
            }

            UserId = userId;
            Current = SocialUserNotesTab.Notes;
            PinnedNotes = NoNotes;
        }

        public void Select(SocialUserNotesTab tab)
        {
            if (_released || string.IsNullOrEmpty(UserId)) return;

            if (tab != Current) CollectionOf(Current).TrimToFirstPage();

            Current = tab;
            var feed = _feeds[(int)tab];
            if (!feed.Requested)
            {
                Load(feed);
            }
            else
            {
                Raise(tab);
            }
        }

        public void BeginLoading(SocialUserNotesTab tab)
        {
            var feed = _feeds[(int)tab];
            feed.Requested = true;
            SetState(feed, SocialFeedState.Loading);
        }

        public void ApplyFirstPage(SocialUserNotesTab tab, SocialApiResult<IReadOnlyList<SocialNote>> result)
        {
            if (_released) return;

            var feed = _feeds[(int)tab];
            feed.Version++;
            Apply(feed, result);
        }

        public void Fail(SocialUserNotesTab tab)
        {
            var feed = _feeds[(int)tab];
            feed.Requested = true;
            SetState(feed, SocialFeedState.Failed);
        }

        public void Retry()
        {
            if (_released || string.IsNullOrEmpty(UserId)) return;

            Load(_feeds[(int)Current]);
        }

        public void Refresh()
        {
            if (_released) return;

            foreach (var feed in _feeds)
            {
                feed.Version++;
                feed.Requested = feed.Tab == Current;
            }

            var current = _feeds[(int)Current];
            current.Collection.ReplaceAll(new SocialNoteItem[0], false);
            SetState(current, SocialFeedState.Loading);
        }

        public void Invalidate(SocialUserNotesTab tab)
        {
            if (_released) return;

            var feed = _feeds[(int)tab];
            if (tab == Current && !string.IsNullOrEmpty(UserId))
            {
                Load(feed);
            }
            else
            {
                feed.Version++;
                feed.Requested = false;
            }
        }

        public bool FavoritesChangedSinceLoad
        {
            get { return _feeds[(int)SocialUserNotesTab.Favorites].Requested && _favoritesVersion != SocialNoteService.FavoritesVersion; }
        }

        public void ApplyChange(SocialNoteChange change)
        {
            if (_released || change == null) return;

            if (change.Kind == SocialNoteChangeKind.Posted)
            {
                InsertPosted(change.Note);
                return;
            }

            if (change.Kind == SocialNoteChangeKind.FavoriteChanged)
            {
                var favorites = _feeds[(int)SocialUserNotesTab.Favorites];
                if (!favorites.Requested) return;

                if (change.Flag)
                {
                    Invalidate(SocialUserNotesTab.Favorites);
                    return;
                }

                _favoritesVersion = SocialNoteService.FavoritesVersion;
                var noteId = change.NoteId;
                if (favorites.Collection.RemoveWhere(item => string.Equals(item.Note.Id, noteId, StringComparison.Ordinal)) > 0)
                {
                    SettleAfterRemoval(favorites);
                }
                return;
            }

            foreach (var feed in _feeds)
            {
                var removed = feed.Collection.RemoveWhere(item => SocialNoteItem.IsRemovedBy(item, change));
                if (removed > 0) SettleAfterRemoval(feed);
            }
        }

        private void InsertPosted(SocialNote note)
        {
            if (note == null || string.IsNullOrEmpty(UserId) || !string.Equals(note.UserId, UserId, StringComparison.Ordinal)) return;

            var reply = !string.IsNullOrEmpty(note.ReplyId);
            var emojis = Emojis ?? SocialContentService.CachedEmojiMap;

            foreach (var feed in _feeds.Where(feed => feed.Requested && !feed.Collection.HasDroppedHead
                                                      && (feed.State == SocialFeedState.Ready || feed.State == SocialFeedState.Empty)))
            {
                bool include;
                switch (feed.Tab)
                {
                    case SocialUserNotesTab.Notes:
                        include = !reply;
                        break;
                    case SocialUserNotesTab.Replies:
                        include = true;
                        break;
                    case SocialUserNotesTab.Media:
                        include = !reply && note.Files.Count > 0;
                        break;
                    default:
                        include = false;
                        break;
                }

                if (!include) continue;

                var exists = false;
                foreach (var existing in feed.Collection)
                {
                    if (string.Equals(existing.Note.Id, note.Id, StringComparison.Ordinal) && !existing.IsPinned)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;

                var item = SocialNoteItem.Create(note, emojis, false);
                if (item == null) continue;

                var index = 0;
                while (index < feed.Collection.Count && feed.Collection[index].IsPinned) index++;
                feed.Collection.Insert(index, item);
                if (feed.State == SocialFeedState.Empty) SetState(feed, SocialFeedState.Ready);
            }
        }

        public void Sweep()
        {
            if (_released) return;

            foreach (var feed in _feeds)
            {
                var removed = feed.Collection.RemoveWhere(item => item.IsGone);
                if (removed > 0) SettleAfterRemoval(feed);
            }
        }

        private void SettleAfterRemoval(Feed feed)
        {
            if (feed.Collection.Count > 0 || feed.State != SocialFeedState.Ready) return;

            if (feed.Collection.HasMoreItems)
            {
                Invalidate(feed.Tab);
            }
            else
            {
                SetState(feed, SocialFeedState.Empty);
            }
        }

        public void TrimAll()
        {
            if (_released) return;

            foreach (var feed in _feeds)
            {
                feed.Collection.TrimToFirstPage();
            }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            try
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileFeeds: could not cancel pending loads - {ex.Message}");
            }

            foreach (var feed in _feeds)
            {
                feed.Collection.ReplaceAll(new SocialNoteItem[0], false);
            }
        }

        private async void Load(Feed feed)
        {
            var version = ++feed.Version;
            feed.Requested = true;

            if (feed.Tab == SocialUserNotesTab.Favorites)
            {
                LoadFavorites(feed, version);
                return;
            }

            SetState(feed, SocialFeedState.Loading);

            try
            {
                var result = await SocialContentService.FetchUserNotesAsync(UserId, feed.Tab, null, Token);
                if (version != feed.Version || _released) return;

                Apply(feed, result);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialProfileFeeds: notes load cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileFeeds: notes load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == feed.Version && !_released) SetState(feed, SocialFeedState.Failed);
            }
        }

        private async void LoadFavorites(Feed feed, int version)
        {
            try
            {
                if (!SocialPermissions.Has(SocialPermissions.ReadFavorites))
                {
                    SetState(feed, SocialFeedState.NeedsSignIn);
                    return;
                }

                SetState(feed, SocialFeedState.Loading);

                var favoritesVersion = SocialNoteService.FavoritesVersion;
                var result = await SocialContentService.FetchFavoritesAsync(null, Token);
                if (version != feed.Version || _released) return;

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SocialPermissions.Forget(SocialPermissions.ReadFavorites);
                    SetState(feed, SocialFeedState.NeedsSignIn);
                    return;
                }

                if (result.Status != SocialApiStatus.Ok)
                {
                    SetState(feed, SocialFeedState.Failed);
                    return;
                }

                _favoritesVersion = favoritesVersion;
                var items = FavoriteItems(result.Value);
                feed.Collection.ReplaceAll(items, result.Value.Count > 0);
                SetState(feed, items.Count == 0 ? SocialFeedState.Empty : SocialFeedState.Ready);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialProfileFeeds: favorites load cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileFeeds: favorites load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == feed.Version && !_released) SetState(feed, SocialFeedState.Failed);
            }
        }

        private List<SocialNoteItem> FavoriteItems(IReadOnlyList<SocialFavoriteEntry> entries)
        {
            var emojis = Emojis ?? SocialContentService.CachedEmojiMap;
            var items = new List<SocialNoteItem>();
            foreach (var entry in entries)
            {
                var item = SocialNoteItem.Create(entry.Note, emojis, false, 0, entry.Id);
                if (item != null) items.Add(item);
            }

            return items;
        }

        private void Apply(Feed feed, SocialApiResult<IReadOnlyList<SocialNote>> result)
        {
            feed.Requested = true;

            if (result == null || result.Status != SocialApiStatus.Ok)
            {
                SetState(feed, SocialFeedState.Failed);
                return;
            }

            var emojis = Emojis ?? SocialContentService.CachedEmojiMap;
            var items = new List<SocialNoteItem>();
            if (feed.Tab == SocialUserNotesTab.Notes)
            {
                items.AddRange(SocialNoteItem.CreateAll(PinnedNotes, emojis, true));
            }
            items.AddRange(SocialNoteItem.CreateAll(result.Value, emojis, false));

            feed.Collection.ReplaceAll(items, result.Value.Count > 0);
            SetState(feed, items.Count == 0 ? SocialFeedState.Empty : SocialFeedState.Ready);
        }

        private async Task<IReadOnlyList<SocialNoteItem>> LoadMoreAsync(SocialUserNotesTab tab, string untilId, CancellationToken cancellationToken)
        {
            var userId = UserId;
            if (_released || string.IsNullOrEmpty(userId)) return null;

            if (tab == SocialUserNotesTab.Favorites)
            {
                var favorites = await SocialContentService.FetchFavoritesAsync(untilId, cancellationToken);
                return favorites.Status == SocialApiStatus.Ok ? FavoriteItems(favorites.Value) : null;
            }

            var result = await SocialContentService.FetchUserNotesAsync(userId, tab, untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return SocialNoteItem.CreateAll(result.Value, Emojis ?? SocialContentService.CachedEmojiMap, false);
        }

        private void SetState(Feed feed, SocialFeedState state)
        {
            feed.State = state;
            Raise(feed.Tab);
        }

        private void Raise(SocialUserNotesTab tab)
        {
            var handler = StateChanged;
            if (handler != null) handler(this, tab);
        }

        private sealed class Feed
        {
            public Feed(SocialProfileFeeds owner, SocialUserNotesTab tab)
            {
                Tab = tab;
                Collection = new SocialFeedCollection<SocialNoteItem>(
                    (untilId, token) => owner.LoadMoreAsync(tab, untilId, token),
                    SocialFeedPaging.NonEmpty, true);
            }

            public SocialUserNotesTab Tab { get; private set; }

            public SocialFeedCollection<SocialNoteItem> Collection { get; private set; }

            public bool Requested { get; set; }

            public int Version { get; set; }

            public SocialFeedState State { get; set; }
        }
    }
}
