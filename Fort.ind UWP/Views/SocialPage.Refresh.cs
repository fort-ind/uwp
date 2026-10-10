using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxcInfoBar = Microsoft.UI.Xaml.Controls.InfoBar;
using MuxcInfoBarClosedEventArgs = Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs;
using MuxcRefreshContainer = Microsoft.UI.Xaml.Controls.RefreshContainer;
using MuxcRefreshRequestedEventArgs = Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage
    {
        private sealed class ListRefresh
        {
            public ListRefresh(MuxcRefreshContainer container, MuxcInfoBar failedBar, ListView list)
            {
                Container = container;
                FailedBar = failedBar;
                List = list;
            }

            public MuxcRefreshContainer Container { get; }

            public MuxcInfoBar FailedBar { get; }

            public ListView List { get; }

            public Func<Task<bool>> RefreshAsync { get; set; }

            public bool IsRefreshing { get; set; }
        }

        private ListRefresh[] _refreshes;

        private ListRefresh _notificationsRefresh;

        private ListRefresh _mentionsRefresh;

        private void InitializeRefreshes()
        {
            foreach (var feed in _timelines)
            {
                var timeline = feed;
                timeline.Refresh.RefreshAsync = () => LoadTimelineAsync(timeline, true);
            }

            _notificationsRefresh = new ListRefresh(NotificationsRefreshContainer, NotificationsRefreshFailedInfoBar, NotificationsList)
            {
                RefreshAsync = () => LoadNotificationsAsync(true)
            };
            _mentionsRefresh = new ListRefresh(MentionsRefreshContainer, MentionsRefreshFailedInfoBar, MentionsList)
            {
                RefreshAsync = () => LoadMentionsAsync(true)
            };

            _refreshes = _timelines.Select(feed => feed.Refresh)
                                   .Concat(new[] { _notificationsRefresh, _mentionsRefresh })
                                   .ToArray();
        }

        private ListRefresh SelectedRefresh()
        {
            var selected = SocialPivot.SelectedItem;
            var feed = TimelineFor(selected);
            if (feed != null) return feed.Refresh;

            if (selected != NotificationsPivotItem || IsSignedOut) return null;

            return _showingMentions ? _mentionsRefresh : _notificationsRefresh;
        }

        private bool TryRequestRefresh(ListRefresh refresh)
        {
            if (refresh == null || !refresh.Container.IsShown()) return false;

            if (!refresh.IsRefreshing) refresh.Container.RequestRefresh();
            return true;
        }

        private async void RefreshContainer_RefreshRequested(MuxcRefreshContainer sender, MuxcRefreshRequestedEventArgs args)
        {
            Deferral deferral = null;
            ListRefresh refresh = null;
            try
            {
                deferral = args.GetDeferral();
                refresh = _refreshes.FirstOrDefault(candidate => candidate.Container == sender);
                if (refresh == null || refresh.RefreshAsync == null) return;

                refresh.IsRefreshing = true;
                HideRefreshFailed(refresh.FailedBar);

                var refreshed = await refresh.RefreshAsync();
                if (refreshed)
                {
                    ScrollToTop(refresh.List);
                }
                else
                {
                    ShowRefreshFailed(refresh.FailedBar);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: pull to refresh failed", ex);
            }
            finally
            {
                if (refresh != null) refresh.IsRefreshing = false;
                CompleteDeferral(deferral);
            }
        }

        private static void CompleteDeferral(Deferral deferral)
        {
            if (deferral == null) return;

            try
            {
                deferral.Complete();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not complete the refresh", ex);
            }
        }

        private static void ScrollToTop(ListView list)
        {
            try
            {
                var scrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(list);
                if (scrollViewer != null) scrollViewer.ChangeView(null, 0, null, true);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not scroll to the newest", ex);
            }
        }

        private static void ShowRefreshFailed(MuxcInfoBar bar)
        {
            bar.Visibility = Visibility.Visible;
            bar.IsOpen = true;
        }

        private static void HideRefreshFailed(MuxcInfoBar bar)
        {
            bar.IsOpen = false;
            bar.Visibility = Visibility.Collapsed;
        }

        private void RefreshFailedInfoBar_Closed(MuxcInfoBar sender, MuxcInfoBarClosedEventArgs args)
        {
            sender.Visibility = Visibility.Collapsed;
        }

        private void RefreshFailedRetryButton_Click(object sender, RoutedEventArgs e)
        {
            var element = sender as DependencyObject;
            var refresh = _refreshes.FirstOrDefault(candidate => VisualTreeSearch.IsDescendantOf(element, candidate.FailedBar));
            if (refresh == null) return;

            HideRefreshFailed(refresh.FailedBar);
            TryRequestRefresh(refresh);
        }
    }
}
