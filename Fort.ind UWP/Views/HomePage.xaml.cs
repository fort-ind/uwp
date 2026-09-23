using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class HomePage : Page, IShellContentPage
    {
        private static readonly SearchItem[] s_noItems = new SearchItem[0];

        private readonly ObservableCollection<SearchItem> _homeFavorites =
            new ObservableCollection<SearchItem>();

        private IReadOnlyList<SearchItem> _favoriteSource = s_noItems;

        private bool _favoritesHandlerAttached = false;

        private bool _favoritesItemsSourceSet = false;

        private bool _favoritesRequested = false;

        public HomePage()
        {
            this.InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_favoritesHandlerAttached)
            {
                FavoritesService.FavoritesChanged += OnFavoritesChanged;
                _favoritesHandlerAttached = true;
            }

            if (_favoritesRequested)
            {
                FavoritesService.Apply(_favoriteSource);
                RefreshFavoritesSection();
                return;
            }

            _favoritesRequested = true;
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                              () => InitializeFavorites());
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_favoritesHandlerAttached)
            {
                FavoritesService.FavoritesChanged -= OnFavoritesChanged;
                _favoritesHandlerAttached = false;
            }
        }

        private async void InitializeFavorites()
        {
            try
            {
                _favoriteSource = await SitemapService.LoadSearchItemsAsync();

                await FavoritesService.EnsureLoadedAsync();
                FavoritesService.Apply(_favoriteSource);
                RefreshFavoritesSection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage: Failed to initialize favorites - {ex.Message}");
                RefreshFavoritesSection();
            }
        }

        private async void OnFavoritesChanged(object sender, EventArgs e)
        {
            try
            {
                if (Dispatcher.HasThreadAccess)
                {
                    ReapplyFavorites();
                    return;
                }

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, ReapplyFavorites);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage: Favorites change handler failed - {ex.Message}");
            }
        }

        private void ReapplyFavorites()
        {
            try
            {
                FavoritesService.Apply(_favoriteSource);
                RefreshFavoritesSection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage: Failed to refresh favorites - {ex.Message}");
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

            var favorites = FavoritesService.GetFavorites(_favoriteSource, AppConstants.HomeFavoritesMaxCount + 1);
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
                Debug.WriteLine($"HomePage: Failed to launch favorite - {ex.Message}");
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
                    PageScrollViewer,
                    LocalizedStrings.Format(isFavorite ? "FavoriteAddedFormat" : "FavoriteRemovedFormat", item.Title),
                    "HomeFavoriteToggle");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage: Failed to toggle favorite - {ex.Message}");
            }
        }

        private async void FavoritesSeeAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WindowManagerService.IsSecondaryView)
                {
                    await WindowManagerService.ShowInMainWindowAsync(AppConstants.NavigationGames);
                    return;
                }

                var shell = MainPage.Current;
                if (shell == null) return;

                shell.NavigateToTag(AppConstants.NavigationGames);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage: Failed to navigate to Games from favorites - {ex.Message}");
            }
        }
    }
}
