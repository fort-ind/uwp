using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;

namespace Fort.ind_UWP
{
    public static class SocialTileService
    {
        private const string LockModeCount = "c";

        private const string LockModeSenders = "s";

        private const int MaximumLockLines = 3;

        private const string FeedSignaturePrefix = "f|";

        private const string FeedAvatars = "a";

        private const string FeedNoAvatars = "n";

        private const string UniversalApiContract = "Windows.Foundation.UniversalApiContract";

        private const ushort WindowsElevenContractVersion = 14;

        private static readonly Lazy<bool> s_startShowsLiveTiles = new Lazy<bool>(
            () => !ApiInformation.IsApiContractPresent(UniversalApiContract, WindowsElevenContractVersion));

        private static readonly object s_lock = new object();

        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        private static int s_generation;

        public static bool PreviewsEnabled
        {
            get { return AppSettings.ReadBool(AppConstants.SettingTileShowsNotifications, true); }
            set { AppSettings.Write(AppConstants.SettingTileShowsNotifications, value); }
        }

        public static bool LockScreenShowsSenders
        {
            get { return AppSettings.ReadBool(AppConstants.SettingLockScreenShowsSenders, false); }
            set { AppSettings.Write(AppConstants.SettingLockScreenShowsSenders, value); }
        }

        public static bool StartShowsLiveTiles
        {
            get { return s_startShowsLiveTiles.Value; }
        }

        public static bool WantsPreviews
        {
            get
            {
                return StartShowsLiveTiles && SocialNotificationService.Enabled && PreviewsEnabled &&
                       !LiveTileService.TileCleared;
            }
        }

        public static void ShowNewsTile(bool replacePreviews)
        {
            lock (s_lock)
            {
                if (replacePreviews)
                {
                    s_generation++;
                }
                else if (ReadShown() != null)
                {
                    return;
                }

                RemoveShown();
                LiveTileService.ShowNewsTile();
            }

            if (replacePreviews)
            {
                ReleaseAvatarsInBackground();
            }
        }

        public static bool ShowsNotificationPreviews
        {
            get
            {
                var shown = ReadShown();
                return shown != null && !IsFeedSignature(shown);
            }
        }

        public static void Withdraw()
        {
            var restoreFeed = false;
            lock (s_lock)
            {
                s_generation++;
                var shown = ReadShown();
                if (shown == null || IsFeedSignature(shown)) return;

                RemoveShown();
                if (!LiveTileService.TileCleared)
                {
                    LiveTileService.ShowNewsTile();
                    restoreFeed = SocialFeedTileService.WantsFeed;
                }
            }

            ReleaseAvatarsInBackground();

            if (restoreFeed)
            {
                SocialFeedTileService.RefreshInBackground();
            }
        }

        public static void WithdrawFeed()
        {
            lock (s_lock)
            {
                s_generation++;
                if (!IsFeedSignature(ReadShown())) return;

                RemoveShown();
                if (!LiveTileService.TileCleared)
                {
                    LiveTileService.ShowNewsTile();
                }
            }

            ReleaseAvatarsInBackground();
        }

        public static void ClearTile()
        {
            lock (s_lock)
            {
                s_generation++;
                RemoveShown();
                LiveTileService.ClearTile();
            }

            ReleaseAvatarsInBackground();
        }

        public static async Task ShowAsync(IReadOnlyList<SocialNotification> unread, int count, CancellationToken cancellationToken)
        {
            if (unread == null || count <= 0 || !WantsPreviews) return;

            var newest = unread.Where(n => n != null)
                               .OrderByDescending(n => n.CreatedAt)
                               .Take(Math.Min(count, AppConstants.SocialTilePreviewLimit))
                               .ToList();
            if (newest.Count == 0) return;

            var showSenders = LockScreenShowsSenders;
            var signature = SignaturePrefix(count, showSenders) + string.Join(",", newest.Select(n => n.Id));

            int generation;
            lock (s_lock)
            {
                if (string.Equals(ReadShown(), signature, StringComparison.Ordinal)) return;
                generation = ++s_generation;
            }

            await s_gate.WaitAsync(cancellationToken);
            try
            {
                var previews = new List<TilePreview>();
                foreach (var notification in newest)
                {
                    var preview = await BuildPreviewAsync(notification, cancellationToken);
                    if (preview != null)
                    {
                        previews.Add(preview);
                    }
                }

                if (previews.Count == 0) return;
                cancellationToken.ThrowIfCancellationRequested();

                var lockLines = LockLinesFor(previews, count, showSenders);

                lock (s_lock)
                {
                    if (generation != s_generation || !WantsPreviews) return;
                    if (!LiveTileService.ShowPreviewTiles(previews, lockLines)) return;

                    WriteShown(signature);
                }

                await AvatarIconService.RetainTileAvatarsAsync(previews.Select(p => p.Avatar).ToList());
            }
            finally
            {
                s_gate.Release();
            }
        }

