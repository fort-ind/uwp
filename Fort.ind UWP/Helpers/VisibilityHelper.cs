using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public static class VisibilityHelper
    {
        public static bool IsShown(this UIElement element)
        {
            return element.Visibility == Visibility.Visible;
        }
    }
}
