using System;
using Windows.Globalization.DateTimeFormatting;

namespace Fort.ind_UWP
{
    public static class RelativeTime
    {
        private static readonly Lazy<DateTimeFormatter> s_dateFormatter =
            new Lazy<DateTimeFormatter>(() => new DateTimeFormatter("shortdate"));

        private static readonly Lazy<DateTimeFormatter> s_timeFormatter =
            new Lazy<DateTimeFormatter>(() => new DateTimeFormatter("shorttime"));

        public static string Short(DateTimeOffset createdAt, DateTimeOffset now)
        {
            if (createdAt == DateTimeOffset.MinValue) return "";

            var elapsed = now - createdAt;
            if (elapsed < TimeSpan.FromMinutes(1)) return LocalizedStrings.Get("RelativeTimeNow");
            if (elapsed < TimeSpan.FromHours(1)) return LocalizedStrings.Format("RelativeTimeMinutesFormat", (int)elapsed.TotalMinutes);
            if (elapsed < TimeSpan.FromDays(1)) return LocalizedStrings.Format("RelativeTimeHoursFormat", (int)elapsed.TotalHours);
            if (elapsed < TimeSpan.FromDays(7)) return LocalizedStrings.Format("RelativeTimeDaysFormat", (int)elapsed.TotalDays);

            return FormatDate(createdAt);
        }

        public static string Remaining(DateTimeOffset end, DateTimeOffset now)
        {
            var left = end - now;
            if (left <= TimeSpan.Zero) return LocalizedStrings.Get("RelativeTimeEnded");
            if (left < TimeSpan.FromHours(1)) return LocalizedStrings.Format("RelativeTimeMinutesLeftFormat", Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
            if (left < TimeSpan.FromDays(1)) return LocalizedStrings.Format("RelativeTimeHoursLeftFormat", (int)left.TotalHours);
            if (left < TimeSpan.FromDays(7)) return LocalizedStrings.Format("RelativeTimeDaysLeftFormat", (int)left.TotalDays);

            return LocalizedStrings.Format("RelativeTimeEndsOnFormat", FormatDate(end));
        }

        public static string Full(DateTimeOffset createdAt)
        {
            if (createdAt == DateTimeOffset.MinValue) return "";

            try
            {
                var local = createdAt.ToLocalTime();
                return LocalizedStrings.Format("SocialTimeFullFormat",
                                               s_dateFormatter.Value.Format(local),
                                               s_timeFormatter.Value.Format(local));
            }
            catch (Exception ex)
            {
                AppLog.Error("RelativeTime: could not format a full timestamp", ex);
                return "";
            }
        }

        public static string FormatDate(DateTimeOffset value)
        {
            if (value == DateTimeOffset.MinValue) return "";

            try
            {
                return s_dateFormatter.Value.Format(value.ToLocalTime());
            }
            catch (Exception ex)
            {
                AppLog.Error("RelativeTime: could not format a date", ex);
                return "";
            }
        }
    }
}
