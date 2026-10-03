using System;
using System.Diagnostics;
using Microsoft.Xaml.Interactivity;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed class SocialContextMenuBehavior : Behavior<FrameworkElement>
    {
        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                AssociatedObject.ContextRequested += OnContextRequested;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialContextMenuBehavior: Failed to attach - {ex.Message}");
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                AssociatedObject.ContextRequested -= OnContextRequested;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialContextMenuBehavior: Failed to detach - {ex.Message}");
            }

            base.OnDetaching();
        }

        private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            try
            {
                if (args.Handled) return;

                var target = args.OriginalSource as FrameworkElement;
                var item = ItemFor(target);
                if (item == null) return;

                var flyout = SocialMenus.Build(item, AssociatedObject);
                if (flyout == null) return;

                Point point;
                SocialMenus.ShowAt(flyout, target, args.TryGetPosition(target, out point) ? point : (Point?)null);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialContextMenuBehavior: Failed to open the menu - {ex.Message}");
            }
        }

        private static object ItemFor(FrameworkElement element)
        {
            DependencyObject current = element;
            while (current != null)
            {
                var framework = current as FrameworkElement;
                if (framework != null)
                {
                    if (framework.DataContext is SocialNoteItem || framework.DataContext is SocialFeedItem || framework.DataContext is SocialUserItem)
                    {
                        return framework.DataContext;
                    }

                    var container = framework as ContentControl;
                    if (container != null && (container.Content is SocialNoteItem || container.Content is SocialFeedItem || container.Content is SocialUserItem))
                    {
                        return container.Content;
                    }

                    if (framework is ListViewBase) return null;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
