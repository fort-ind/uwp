using System;
using System.Globalization;

namespace Fort.ind_UWP
{
    public static class UnixTimeText
    {
        private const double Minute = 60;

        private const double Hour = 3600;

        private const double Day = 86400;

        private const double Week = 604800;

        private const double Month = 2592000;

        private const double Year = 31536000;

        private const long MinimumSeconds = -62135596800;

        private const long MaximumSeconds = 253402300799;

        private const int MaximumDigits = 12;

        public static bool TryParse(string content, out DateTimeOffset time)
        {
            time = default(DateTimeOffset);
            if (content == null) return false;

            var i = 0;
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;

            var negative = i < content.Length && content[i] == '-';
            if (negative || (i < content.Length && content[i] == '+')) i++;

            var start = i;
            while (i < content.Length && content[i] >= '0' && content[i] <= '9') i++;

            var digits = i - start;
            if (digits == 0 || digits > MaximumDigits) return false;

            long seconds;
            if (!long.TryParse(content.Substring(start, digits), NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
            {
                return false;
            }

            if (negative) seconds = -seconds;
            if (seconds < MinimumSeconds || seconds > MaximumSeconds) return false;

            time = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }

        public static string Label(DateTimeOffset time, DateTimeOffset now)
        {
            var relative = Ago(time, now);
            var absolute = RelativeTime.Detailed(time);
            return absolute.Length == 0 ? relative : LocalizedStrings.Format("UnixTimeLabelFormat", absolute, relative);
        }

        public static string Ago(DateTimeOffset time, DateTimeOffset now)
        {
            var elapsed = (now - time).TotalSeconds;
            if (elapsed >= Year) return LocalizedStrings.Format("UnixTimeAgoYearsFormat", Round(elapsed / Year));
            if (elapsed >= Month) return LocalizedStrings.Format("UnixTimeAgoMonthsFormat", Round(elapsed / Month));
            if (elapsed >= Week) return LocalizedStrings.Format("UnixTimeAgoWeeksFormat", Round(elapsed / Week));
            if (elapsed >= Day) return LocalizedStrings.Format("UnixTimeAgoDaysFormat", Round(elapsed / Day));
            if (elapsed >= Hour) return LocalizedStrings.Format("UnixTimeAgoHoursFormat", Round(elapsed / Hour));
            if (elapsed >= Minute) return LocalizedStrings.Format("UnixTimeAgoMinutesFormat", (int)(elapsed / Minute));
            if (elapsed >= 10) return LocalizedStrings.Format("UnixTimeAgoSecondsFormat", (int)(elapsed % Minute));
            if (elapsed >= -3) return LocalizedStrings.Get("UnixTimeAgoNow");

            var ahead = -elapsed;
            if (ahead > Year) return LocalizedStrings.Format("UnixTimeInYearsFormat", Round(ahead / Year));
            if (ahead > Month) return LocalizedStrings.Format("UnixTimeInMonthsFormat", Round(ahead / Month));
            if (ahead > Week) return LocalizedStrings.Format("UnixTimeInWeeksFormat", Round(ahead / Week));
            if (ahead > Day) return LocalizedStrings.Format("UnixTimeInDaysFormat", Round(ahead / Day));
            if (ahead > Hour) return LocalizedStrings.Format("UnixTimeInHoursFormat", Round(ahead / Hour));
            if (ahead > Minute) return LocalizedStrings.Format("UnixTimeInMinutesFormat", (int)(ahead / Minute));
            return LocalizedStrings.Format("UnixTimeInSecondsFormat", (int)(ahead % Minute));
        }

        private static int Round(double value)
        {
            return (int)Math.Floor(value + 0.5);
        }
    }
}
