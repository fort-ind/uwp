using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The favorites section on Home - the list under the news cards, and the star on each row.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Bound once and then mutated in place. Reassigning ItemsSource would drop the binding
        /// the ItemsControl was given in markup.
        /// </summary>
        private readonly ObservableCollection<SearchItem> _homeFavorites =
            new ObservableCollection<SearchItem>();

        private bool _favoritesHandlerAttached = false;

        private bool _favoritesItemsSourceSet = false;

        /// <summary>
        /// Reads the persisted set, stamps it onto the loaded items and paints the section. Called
        /// once the sitemap has landed, since the section can only show items that exist.
        /// </summary>
        private async void InitializeFavorites()
        {
            try
            {
                await FavoritesService.EnsureLoadedAsync();
                FavoritesService.Apply(_allSearchItems);
                RefreshFavoritesSection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to initialize favorites - {ex.Message}");
            }
        }

        private void AttachFavoritesHandler()
        {
            if (_favoritesHandlerAttached) return;

            FavoritesService.FavoritesChanged += OnFavoritesChanged;
            _favoritesHandlerAttached = true;
        }

        private void DetachFavoritesHandler()
        {
            if (!_favoritesHandlerAttached) return;

            FavoritesService.FavoritesChanged -= OnFavoritesChanged;
            _favoritesHandlerAttached = false;
        }

        /// <summary>
        /// Fires when the set changes anywhere - including from GamesPage, which is a different
        /// page entirely. Home is still loaded behind ContentFrame at that point, so it repaints
        /// rather than waiting to be navigated back to.
        /// </summary>
        private void OnFavoritesChanged(object sender, EventArgs e)
        {
            try
            {
                // Re-stamp before repainting. On a plain toggle this is a no-op for every item but
                // one; after an app-data wipe it is what actually clears the stars still showing on
                // GamesPage, which holds the very same SearchItem instances.
                FavoritesService.Apply(_allSearchItems);
                RefreshFavoritesSection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to refresh favorites - {ex.Message}");
            }
        }

        private void RefreshFavoritesSection()
        {
            if (FavoritesList == null) return;

            if (!_favoritesItemsSourceSet)
            {
                FavoritesList.ItemsSource = _homeFavorites;
                _favoritesItemsSourceSet = true;
            }

            var favorites = FavoritesService.GetFavorites(_allSearchItems, AppConstants.HomeFavoritesMaxCount);

            _homeFavorites.Clear();
            foreach (var item in favorites)
            {
                _homeFavorites.Add(item);
            }

            var hasAny = favorites.Count > 0;
            FavoritesList.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
            FavoritesEmptyText.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

            // Only offer the overflow link when there is genuinely more than the cap shows.
            FavoritesSeeAllLink.Visibility = FavoritesService.Count > AppConstants.HomeFavoritesMaxCount
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void HomeFavoriteItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as FrameworkElement;
                if (button == null) return;

                var item = button.DataContext as SearchItem;
                if (item == null || string.IsNullOrEmpty(item.Url)) return;

                // WebLauncher, not Launcher: the URL came out of the sitemap, and only http/https
                // may be launched from it.
                await WebLauncher.LaunchAsync(item.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to launch favorite - {ex.Message}");
            }
        }

        private void HomeFavoriteToggle_Checked(object sender, RoutedEventArgs e)
        {
            SetHomeFavoriteAsync(sender, true);
        }

        private void HomeFavoriteToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SetHomeFavoriteAsync(sender, false);
        }

        /// <summary>
        /// Unstarring from Home removes the row under the user's pointer, so the announcement is
        /// the only feedback a screen reader gets. Guarded against the container-recycling case the
        /// same way GamesPage is - the IsChecked binding raises these events itself.
        /// </summary>
        private async void SetHomeFavoriteAsync(object sender, bool isFavorite)
        {
            try
            {
                var toggle = sender as FrameworkElement;
                if (toggle == null) return;

                var item = toggle.DataContext as SearchItem;
                if (item == null) return;

                if (item.IsFavorite == isFavorite) return;

                await FavoritesService.SetFavoriteAsync(item, isFavorite);

                AutomationHelper.AnnounceStatus(
                    ContentScrollViewer,
                    LocalizedStrings.Format(isFavorite ? "FavoriteAddedFormat" : "FavoriteRemovedFormat", item.Title),
                    "HomeFavoriteToggle");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to toggle favorite - {ex.Message}");
            }
        }

        private void FavoritesSeeAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Both calls are needed: assigning SelectedItem raises SelectionChanged, not
                // ItemInvoked, so the pane lights up but nothing navigates without ShowContent.
                SelectNavItemForTag(AppConstants.NavigationGames);
                ShowContent(AppConstants.NavigationGames, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to navigate to Games from favorites - {ex.Message}");
            }
        }
    }
}
