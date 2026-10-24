using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    public static class SocialDiscoveryService
    {
        private static readonly object s_lock = new object();

        private static readonly TimeSpan HashtagLifetime = TimeSpan.FromMinutes(AppConstants.SocialHashtagCacheMinutes);

        private static readonly TimeSpan AnnouncementLifetime = TimeSpan.FromMinutes(AppConstants.SocialAnnouncementCacheMinutes);

        private static readonly TimeSpan AnnouncementRefreshAge = TimeSpan.FromSeconds(AppConstants.SocialAnnouncementRefreshSeconds);

        private static IReadOnlyList<SocialHashtag> s_hashtags;

        private static DateTimeOffset s_hashtagsAt;

        private static IReadOnlyList<SocialAnnouncement> s_announcements;

        private static DateTimeOffset s_announcementsAt;

        private static string s_announcementsAccount;

        public static async Task<SocialApiResult<IReadOnlyList<SocialHashtag>>> GetPopularHashtagsAsync(CancellationToken cancellationToken)
        {
            lock (s_lock)
            {
                if (s_hashtags != null && DateTimeOffset.UtcNow - s_hashtagsAt < HashtagLifetime)
                {
                    return SocialApiResult<IReadOnlyList<SocialHashtag>>.Succeeded(s_hashtags);
                }
            }

            var result = await SocialApiService.GetPopularHashtagsAsync(AppConstants.SocialHashtagLimit, cancellationToken);
            if (result.Status == SocialApiStatus.Ok)
            {
                lock (s_lock)
                {
                    s_hashtags = result.Value;
                    s_hashtagsAt = DateTimeOffset.UtcNow;
                }
            }

            return result;
        }

        public static async Task<IReadOnlyList<SocialAnnouncement>> GetUnreadAnnouncementsAsync(bool force, CancellationToken cancellationToken)
        {
            var account = SocialContentService.CurrentAccountId() ?? string.Empty;
            var maxAge = force ? AnnouncementRefreshAge : AnnouncementLifetime;

            lock (s_lock)
            {
                if (s_announcements != null
                    && string.Equals(s_announcementsAccount, account, StringComparison.Ordinal)
                    && DateTimeOffset.UtcNow - s_announcementsAt < maxAge)
                {
                    return Unread(s_announcements);
                }
            }

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetAnnouncementsAsync(token, AppConstants.SocialAnnouncementLimit, cancellationToken);

            lock (s_lock)
            {
                if (result.Status == SocialApiStatus.Ok)
                {
                    s_announcements = result.Value;
                    s_announcementsAt = DateTimeOffset.UtcNow;
                    s_announcementsAccount = account;
                    return Unread(s_announcements);
                }

                var sameAccount = s_announcements != null && string.Equals(s_announcementsAccount, account, StringComparison.Ordinal);
                return sameAccount ? Unread(s_announcements) : new SocialAnnouncement[0];
            }
        }

        public static async Task DismissAnnouncementAsync(SocialAnnouncement announcement)
        {
            if (announcement == null || string.IsNullOrEmpty(announcement.Id)) return;

            RememberDismissed(announcement.Id);

            var token = await MisskeyAuthService.TryGetTokenAsync();
            if (string.IsNullOrEmpty(token)) return;

            await SocialApiService.ReadAnnouncementAsync(token, announcement.Id, CancellationToken.None);
        }

        private static IReadOnlyList<SocialAnnouncement> Unread(IReadOnlyList<SocialAnnouncement> announcements)
        {
            var dismissed = new HashSet<string>(ReadDismissed(), StringComparer.Ordinal);
            return announcements.Where(announcement => !announcement.IsRead && !dismissed.Contains(announcement.Id)).ToList();
        }

        private static List<string> ReadDismissed()
        {
            var stored = AppSettings.ReadString(AppConstants.SettingSocialDismissedAnnouncements, null);
            return string.IsNullOrEmpty(stored)
                   ? new List<string>()
                   : stored.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static void RememberDismissed(string id)
        {
            lock (s_lock)
            {
                var dismissed = ReadDismissed();
                if (dismissed.Contains(id, StringComparer.Ordinal)) return;

                dismissed.Add(id);
                var excess = dismissed.Count - AppConstants.SocialDismissedAnnouncementLimit;
                if (excess > 0) dismissed.RemoveRange(0, excess);

                AppSettings.Write(AppConstants.SettingSocialDismissedAnnouncements, string.Join(",", dismissed));
            }
        }
    }
}
