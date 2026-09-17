using System.Globalization;
using System.Text;

namespace Fort.ind_UWP
{
    public static class TextHelper
    {
        public static string FirstTextElements(string value, int count)
        {
            if (string.IsNullOrEmpty(value) || count <= 0) return "";

            StringBuilder builder = new StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(value);

            int taken = 0;
            while (taken < count && enumerator.MoveNext())
            {
                builder.Append((string)enumerator.Current);
                taken += 1;
            }

            return builder.ToString();
        }
    }
}
