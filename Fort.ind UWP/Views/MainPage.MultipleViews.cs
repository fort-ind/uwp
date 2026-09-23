using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void AttachOpenInNewWindowMenus()
        {
            if (!LabsService.MultipleViewsActive) return;

            try
            {
                foreach (var item in NavView.MenuItems)
                {
                    var navItem = item as NavigationViewItem;
                    if (navItem == null) continue;

                    var tag = navItem.Tag as string;
                    if (!CanOpenInNewWindow(tag)) continue;

                    navItem.ContextFlyout = BuildOpenInNewWindowFlyout(tag);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not attach the new-window menus - {ex.Message}");
            }
        }

        private static bool CanOpenInNewWindow(string tag)
        {
            switch (tag)
            {
                case AppConstants.NavigationLatestNews:
                case AppConstants.NavigationGames:
                case AppConstants.NavigationBetas:
                case AppConstants.NavigationProfile:
                case AppConstants.NavigationSocial:
                    return true;
                default:
                    return false;
            }
        }

        private MenuFlyout BuildOpenInNewWindowFlyout(string tag)
        {
            var menuItem = new MenuFlyoutItem()
            {
                Text = LocalizedStrings.Get("OpenInNewWindowMenuItem"),
                Icon = new FontIcon() { Glyph = "" },
                Tag = tag
            };
            menuItem.Click += OpenInNewWindowMenuItem_Click;

            var flyout = new MenuFlyout();
            flyout.Items.Add(menuItem);
            return flyout;
        }

        private async void OpenInNewWindowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as FrameworkElement;
                var tag = menuItem == null ? null : menuItem.Tag as string;
                if (!CanOpenInNewWindow(tag)) return;

                var shown = await WindowManagerService.ShowAsync(tag, HeaderFor(tag), PageTypeFor(tag));
                if (!shown)
                {
                    Debug.WriteLine($"MainPage: the window for {tag} was not shown");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: opening a new window failed - {ex.Message}");
            }
        }
    }
}
