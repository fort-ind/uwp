using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxcInfoBar = Microsoft.UI.Xaml.Controls.InfoBar;
using MuxcRefreshContainer = Microsoft.UI.Xaml.Controls.RefreshContainer;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage
    {
        private const string LocalTimelineDisabledCode = "LTL_DISABLED";

        private const string GlobalTimelineDisabledCode = "GTL_DISABLED";

        private sealed class TimelineFeed
        {
            public TimelineFeed(SocialTimeline timeline, PivotItem pivotItem, ListRefresh refresh, ProgressRing ring,
                                StackPanel panel, FontIcon glyph, TextBlock text, Button button,
                                string emptyKey, string failedKey)
            {
                Timeline = timeline;
                PivotItem = pivotItem;
                Refresh = refresh;
                List = refresh.List;
                Ring = ring;
                Panel = panel;
                Glyph = glyph;
                Text = text;
                Button = button;
                EmptyKey = emptyKey;
                FailedKey = failedKey;
            }

            public SocialTimeline Timeline { get; }

            public PivotItem PivotItem { get; }

            public ListRefresh Refresh { get; }

            public ListView List { get; }

            public ProgressRing Ring { get; }

            public StackPanel Panel { get; }

            public FontIcon Glyph { get; }

            public TextBlock Text { get; }

            public Button Button { get; }

            public string EmptyKey { get; }

            public string FailedKey { get; }

            public SocialFeedCollection<SocialNoteItem> Items { get; set; }

            public int Version { get; set; }

            public bool Requested { get; set; }

            public FeedState State { get; set; } = FeedState.Loading;

            public bool CanTakePosts
            {
                get { return Requested && (State == FeedState.Ready || State == FeedState.Empty); }
            }
        }

        private TimelineFeed CreateTimeline(SocialTimeline timeline, PivotItem pivotItem, ListView list,
                                            MuxcRefreshContainer container, MuxcInfoBar failedBar, ProgressRing ring,
                                            StackPanel panel, FontIcon glyph, TextBlock text, Button button,
                                            string emptyKey, string failedKey)
        {
            var refresh = new ListRefresh(container, failedBar, list);
            var feed = new TimelineFeed(timeline, pivotItem, refresh, ring, panel, glyph, text, button, emptyKey, failedKey);
            feed.Items = new SocialFeedCollection<SocialNoteItem>(
                (untilId, cancellationToken) => LoadMoreTimelineAsync(feed, untilId, cancellationToken),
                SocialFeedPaging.NonEmpty, true);
            list.ItemsSource = feed.Items;
            return feed;
        }

        private TimelineFeed TimelineFor(object pivotItem)
        {
            return _timelines.FirstOrDefault(feed => feed.PivotItem == pivotItem);
        }

        private void LoadTimeline(TimelineFeed feed)
        {
            var ignored = LoadTimelineAsync(feed, false);
        }

        private async Task<bool> LoadTimelineAsync(TimelineFeed feed, bool keepList)
        {
            var version = ++feed.Version;
            feed.Requested = true;
            if (!keepList) SetTimelineState(feed, FeedState.Loading);

            try
            {
                var emojiTask = SocialContentService.GetEmojiMapAsync();
                var result = await SocialContentService.FetchTimelineAsync(feed.Timeline, null, CancellationToken.None);
                var emojis = await emojiTask;
                if (version != feed.Version) return true;

                if (result.Status != SocialApiStatus.Ok)
                {
                    var failure = FailureStateOf(result);
                    if (keepList && failure == FeedState.Failed) return false;

                    SetTimelineState(feed, failure);
                    return true;
                }

                var items = SocialNoteItem.CreateAll(result.Value, emojis, false);
                feed.Items.ReplaceAll(items, result.Value.Count > 0);
                SetTimelineState(feed, items.Count == 0 ? FeedState.Empty : FeedState.Ready);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"SocialPage: {feed.Timeline} timeline load failed", ex);
                if (version != feed.Version) return true;
                if (keepList) return false;

                SetTimelineState(feed, FeedState.Failed);
                return true;
            }
        }

        private static FeedState FailureStateOf(SocialApiResult<IReadOnlyList<SocialNote>> result)
        {
            if (result.Status == SocialApiStatus.PermissionDenied) return FeedState.NeedsPermission;

            if (string.Equals(result.ErrorCode, LocalTimelineDisabledCode, StringComparison.Ordinal)
                || string.Equals(result.ErrorCode, GlobalTimelineDisabledCode, StringComparison.Ordinal))
            {
                return FeedState.Unavailable;
            }

            return FeedState.Failed;
        }

        private static async Task<IReadOnlyList<SocialNoteItem>> LoadMoreTimelineAsync(TimelineFeed feed, string untilId,
                                                                                       CancellationToken cancellationToken)
        {
            var result = await SocialContentService.FetchTimelineAsync(feed.Timeline, untilId, cancellationToken);
            if (result.Status != SocialApiStatus.Ok) return null;

            return SocialNoteItem.CreateAll(result.Value, SocialContentService.CachedEmojiMap, false);
        }

        private void SetTimelineState(TimelineFeed feed, FeedState state)
        {
            feed.State = state;
            ApplyFeedState(state, feed.Refresh, feed.Ring, feed.Panel, feed.Glyph, feed.Text, feed.Button,
                           feed.EmptyKey, feed.FailedKey);
        }

        private void BackToNewestButton_Click(object sender, RoutedEventArgs e)
        {
            var feed = _timelines.FirstOrDefault(candidate => ReferenceEquals(candidate.List.Header, sender));
            if (feed != null)
            {
                if (!TryRequestRefresh(feed.Refresh)) LoadTimeline(feed);
            }
            else if (ReferenceEquals(sender, NotificationsBackToNewestButton))
            {
                if (!TryRequestRefresh(_notificationsRefresh)) LoadNotifications();
            }
            else if (ReferenceEquals(sender, MentionsBackToNewestButton))
            {
                if (!TryRequestRefresh(_mentionsRefresh)) LoadMentions();
            }
        }

        private void TimelineStateButton_Click(object sender, RoutedEventArgs e)
        {
            var feed = _timelines.FirstOrDefault(candidate => candidate.Button == sender);
            if (feed == null) return;

            if (feed.State == FeedState.NeedsPermission)
            {
                OpenSignIn();
            }
            else
            {
                LoadTimeline(feed);
            }
        }

        private void InsertPosted(SocialNote note)
        {
            if (note == null || !string.IsNullOrEmpty(note.ReplyId)) return;
            if (!SocialContentService.IsCurrentAccount(note.UserId)) return;

            InsertPostedInto(_forYou, note);

            if (string.Equals(note.Visibility, SocialNoteActionService.PublicVisibility, StringComparison.Ordinal))
            {
                InsertPostedInto(_local, note);
                InsertPostedInto(_global, note);
            }
        }

        private void InsertPostedInto(TimelineFeed feed, SocialNote note)
        {
            if (!feed.CanTakePosts || feed.Items.HasDroppedHead) return;
            if (feed.Items.Any(existing => string.Equals(existing.Note.Id, note.Id, StringComparison.Ordinal))) return;

            var item = SocialNoteItem.Create(note, SocialContentService.CachedEmojiMap, false);
            if (item == null) return;

            feed.Items.Insert(0, item);
            if (feed.State != FeedState.Ready) SetTimelineState(feed, FeedState.Ready);
        }

        private void RemoveTimelineRows(Func<SocialNoteItem, bool> predicate)
        {
            foreach (var feed in _timelines)
            {
                var removed = feed.Items.RemoveWhere(predicate);
                if (removed == 0) continue;

                if (feed.Items.Count > 0 || feed.State != FeedState.Ready) continue;

                if (feed.Items.HasMoreItems)
                {
                    LoadTimeline(feed);
                }
                else
                {
                    SetTimelineState(feed, FeedState.Empty);
                }
            }
        }
    }
}
