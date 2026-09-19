using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private void FocusSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                NavSearchBox.Focus(FocusState.Keyboard);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Focus search accelerator failed - {ex.Message}");
            }
        }

        private void ClearSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                if (string.IsNullOrEmpty(NavSearchBox.Text)) return;

                _searchDebounce.Cancel();
                NavSearchBox.Text = "";
                NavSearchBox.ItemsSource = null;
                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Clear search accelerator failed - {ex.Message}");
            }
        }

        private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();

                if (string.IsNullOrEmpty(query))
                {
                    _searchDebounce.Cancel();
                    sender.ItemsSource = null;
                    return;
                }

                ApplySearchSuggestions(sender, query, _searchDebounce.Restart());
            }
        }

        private async void ApplySearchSuggestions(AutoSuggestBox sender, string query, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SearchDebounceMilliseconds);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var snapshot = _allSearchItems;

                var profileResult = SearchCatalog.BuildProfileResultItem(ProfileService.CurrentUser);

                var results = await Task.Run(() => SearchCatalog.BuildSuggestions(query, snapshot, profileResult), cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    sender.ItemsSource = results;
                    AnnounceSearchResultCount(results.Count);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("MainPage: search superseded before it started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Debounced search failed – {ex.Message}");
            }
        }

        private void AnnounceSearchResultCount(int count)
        {
            string message;
            if (count == 0)
            {
                message = LocalizedStrings.Get("SearchResultsNone");
            }
            else if (count == 1)
            {
                message = LocalizedStrings.Get("SearchResultsOne");
            }
            else
            {
                message = LocalizedStrings.Format("SearchResultsCountFormat", count);
            }

            AutomationHelper.AnnounceStatus(NavSearchBox, message, "FortIndSearchResults");
        }

        private async void NavSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                _searchDebounce.Cancel();

                if (args.ChosenSuggestion != null)
                {
                    var item = args.ChosenSuggestion as SearchItem;
                    if (item != null)
                    {
                        await NavigateToSearchItemAsync(item);
                    }
                }
                else
                {
                    var query = (args.QueryText ?? "").Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        await SubmitFreeTextQueryAsync(query);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Search query failed – {ex.Message}");
            }
        }

        private async Task SubmitFreeTextQueryAsync(string query)
        {
            var destinations = SearchCatalog.GetStaticItems();

            var exact = FirstDestination(destinations,
                                         item => string.Equals(item.Title, query, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                NavigateToTag(exact.NavigationTag, exact.SettingsSection);
                return;
            }

            if (!await AnyGameMatchesAsync(query))
            {
                var partial = FirstDestination(destinations, item => SearchCatalog.Matches(item, query));
                if (partial != null)
                {
                    NavigateToTag(partial.NavigationTag, partial.SettingsSection);
                    return;
                }
            }

            NavigateToTag(AppConstants.NavigationGames);

            var games = ContentFrame.Content as GamesPage;
            if (games != null)
            {
                games.ShowFiltered(query);
            }
        }

        private static SearchItem FirstDestination(IEnumerable<SearchItem> destinations, Func<SearchItem, bool> predicate)
        {
            foreach (var item in destinations)
            {
                if (!string.IsNullOrEmpty(item.NavigationTag) && predicate(item))
                {
                    return item;
                }
            }

            return null;
        }

        private static async Task<bool> AnyGameMatchesAsync(string query)
        {
            var games = await SitemapService.LoadGameItemsAsync();
            foreach (var game in games)
            {
                if (SearchCatalog.Matches(game, query))
                {
                    return true;
                }
            }

            return false;
        }

        private void NavSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var item = args.SelectedItem as SearchItem;
            if (item != null)
            {
                sender.Text = item.Title;
            }
        }

        private async Task NavigateToSearchItemAsync(SearchItem item)
        {
            if (item == null)
            {
                Debug.WriteLine("MainPage: NavigateToSearchItemAsync called with null item");
                return;
            }

            if (!string.IsNullOrEmpty(item.Url))
            {
                await WebLauncher.LaunchAsync(item.Url);
            }
            else if (!string.IsNullOrEmpty(item.NavigationTag))
            {
                NavigateToTag(item.NavigationTag, item.SettingsSection);
            }
        }
    }
}
