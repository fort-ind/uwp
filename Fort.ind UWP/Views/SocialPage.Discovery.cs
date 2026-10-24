using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage
    {
        private int _tagsVersion;

        private int _announcementsVersion;

        private string _shownAnnouncementIds = string.Empty;

        private void TagsFlyout_Opening(object sender, object e)
        {
            LoadTags();
        }

        private void TagsRetryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTags();
        }

        private async void LoadTags()
        {
            try
            {
                var version = ++_tagsVersion;

                if (TagsList.ItemsSource == null)
                {
                    SetTagsState(true, false, null, false);
                }

                var result = await SocialDiscoveryService.GetPopularHashtagsAsync(CancellationToken.None);
                if (version != _tagsVersion) return;

                if (result.Status == SocialApiStatus.Ok && result.Value.Count > 0)
                {
                    TagsList.ItemsSource = result.Value;
                    SetTagsState(false, true, null, false);
                }
                else if (result.Status == SocialApiStatus.Ok)
                {
                    SetTagsState(false, false, LocalizedStrings.Get("SocialTagsEmpty"), false);
                }
                else if (TagsList.ItemsSource == null)
                {
                    SetTagsState(false, false, LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialTagsFailed"), true);
                }
                else
                {
                    SetTagsState(false, true, null, false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not load the popular hashtags", ex);
            }
        }

        private void SetTagsState(bool loading, bool ready, string message, bool canRetry)
        {
            TagsLoadingRing.IsActive = loading;
            TagsLoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            TagsList.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

            var showMessage = !loading && !ready;
            TagsStatePanel.Visibility = showMessage ? Visibility.Visible : Visibility.Collapsed;
            TagsStateText.Text = message ?? string.Empty;
            TagsRetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void TagsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var hashtag = e.ClickedItem as SocialHashtag;
                if (hashtag == null) return;

                TagsFlyout.Hide();

                if (!SocialThreads.OpenTag(this, hashtag.Tag))
                {
                    await WebLauncher.LaunchAsync(SocialLinks.TagUrl(hashtag.Tag));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not open the hashtag", ex);
            }
        }

        private async void LoadAnnouncements(bool force)
        {
            try
            {
                var version = ++_announcementsVersion;

                var announcements = await SocialDiscoveryService.GetUnreadAnnouncementsAsync(force, CancellationToken.None);
                if (version != _announcementsVersion) return;

                ShowAnnouncements(announcements);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not load the announcements", ex);
            }
        }

        private void ShowAnnouncements(IReadOnlyList<SocialAnnouncement> announcements)
        {
            var ids = string.Join(",", announcements.Select(announcement => announcement.Id));
            if (string.Equals(ids, _shownAnnouncementIds, StringComparison.Ordinal)) return;
            _shownAnnouncementIds = ids;

            AnnouncementsPanel.Children.Clear();
            foreach (var announcement in announcements)
            {
                var bar = new muxc.InfoBar
                {
                    Title = announcement.Title,
                    Message = announcement.Text,
                    Severity = SeverityOf(announcement),
                    IsClosable = true,
                    IsOpen = true,
                    Tag = announcement
                };
                bar.Closed += AnnouncementBar_Closed;
                AnnouncementsPanel.Children.Add(bar);
            }

            AnnouncementsPanel.Visibility = announcements.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static muxc.InfoBarSeverity SeverityOf(SocialAnnouncement announcement)
        {
            switch (announcement.Icon)
            {
                case "warning":
                    return muxc.InfoBarSeverity.Warning;
                case "error":
                    return muxc.InfoBarSeverity.Error;
                case "success":
                    return muxc.InfoBarSeverity.Success;
                default:
                    return muxc.InfoBarSeverity.Informational;
            }
        }

        private async void AnnouncementBar_Closed(muxc.InfoBar sender, muxc.InfoBarClosedEventArgs args)
        {
            try
            {
                var announcement = sender.Tag as SocialAnnouncement;
                sender.Closed -= AnnouncementBar_Closed;
                AnnouncementsPanel.Children.Remove(sender);
                if (AnnouncementsPanel.Children.Count == 0) AnnouncementsPanel.Visibility = Visibility.Collapsed;

                _shownAnnouncementIds = string.Join(",", AnnouncementsPanel.Children
                                                          .OfType<muxc.InfoBar>()
                                                          .Select(bar => ((SocialAnnouncement)bar.Tag).Id));

                if (announcement != null) await SocialDiscoveryService.DismissAnnouncementAsync(announcement);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not dismiss the announcement", ex);
            }
        }
    }
}
