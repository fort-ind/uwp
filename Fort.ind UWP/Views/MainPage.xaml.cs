
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private IReadOnlyList<SearchItem> _allSearchItems = SearchCatalog.GetStaticItems();

        /// <summary>
        /// True while settings are being restored into the controls, so their change handlers do
        /// not treat that as a user edit. Starts true, and LoadAppearanceSettings' finally is what
        /// first clears it.
        /// </summary>
        /// <remarks>
        /// The initial true is load-bearing, not tidiness. Setting a Slider's Minimum in markup
        /// coerces its Value up to that minimum, which raises ValueChanged *during*
        /// InitializeComponent - before the elements declared after it in the XAML have been
        /// assigned to their fields. With this false, the transparency sliders' handlers ran at
        /// that moment against a null BodyAcrylicValue, and would also have persisted a value read
        /// off a control that had not been restored yet. Anything between InitializeComponent and
        /// LoadAppearanceSettings is by definition not a user edit, so the flag covers that whole
        /// window.
        /// </remarks>
        private bool _loadingSettings = true;

        private readonly Debouncer _searchDebounce = new Debouncer();

        private bool _authHandlerAttached = false;

        private bool _themeHandlerAttached = false;

        private bool _titleBarMetricsHandlerAttached = false;

        private bool _systemBackHandlerAttached = false;

        private bool _navViewInitialized = false;

        private string _navAvatarUrl = null;

        public MainPage()
        {
            this.InitializeComponent();
            AboutVersionText.Text = LocalizedStrings.Format("AboutVersionFormat", AppConstants.AppVersionDisplay);
            SetupTitleBar();
            UpdateProfileNavItem();
            LoadAppearanceSettings();

            // The sitemap load is queued at Low with the tile push, for the reason App.OnLaunched
            // queues session restore there: started inline, its continuation (building ~300
            // SearchItems) interleaved with the first layout pass and made the window stutter as it
            // appeared. Search works in the meantime off the static items _allSearchItems starts
            // with, and GamesPage loads through SitemapService on its own if it gets there first.
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                              () => LoadSitemapItems());
            ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                          () => UpdateLiveTile());

            Unloaded += MainPage_Unloaded;
            Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }

            if (!_themeHandlerAttached)
            {
                ActualThemeChanged += OnActualThemeChanged;
                _themeHandlerAttached = true;
            }

            if (!_titleBarMetricsHandlerAttached)
            {
                var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
                coreTitleBar.LayoutMetricsChanged += OnTitleBarLayoutMetricsChanged;
                _titleBarMetricsHandlerAttached = true;

                ApplyTitleBarLayoutMetrics(coreTitleBar);
            }

            if (!_systemBackHandlerAttached)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += OnSystemBackRequested;
                _systemBackHandlerAttached = true;
            }

            AttachFavoritesHandler();

            UpdateProfileNavItem();
        }

        private async void LoadSitemapItems()
        {
            try
            {
                SetSitemapLoadingIndicator(true);

                var sitemapItems = await SitemapService.LoadSearchItemsAsync();
                var staticItems = SearchCatalog.GetStaticItems();
                List<SearchItem> combined = new List<SearchItem>(staticItems.Length + sitemapItems.Count);
                combined.AddRange(staticItems);
                combined.AddRange(sitemapItems);
                _allSearchItems = combined;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to load sitemap items – {ex.Message}");
            }
            finally
            {
                SetSitemapLoadingIndicator(false);

                // In the finally, not the try: a failed sitemap load still has to resolve the
                // favorites section, or Home renders its heading over nothing at all rather than
                // over the empty-state hint.
                InitializeFavorites();
            }
        }

        private void SetSitemapLoadingIndicator(bool active)
        {
            if (LoadingIndicator == null) return;
            LoadingIndicator.IsIndeterminate = active;
            LoadingIndicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachFavoritesHandler();

            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }

            if (_themeHandlerAttached)
            {
                ActualThemeChanged -= OnActualThemeChanged;
                _themeHandlerAttached = false;
            }

            if (_titleBarMetricsHandlerAttached)
            {
                try
                {
                    CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged -= OnTitleBarLayoutMetricsChanged;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Failed to remove title bar metrics handler - {ex.Message}");
                }
                _titleBarMetricsHandlerAttached = false;
            }

            if (_systemBackHandlerAttached)
            {
                try
                {
                    SystemNavigationManager.GetForCurrentView().BackRequested -= OnSystemBackRequested;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Failed to remove system back handler - {ex.Message}");
                }
                _systemBackHandlerAttached = false;
            }

            _searchDebounce.Cancel();
        }
    }
}
