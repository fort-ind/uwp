using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private readonly ObservableCollection<SearchItem> _homeFavorites =
            new ObservableCollection<SearchItem>();

        private bool _favoritesHandlerAttached = false;

        private bool _favoritesItemsSourceSet = false;

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

        private void OnFavoritesChanged(object sender, EventArgs e)
        {
            try
            {
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

            var favorites = FavoritesService.GetFavorites(_allSearchItems, AppConstants.HomeFavoritesMaxCount + 1);
            var hasOverflow = favorites.Count > AppConstants.HomeFavoritesMaxCount;
            if (hasOverflow)
            {
                favorites.RemoveAt(favorites.Count - 1);
            }

            _homeFavorites.Clear();
            foreach (var item in favorites)
            {
                _homeFavorites.Add(item);
            }

            var hasAny = favorites.Count > 0;
            FavoritesList.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
            FavoritesEmptyText.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

            FavoritesSeeAllLink.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void HomeFavoriteItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as FrameworkElement;
                if (button == null) return;

                var item = button.DataContext as SearchItem;
                if (item == null || string.IsNullOrEmpty(item.Url)) return;

                await WebLauncher.LaunchAsync(item.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to launch favorite - {ex.Message}");
            }
        }

        private void HomeFavoriteToggle_Checked(object sender, RoutedEventArgs e)
        {
            SetHomeFavorite(sender, true);
        }

        private void HomeFavoriteToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SetHomeFavorite(sender, false);
        }

        private async void SetHomeFavorite(object sender, bool isFavorite)
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
