using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class SocialTileService
    {
        private const string LockModeCount = "c";

        private const string LockModeSenders = "s";

        private const int MaximumLockLines = 3;

        private const string UniversalApiContract = "Windows.Foundation.UniversalApiContract";

        private const ushort WindowsElevenContractVersion = 14;

        private static readonly Lazy<bool> s_startShowsLiveTiles = new Lazy<bool>(
            () => !ApiInformation.IsApiContractPresent(UniversalApiContract, WindowsElevenContractVersion));

        private static readonly object s_lock = new object();

        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        private static int s_generation;

        public static bool PreviewsEnabled
        {
            get { return ReadFlag(AppConstants.SettingTileShowsNotifications, true); }
            set { WriteFlag(AppConstants.SettingTileShowsNotifications, value); }
        }

        public static bool LockScreenShowsSenders
        {
            get { return ReadFlag(AppConstants.SettingLockScreenShowsSenders, false); }
            set { WriteFlag(AppConstants.SettingLockScreenShowsSenders, value); }
        }

        public static bool StartShowsLiveTiles
        {
            get { return s_startShowsLiveTiles.Value; }
        }

        public static bool WantsPreviews
        {
            get
            {
                return StartShowsLiveTiles && LabsService.SocialNotificationsEnabled && PreviewsEnabled &&
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

        public static void Withdraw()
        {
            lock (s_lock)
            {
                s_generation++;
                if (ReadShown() == null) return;

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
                Debug.WriteLine($"SocialTileService: could not release the tile avatars - {ex.Message}");
            }
        }

        private static string SignaturePrefix(int count, bool showSenders)
        {
            return count.ToString(CultureInfo.InvariantCulture) + "|" +
                   (showSenders ? LockModeSenders : LockModeCount) + "|";
        }

        private static string ReadShown()
        {
            try
            {
                return ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialTileShown] as string;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialTileService: could not read the shown previews - {ex.Message}");
                return null;
            }
        }

        private static void WriteShown(string signature)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialTileShown] = signature;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialTileService: could not record the shown previews - {ex.Message}");
            }
        }

        private static void RemoveShown()
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values.Remove(AppConstants.SettingSocialTileShown);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialTileService: could not forget the shown previews - {ex.Message}");
            }
        }

        private static bool ReadFlag(string key, bool fallback)
        {
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[key];
                return stored == null ? fallback : Convert.ToBoolean(stored);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialTileService: could not read {key} - {ex.Message}");
                return fallback;
            }
        }

        private static void WriteFlag(string key, bool value)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialTileService: could not write {key} - {ex.Message}");
            }
        }
    }
}
