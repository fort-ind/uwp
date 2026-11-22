using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    internal static class ElementLoadedState
    {
        public static bool IsLoadedNow(this FrameworkElement element)
        {
            if (element == null) return false;
            if (ApiContracts.Has1809) return ReadIsLoaded(element);

            return VisualTreeHelper.GetParent(element) != null && element.ActualWidth > 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadIsLoaded(FrameworkElement element)
        {
            return element.IsLoaded;
        }
    }
}
