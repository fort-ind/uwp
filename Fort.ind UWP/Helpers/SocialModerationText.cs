using System;

namespace Fort.ind_UWP
{
    internal static class SocialModerationText
    {
        public static string ActionLabel(SocialModerationList list)
        {
            switch (list)
            {
                case SocialModerationList.Muted: return LocalizedStrings.Get("SocialModerationUnmute");
                case SocialModerationList.RenoteMuted: return LocalizedStrings.Get("SocialModerationShowRenotes");
                default: return LocalizedStrings.Get("SocialModerationUnblock");
            }
        }

        public static string Outcome(SocialModerationList list)
        {
            switch (list)
            {
                case SocialModerationList.Muted: return LocalizedStrings.Get("SocialModerationUnmuted");
                case SocialModerationList.RenoteMuted: return LocalizedStrings.Get("SocialModerationRenotesShown");
                default: return LocalizedStrings.Get("SocialModerationUnblocked");
            }
        }

        public static string Detail(SocialUserItem item)
        {
            if (item == null || item.Moderation != SocialModerationList.Muted || !item.ModerationActive) return "";

            var ends = item.ModerationExpiresAt;
            return ends.HasValue
                   ? LocalizedStrings.Format("SocialModerationMutedUntilFormat", RelativeTime.Full(ends.Value))
                   : LocalizedStrings.Get("SocialModerationMutedForever");
        }

        public static string StatusOf(SocialUserItem item)
        {
            if (item == null || !item.Moderation.HasValue) return "";

            return item.ModerationActive ? Detail(item) : Outcome(item.Moderation.Value);
        }

        public static TimeSpan? RemainingMute(SocialUserItem item)
        {
            if (item == null || !item.ModerationExpiresAt.HasValue) return null;

            var left = item.ModerationExpiresAt.Value - DateTimeOffset.UtcNow;
            return left > TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1);
        }
    }
}
