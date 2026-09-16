using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class GamesPage : Page
    {
        private const string DigitGroupKey = "#";

        private enum GamesPageState
        {
            Loading,
            Content,
            Empty,
            Failed
        }

        private readonly GroupCollection _groups = new GroupCollection();

        /// <summary>
        /// An ObservableCollection whose contents can be swapped with a single Reset.
        /// </summary>
        /// <remarks>
        /// The filter used to Clear() and then Add each group, which is one Reset followed by up to
        /// 27 Add notifications per debounced keystroke - and the grouped view and both
        /// SemanticZoom views react to every one of them. The instance is still mutated in place
        /// rather than replaced, because GamesViewSource.Source and both ItemsSources hold it (see
        /// EnsureItemsSources).
        /// </remarks>
        private sealed class GroupCollection : ObservableCollection<GameGroup>
        {
            public void ReplaceAll(IEnumerable<GameGroup> groups)
            {
                CheckReentrancy();

                Items.Clear();
                foreach (var group in groups)
                {
                    Items.Add(group);
                }

                OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
                OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                    System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            }
        }

        private readonly CollectionViewSource _viewSource;

        private IReadOnlyList<SearchItem> _allGames = Array.Empty<SearchItem>();

        /// <summary>
        /// The shared debouncer, not a hand-rolled CancellationTokenSource field. It already gets
        /// the ordering right - Cancel nulls the field before disposing, so a late caller can
        /// never reach a dead source - and MainPage's search box uses the same one.
        /// </summary>
        private readonly Debouncer _filterDebounce = new Debouncer();

        private bool _dataLoaded = false;

        private bool _loadInProgress = false;

        public GamesPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;

            _viewSource = (CollectionViewSource)Resources["GamesViewSource"];
            _viewSource.Source = _groups;
            EnsureItemsSources();

            AddSemanticZoomAccelerators();

            Loaded += GamesPage_Loaded;
            Unloaded += GamesPage_Unloaded;
        }

        private void AddSemanticZoomAccelerators()
        {
            const int VirtualKeyOemPlus = 187;
            const int VirtualKeyOemMinus = 189;

            AddAccelerator(VirtualKey.Add, ZoomInAccelerator_Invoked);
            AddAccelerator((VirtualKey)VirtualKeyOemPlus, ZoomInAccelerator_Invoked);
            AddAccelerator(VirtualKey.Subtract, ZoomOutAccelerator_Invoked);
            AddAccelerator((VirtualKey)VirtualKeyOemMinus, ZoomOutAccelerator_Invoked);
        }

        private void AddAccelerator(VirtualKey key, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
        {
            var accelerator = new KeyboardAccelerator();
            accelerator.Modifiers = VirtualKeyModifiers.Control;
            accelerator.Key = key;
            accelerator.Invoked += handler;
            KeyboardAccelerators.Add(accelerator);
        }

        private void EnsureItemsSources()
        {
            var view = _viewSource.View;
            if (view == null) return;

            if (GamesList.ItemsSource == null)
            {
                GamesList.ItemsSource = view;
            }
            if (GamesJumpGrid.ItemsSource == null)
            {
                GamesJumpGrid.ItemsSource = view.CollectionGroups;
            }
        }

        private void GamesPage_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureItemsSources();

            if (_dataLoaded) return;
            LoadGames();
        }

        private void GamesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _filterDebounce.Cancel();
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadGames();
        }

        private async void LoadGames()
        {
            if (_loadInProgress) return;
            _loadInProgress = true;

            try
            {
                SetState(GamesPageState.Loading);

                var games = await SitemapService.LoadGameItemsAsync();

                // Stamp the stars on before the list is bound, so no row renders unstarred and
                // then flips. Idempotent, and cheap enough to repeat on a retry.
                await FavoritesService.EnsureLoadedAsync();
                FavoritesService.Apply(games);

                List<SearchItem> sorted = new List<SearchItem>(games);
                sorted.Sort((left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
                _allGames = sorted.AsReadOnly();
                _dataLoaded = true;

                ApplyFilter(FilterBox.Text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Failed to load games - {ex.Message}");
                SetState(GamesPageState.Failed);
            }
            finally
            {
                _loadInProgress = false;
            }
        }

        private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            ApplyFilterDebounced(sender.Text, _filterDebounce.Restart());
        }

        private async void ApplyFilterDebounced(string query, CancellationToken cancellationToken)
        {
            try
            {
                // Untokenized on purpose - see the note on the same call in MainPage.Search.cs.
                // The token would make every superseded keystroke throw TaskCanceledException;
                // the check below is what actually stops the stale filter.
                await Task.Delay(AppConstants.SearchDebounceMilliseconds);

                if (cancellationToken.IsCancellationRequested) return;
                if (!_dataLoaded) return;

                ApplyFilter(query);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Filter failed - {ex.Message}");
            }
        }

        private void ApplyFilter(string query)
        {
            var trimmed = (query ?? string.Empty).Trim();

            List<SearchItem> matches;
            if (trimmed.Length == 0)
            {
                matches = new List<SearchItem>(_allGames);
            }
            else
            {
                matches = new List<SearchItem>();
                foreach (var item in _allGames)
                {
                    if (item.Title.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches.Add(item);
                    }
                }
            }

            RebuildGroups(matches);
            UpdateCountText(matches.Count);

            if (_allGames.Count == 0)
            {
                SetState(GamesPageState.Failed);
            }
            else if (matches.Count == 0)
            {
                EmptyText.Text = LocalizedStrings.Format("GamesEmptyFilterFormat", trimmed);
                SetState(GamesPageState.Empty);
            }
            else
            {
                SetState(GamesPageState.Content);
            }
        }

        private void RebuildGroups(IEnumerable<SearchItem> matches)
        {
            if (!GamesZoom.IsZoomedInViewActive)
            {
                GamesZoom.IsZoomedInViewActive = true;
            }

            _groups.ReplaceAll(BuildGroups(matches));
        }

        private static List<GameGroup> BuildGroups(IEnumerable<SearchItem> items)
        {
            Dictionary<string, GameGroup> lookup = new Dictionary<string, GameGroup>(StringComparer.Ordinal);
            List<GameGroup> ordered = new List<GameGroup>();

            foreach (var item in items)
            {
                var key = GroupKeyFor(item.Title);
                GameGroup group = null;
                if (!lookup.TryGetValue(key, out group))
                {
                    group = new GameGroup(key);
                    lookup.Add(key, group);
                    ordered.Add(group);
                }
                group.Items.Add(item);
            }

            ordered.Sort((left, right) => CompareGroupKeys(left.Key, right.Key));
            return ordered;
        }

        private static string GroupKeyFor(string title)
        {
            if (string.IsNullOrEmpty(title)) return DigitGroupKey;
            var first = char.ToUpperInvariant(title[0]);
            if (first >= 'A' && first <= 'Z') return first.ToString();
            return DigitGroupKey;
        }

        private static int CompareGroupKeys(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal)) return 0;
            if (string.Equals(left, DigitGroupKey, StringComparison.Ordinal)) return -1;
            if (string.Equals(right, DigitGroupKey, StringComparison.Ordinal)) return 1;
            return string.Compare(left, right, StringComparison.Ordinal);
        }

        private void UpdateCountText(int shownCount)
        {
            var total = _allGames.Count;

            if (shownCount != total)
            {
                CountText.Text = LocalizedStrings.Format("GamesCountFilteredFormat", shownCount, total);
            }
            else if (total == 1)
            {
                CountText.Text = LocalizedStrings.Get("GamesCountOne");
            }
            else
            {
                CountText.Text = LocalizedStrings.Format("GamesCountAllFormat", total);
            }
        }

        private void SetState(GamesPageState state)
        {
            GamesZoom.Visibility = state == GamesPageState.Content ? Visibility.Visible : Visibility.Collapsed;
            LoadingPanel.Visibility = state == GamesPageState.Loading ? Visibility.Visible : Visibility.Collapsed;
            EmptyPanel.Visibility = state == GamesPageState.Empty ? Visibility.Visible : Visibility.Collapsed;
            ErrorPanel.Visibility = state == GamesPageState.Failed ? Visibility.Visible : Visibility.Collapsed;

            LoadingRing.IsActive = (state == GamesPageState.Loading);
            FilterBox.IsEnabled = (state == GamesPageState.Content || state == GamesPageState.Empty);

            AnnounceState(state);
        }

        /// <summary>
        /// Speaks whichever state text has just become the live one.
        /// </summary>
        /// <remarks>
        /// The AutomationProperties.LiveSetting values in the markup only declare a politeness
        /// level - nothing at all is announced until LiveRegionChanged is raised, which is what
        /// AutomationHelper.AnnounceLiveRegion does. Driven from SetState because that is the one
        /// point every transition passes through, and each text is written before it runs:
        /// UpdateCountText precedes it and ApplyFilter sets EmptyText.Text first.
        ///
        /// Content announces the count, which is the whole reason CountText carries Polite - a
        /// debounced filter rewrites it with no focus change and no new element, so it is
        /// otherwise silent. ErrorPanel has no live region of its own, so Failed says nothing yet.
        /// </remarks>
        private void AnnounceState(GamesPageState state)
        {
            switch (state)
            {
                case GamesPageState.Loading:
                    AutomationHelper.AnnounceLiveRegion(LoadingText);
                    break;
                case GamesPageState.Content:
                    AutomationHelper.AnnounceLiveRegion(CountText);
                    break;
                case GamesPageState.Empty:
                    AutomationHelper.AnnounceLiveRegion(EmptyText);
                    break;
            }
        }

        private async void GamesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SearchItem;
                if (item == null || string.IsNullOrEmpty(item.Url)) return;

                await WebLauncher.LaunchAsync(item.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Failed to launch game - {ex.Message}");
            }
        }

        private void FavoriteToggle_Checked(object sender, RoutedEventArgs e)
        {
            SetFavoriteFromToggle(sender, true);
        }

        private void FavoriteToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SetFavoriteFromToggle(sender, false);
        }

        /// <summary>
        /// Shared body of the two toggle handlers.
        /// </summary>
        /// <remarks>
        /// The IsChecked binding fires Checked/Unchecked as containers are recycled during
        /// scrolling, not just when the user clicks - so this no-ops when the model already agrees
        /// with the requested state. Without that guard every scroll would re-save the file and
        /// re-announce to a screen reader. FavoritesService.SetFavoriteAsync is idempotent as well,
        /// but the guard keeps the async churn off the scroll path entirely.
        /// </remarks>
        private async void SetFavoriteFromToggle(object sender, bool isFavorite)
        {
            try
            {
                var toggle = sender as FrameworkElement;
                if (toggle == null) return;

                var item = toggle.DataContext as SearchItem;
                if (item == null) return;

                if (item.IsFavorite == isFavorite) return;

                await FavoritesService.SetFavoriteAsync(item, isFavorite);

                // Starring changes no text and moves no focus, so it is silent to a screen reader
                // without an explicit notification.
                AutomationHelper.AnnounceStatus(
                    toggle,
                    LocalizedStrings.Format(isFavorite ? "FavoriteAddedFormat" : "FavoriteRemovedFormat", item.Title),
                    "GamesFavoriteToggle");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Failed to toggle favorite - {ex.Message}");
            }
        }

        private void ZoomOutAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomedInViewActive(false, args);
        }

        private void ZoomInAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomedInViewActive(true, args);
        }

        private void SetZoomedInViewActive(bool zoomedIn, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                if (GamesZoom.Visibility != Visibility.Visible) return;
                if (GamesZoom.IsZoomedInViewActive == zoomedIn)
                {
                    args.Handled = true;
                    return;
                }

                GamesZoom.IsZoomedInViewActive = zoomedIn;
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GamesPage: Semantic zoom accelerator failed - {ex.Message}");
            }
        }
    }
}
