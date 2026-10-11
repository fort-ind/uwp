using System.Globalization;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    internal static class SettingsComboTags
    {
        public static int? IntTagOf(ComboBoxItem item)
        {
            int value;
            if (item == null || !int.TryParse(item.Tag as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return null;
            }

            return value;
        }
    }
}
