using System;
using Microsoft.Xaml.Interactivity;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed class SocialNoteShortcutsBehavior : Behavior<ListViewBase>
    {
        private static readonly VirtualKey[] s_keys = { VirtualKey.R, VirtualKey.L, VirtualKey.E, VirtualKey.N, VirtualKey.Q, VirtualKey.Delete };

        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                AssociatedObject.ContainerContentChanging += OnContainerContentChanging;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialNoteShortcutsBehavior: Failed to attach", ex);
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                AssociatedObject.ContainerContentChanging -= OnContainerContentChanging;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialNoteShortcutsBehavior: Failed to detach", ex);
            }

            base.OnDetaching();
        }

        private static void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            try
            {
                if (args.InRecycleQueue) return;

                var container = args.ItemContainer;
                if (container == null || container.KeyboardAccelerators.Count > 0) return;

                Add(container, container);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialNoteShortcutsBehavior: could not add the shortcuts", ex);
            }
        }

        internal static void Add(UIElement target, DependencyObject scope)
        {
            target.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

            foreach (var key in s_keys)
            {
                var accelerator = new KeyboardAccelerator { Key = key, ScopeOwner = scope };
                accelerator.Invoked += OnInvoked;
                target.KeyboardAccelerators.Add(accelerator);
            }
        }

        private static void OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                var view = FindView(sender.ScopeOwner);
                if (view == null) return;

                SocialNoteShortcut shortcut;
                switch (sender.Key)
                {
                    case VirtualKey.R:
                        shortcut = SocialNoteShortcut.Reply;
                        break;
                    case VirtualKey.L:
                        shortcut = SocialNoteShortcut.Like;
                        break;
                    case VirtualKey.E:
                        shortcut = SocialNoteShortcut.React;
                        break;
                    case VirtualKey.N:
                        shortcut = SocialNoteShortcut.Renote;
                        break;
                    case VirtualKey.Q:
                        shortcut = SocialNoteShortcut.Quote;
                        break;
                    case VirtualKey.Delete:
                        shortcut = SocialNoteShortcut.Delete;
                        break;
                    default:
                        return;
                }

                args.Handled = view.InvokeShortcut(shortcut);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialNoteShortcutsBehavior: shortcut failed", ex);
            }
        }

        private static SocialNoteView FindView(DependencyObject root)
        {
            if (root == null) return null;

            var view = root as SocialNoteView;
            if (view != null) return view;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindView(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }

            return null;
        }
    }
}