        public static async Task ShowFeedAsync(IReadOnlyList<SocialNote> notes, bool showAvatars, TimeSpan interval, CancellationToken cancellationToken)
        {
            if (notes == null || notes.Count == 0 || !SocialFeedTileService.WantsFeed) return;

            var signature = FeedSignaturePrefix + (showAvatars ? FeedAvatars : FeedNoAvatars) + "|" +
                            string.Join(",", notes.Select(n => n.Id));

            int generation;
            lock (s_lock)
            {
                var shown = ReadShown();
                if (shown != null && !IsFeedSignature(shown)) return;
                if (string.Equals(shown, signature, StringComparison.Ordinal) && FeedPushIsFresh(interval)) return;

                generation = s_generation;
            }

            await s_gate.WaitAsync(cancellationToken);
            try
            {
                var previews = new List<TilePreview>();
                foreach (var note in notes)
                {
                    var preview = await BuildFeedPreviewAsync(note, showAvatars, cancellationToken);
                    if (preview != null)
                    {
                        previews.Add(preview);
                    }
                }

                if (previews.Count == 0) return;
                cancellationToken.ThrowIfCancellationRequested();

                var expiration = DateTimeOffset.Now + FeedExpiry(interval);

                lock (s_lock)
                {
                    if (generation != s_generation || !SocialFeedTileService.WantsFeed) return;
                    if (ShowsNotificationPreviews) return;
                    if (!LiveTileService.ShowPreviewTiles(previews, null, expiration)) return;

                    WriteShown(signature);
                    AppSettings.Write(AppConstants.SettingSocialFeedTilePushedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                }

                await AvatarIconService.RetainTileAvatarsAsync(previews.Select(p => p.Avatar).ToList());
            }
            finally
            {
                s_gate.Release();
            }
        }

        public static async Task ClearAvatarsAsync()
        {
            await s_gate.WaitAsync();
            try
            {
                lock (s_lock)
                {
                    RemoveShown();
                }

                await AvatarIconService.RetainTileAvatarsAsync(new Uri[0]);
            }
            finally
            {
                s_gate.Release();
            }
        }

        private static async Task<TilePreview> BuildPreviewAsync(SocialNotification notification, CancellationToken cancellationToken)
        {
            var item = SocialFeedItem.FromNotification(notification, true);
            if (item == null) return null;

            Uri avatar = null;
            if (notification.User != null)
            {
                avatar = await AvatarIconService.GetTileAvatarUriAsync(notification.User.AvatarUrl, cancellationToken);
            }

            return new TilePreview
            {
                Title = item.Title,
                Body = item.IsDirect ? LocalizedStrings.Get("SocialToastDirectNoteBody") : item.Body,
                ActorName = item.ActorName,
                Avatar = avatar
            };
        }

        private static async Task<TilePreview> BuildFeedPreviewAsync(SocialNote note, bool showAvatars, CancellationToken cancellationToken)
        {
            var item = SocialFeedItem.FromNote(note);
            if (item == null || string.IsNullOrWhiteSpace(item.Body)) return null;

            Uri avatar = null;
            if (showAvatars && note.User != null)
            {
                avatar = await AvatarIconService.GetTileAvatarUriAsync(note.User.AvatarUrl, cancellationToken);
            }

            return new TilePreview
            {
                Title = item.Title,
                Body = item.Body,
                ActorName = item.ActorName,
                Avatar = avatar
            };
        }

        private static bool IsFeedSignature(string signature)
        {
            return signature != null && signature.StartsWith(FeedSignaturePrefix, StringComparison.Ordinal);
        }

        private static TimeSpan FeedExpiry(TimeSpan interval)
        {
            var expiry = TimeSpan.FromTicks(interval.Ticks * AppConstants.SocialFeedTileExpiryMultiple);
            var floor = TimeSpan.FromHours(AppConstants.SocialFeedTileMinimumExpiryHours);
            return expiry < floor ? floor : expiry;
        }

        private static bool FeedPushIsFresh(TimeSpan interval)
        {
            var pushedAt = AppSettings.ReadDouble(AppConstants.SettingSocialFeedTilePushedAt, 0);
            if (pushedAt <= 0) return false;

            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - pushedAt;
            return age >= 0 && age < interval.TotalSeconds / 2;
        }

        private static IReadOnlyList<string> LockLinesFor(IReadOnlyList<TilePreview> previews, int count, bool showSenders)
        {
            if (!showSenders)
            {
                return new[]
                {
                    count == 1
                    ? LocalizedStrings.Get("TileLockOneNotification")
                    : LocalizedStrings.Format("TileLockNotificationsFormat", count),
                    LocalizedStrings.Get("TileLockSource")
                };
            }

            var named = count > MaximumLockLines ? MaximumLockLines - 1 : MaximumLockLines;
            var lines = previews.Take(named).Select(p => p.Title).ToList();

            var remaining = count - lines.Count;
            if (remaining > 0)
            {
                lines.Add(LocalizedStrings.Format("TileLockMoreFormat", remaining));
            }

            return lines;
        }

        private static async void ReleaseAvatarsInBackground()
        {
            try
            {
                await s_gate.WaitAsync();
                try
                {
                    if (ReadShown() != null) return;

                    await AvatarIconService.RetainTileAvatarsAsync(new Uri[0]);
                }
                finally
                {
                    s_gate.Release();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialTileService: could not release the tile avatars", ex);
            }
        }

        private static string SignaturePrefix(int count, bool showSenders)
        {
            return count.ToString(CultureInfo.InvariantCulture) + "|" +
                   (showSenders ? LockModeSenders : LockModeCount) + "|";
        }

        private static string ReadShown()
        {
            return AppSettings.ReadString(AppConstants.SettingSocialTileShown, null);
        }

        private static void WriteShown(string signature)
        {
            AppSettings.Write(AppConstants.SettingSocialTileShown, signature);
        }

        private static void RemoveShown()
        {
            AppSettings.Remove(AppConstants.SettingSocialTileShown);
        }
    }
}
