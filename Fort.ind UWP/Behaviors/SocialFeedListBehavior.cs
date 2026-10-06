using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Xaml.Interactivity;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class SocialFeedListBehavior : Behavior<ListViewBase>
    {
        public static readonly DependencyProperty BackToNewestProperty =
            DependencyProperty.Register(nameof(BackToNewest), typeof(UIElement), typeof(SocialFeedListBehavior),
                                        new PropertyMetadata(null, OnBackToNewestChanged));

        private long _itemsSourceToken = -1;

        private INotifyPropertyChanged _watched;

        public UIElement BackToNewest
        {
            get { return (UIElement)GetValue(BackToNewestProperty); }
            set { SetValue(BackToNewestProperty, value); }
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                _itemsSourceToken = AssociatedObject.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, OnItemsSourceChanged);
                AssociatedObject.Loaded += OnAssociatedObjectLoaded;

                Watch(AssociatedObject.ItemsSource);
                ApplyCacheLength();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedListBehavior: Failed to attach - {ex.Message}");
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                AssociatedObject.Loaded -= OnAssociatedObjectLoaded;

                if (_itemsSourceToken >= 0)
                {
                    AssociatedObject.UnregisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, _itemsSourceToken);
                    _itemsSourceToken = -1;
                }

                Watch(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedListBehavior: Failed to detach - {ex.Message}");
            }

            base.OnDetaching();
        }

        private static void OnBackToNewestChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var behavior = sender as SocialFeedListBehavior;
            if (behavior != null) behavior.UpdateBackToNewest();
        }

        private void OnItemsSourceChanged(DependencyObject sender, DependencyProperty property)
        {
            try
            {
                Watch(AssociatedObject.ItemsSource);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedListBehavior: Failed to follow the list's items - {ex.Message}");
            }
        }

        private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyCacheLength();
                UpdateBackToNewest();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedListBehavior: Failed to sync on load - {ex.Message}");
            }
        }

        private void Watch(object source)
        {
            if (_watched != null) _watched.PropertyChanged -= OnFeedPropertyChanged;

            _watched = source as ISocialFeedHead;
            if (_watched != null) _watched.PropertyChanged += OnFeedPropertyChanged;

            UpdateBackToNewest();
        }

        private void OnFeedPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(ISocialFeedHead.HasDroppedHead), StringComparison.Ordinal)) return;

            try
            {
                UpdateBackToNewest();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedListBehavior: Failed to show Back to newest - {ex.Message}");
            }
        }

        private void UpdateBackToNewest()
        {
            var target = BackToNewest;
            if (target == null) return;

            var head = _watched as ISocialFeedHead;
            target.Visibility = head != null && head.HasDroppedHead ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyCacheLength()
        {
            var panel = AssociatedObject == null ? null : AssociatedObject.ItemsPanelRoot as ItemsStackPanel;
            if (panel == null) return;

            var length = MemoryService.ListCacheLength;
            if (panel.CacheLength != length) panel.CacheLength = length;
        }
    }
}
