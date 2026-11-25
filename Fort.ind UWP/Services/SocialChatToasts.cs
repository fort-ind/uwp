using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class SocialChatToasts
    {
        private const int MaximumToastKeyLength = 64;

        private static readonly object s_lock = new object();

        public static string GroupFor(string otherUserId)
        {
            return Truncate(AppConstants.SocialChatToastGroupPrefix + (otherUserId ?? ""));
        }

        private static bool Wants(string account)
        {
            return LabsService.ChatEnabled
                   && SocialNotificationService.Enabled
                   && SocialNotificationService.IsActiveAccount(account)
                   && SocialPermissions.Has(SocialPermissions.ReadChat);
        }

        public static async void ToastFromStream(SocialChatMessage message, string account)
        {
            try
            {
                if (message == null || !Wants(account)) return;

                if (TryClaim(message))
                {
                    await ShowAsync(message, account, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatToasts: live chat toast failed", ex);
            }
        }

        public static async Task CheckAsync(string token, string account, bool hasUnread, CancellationToken cancellationToken)
        {
            if (!Wants(account)) return;

            if (!ReadFloor().HasValue)
            {
                await EstablishBaselineAsync(token, account, cancellationToken);
                return;
            }

            if (!hasUnread) return;

            var history = await SocialApiService.GetChatHistoryAsync(token, AppConstants.SocialChatBackgroundHistoryLimit, cancellationToken);
            if (!SocialNotificationService.IsActiveAccount(account)) return;

            if (history.Status == SocialApiStatus.PermissionDenied)
            {
                SocialPermissions.Forget(SocialPermissions.ReadChat);
                return;
            }

            if (history.Status != SocialApiStatus.Ok) return;

            var waiting = history.Value.Where(message => !message.IsRoomMessage
                                                         && message.IsKnownUnread
                                                         && !message.IsFrom(account))
                                       .OrderBy(message => message.CreatedAt)
                                       .ToList();

            foreach (var message in waiting)
            {
                if (!SocialNotificationService.IsActiveAccount(account)) return;

                if (TryClaim(message))
                {
                    await ShowAsync(message, account, cancellationToken);
                }
            }
        }

        private static async Task EstablishBaselineAsync(string token, string account, CancellationToken cancellationToken)
        {
            var newest = await SocialApiService.GetChatHistoryAsync(token, AppConstants.SocialChatBackgroundHistoryLimit, cancellationToken);
            if (!SocialNotificationService.IsActiveAccount(account) || newest.Status != SocialApiStatus.Ok) return;

            lock (s_lock)
            {
                if (ReadFloor().HasValue) return;

                var floor = newest.Value.Count == 0 ? DateTimeOffset.MinValue : newest.Value.Max(message => message.CreatedAt);
                WriteFloor(floor);
            }
        }

        private static async Task ShowAsync(SocialChatMessage message, string account, CancellationToken cancellationToken)
        {
            var sender = message.FromUser;

            Uri logo = null;
            if (sender != null)
            {
                logo = await AvatarIconService.GetToastAvatarUriAsync(sender.AvatarUrl, cancellationToken);
            }

            var toast = new GroupedToast
            {
                Title = sender == null ? LocalizedStrings.Get("SocialChatToastSomeone") : MfmText.PlainName(SocialNoteItem.DisplayNameOf(sender)),
                Body = BodyFor(message),
                Attribution = LocalizedStrings.Get("SocialChatToastAttribution"),
                AppLogo = logo,
                Timestamp = message.CreatedAt,
                Group = GroupFor(message.FromUserId),
                Tag = Truncate(message.Id)
            };
            toast.AddArgument(AppConstants.ToastArgumentOpen, AppConstants.ToastOpenChat);
            toast.AddArgument(AppConstants.ToastArgumentUser, message.FromUserId);
            toast.AddArgument(AppConstants.ToastArgumentAccount, account);
            toast.ChatReply = SocialToastActions.ChatReplyFor(message.FromUserId, sender, account);

            LiveTileService.ShowGroupedToast(toast);
        }

        public static string BodyFor(SocialChatMessage message)
        {
            if (message == null) return "";

            var text = string.IsNullOrWhiteSpace(message.Text) ? null : MfmText.Preview(message.Text, null);
            if (!string.IsNullOrWhiteSpace(text)) return text;

            return message.File != null ? LocalizedStrings.Get("SocialChatAttachmentPreview") : "";
        }

        public static void Remove(string otherUserId)
        {
            if (string.IsNullOrEmpty(otherUserId)) return;

            LiveTileService.RemoveToastGroup(GroupFor(otherUserId));
        }

        public static void RemoveAll()
        {
            LiveTileService.RemoveToastGroups(group => group.StartsWith(AppConstants.SocialChatToastGroupPrefix, StringComparison.Ordinal));
        }

        public static void ClearState()
        {
            lock (s_lock)
            {
                ApplicationData.Current.LocalSettings.Values.Remove(AppConstants.SettingSocialChatToastedThrough);
                ApplicationData.Current.LocalSettings.Values.Remove(AppConstants.SettingSocialChatToastedIds);
            }

            RemoveAll();
        }

        private static bool TryClaim(SocialChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Id)) return false;

            lock (s_lock)
            {
                var floor = ReadFloor();
                var toasted = ReadToasted();
                var key = Truncate(message.Id);

                if (floor.HasValue && message.CreatedAt <= floor.Value) return false;
                if (toasted.ContainsKey(key)) return false;

                toasted[key] = message.CreatedAt.UtcTicks;

                var newFloor = floor ?? message.CreatedAt.AddTicks(-1);
                while (toasted.Count > AppConstants.SocialToastedIdLimit)
                {
                    var oldest = toasted.OrderBy(entry => entry.Value).First();
                    toasted.Remove(oldest.Key);

                    var oldestAt = new DateTimeOffset(oldest.Value, TimeSpan.Zero);
                    if (oldestAt > newFloor) newFloor = oldestAt;
                }

                if (newFloor != floor) WriteFloor(newFloor);
                WriteToasted(toasted);
                return true;
            }
        }

        private static string Truncate(string value)
        {
            if (value == null) return "";
            return value.Length > MaximumToastKeyLength ? value.Substring(0, MaximumToastKeyLength) : value;
        }

        private static Dictionary<string, long> ReadToasted()
        {
            var toasted = new Dictionary<string, long>(StringComparer.Ordinal);
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialChatToastedIds] as ApplicationDataCompositeValue;
                if (stored == null) return toasted;

                foreach (var entry in stored)
                {
                    var ticks = Convert.ToInt64(entry.Value, CultureInfo.InvariantCulture);
                    if (ticks >= 0 && ticks <= DateTimeOffset.MaxValue.UtcTicks)
                    {
                        toasted[entry.Key] = ticks;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatToasts: could not read the toasted messages", ex);
            }

            return toasted;
        }

        private static void WriteToasted(Dictionary<string, long> toasted)
        {
            var stored = new ApplicationDataCompositeValue();
            foreach (var entry in toasted)
            {
                stored[entry.Key] = entry.Value;
            }

            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialChatToastedIds] = stored;
        }

        private static DateTimeOffset? ReadFloor()
        {
            try
            {
                var raw = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialChatToastedThrough] as string;
                DateTimeOffset parsed;
                if (!string.IsNullOrEmpty(raw) &&
                    DateTimeOffset.TryParseExact(raw, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatToasts: could not read the toast floor", ex);
            }

            return null;
        }

        private static void WriteFloor(DateTimeOffset value)
        {
            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialChatToastedThrough] =
                value.ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
