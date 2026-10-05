using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public static class VisualTreeSearch
    {
        public static FrameworkElement FindDescendantByName(DependencyObject root, string name)
        {
            if (root == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                var element = child as FrameworkElement;
                if (element != null && element.Name == name) return element;

                var found = FindDescendantByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

        public static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            var current = element == null ? null : VisualTreeHelper.GetParent(element);
            while (current != null)
            {
                var match = current as T;
                if (match != null) return match;
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        public static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
        {
            if (ancestor == null) return false;

            while (element is UIElement)
            {
                if (element == ancestor) return true;
                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }
    }
}
