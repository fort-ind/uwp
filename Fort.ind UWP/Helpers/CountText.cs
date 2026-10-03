using System;
using System.Diagnostics;
using Windows.Globalization.NumberFormatting;
using Windows.UI.Text;
using Windows.UI.Xaml.Documents;

namespace Fort.ind_UWP
{
    public static class CountText
    {
        private static DecimalFormatter s_formatter;

        public static string Format(int count)
        {
            try
            {
                if (s_formatter == null)
                {
                    s_formatter = new DecimalFormatter()
                    {
                        FractionDigits = 0,
                        IsGrouped = true
                    };
                }

                return s_formatter.FormatInt(count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CountText: count formatting failed - {ex.Message}");
                return count.ToString(System.Globalization.CultureInfo.CurrentCulture);
            }
        }

        public static void AppendBold(InlineCollection target, string formatKey, int count)
        {
            var format = LocalizedStrings.Get(formatKey);
            var number = Format(count);

            var index = format.IndexOf("{0}", StringComparison.Ordinal);
            if (index < 0)
            {
                target.Add(new Run { Text = LocalizedStrings.FormatPattern(format, formatKey, number) });
                return;
            }

            var before = format.Substring(0, index);
            var after = format.Substring(index + 3);

            if (before.Length > 0)
            {
                target.Add(new Run { Text = before });
            }

            target.Add(new Run { Text = number, FontWeight = FontWeights.SemiBold });

            if (after.Length > 0)
            {
                target.Add(new Run { Text = after });
            }
        }
    }
}
