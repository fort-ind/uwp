using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private async void FocusSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                args.Handled = true;
                if (NavSearchBox.Focus(FocusState.Keyboard)) return;

                NavView.IsPaneOpen = true;
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                NavSearchBox.Focus(FocusState.Keyboard);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Focus search accelerator failed", ex);
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
                AppLog.Error("MainPage: Clear search accelerator failed", ex);
            }
        }

        private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) UpdateSearchSuggestions(sender);
        }

        private void NavSearchInk_TextWritten(object sender, EventArgs e)
        {
            try
            {
                UpdateSearchSuggestions(NavSearchBox);
                NavSearchBox.IsSuggestionListOpen = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not search what was written", ex);
            }
        }

        private void UpdateSearchSuggestions(AutoSuggestBox sender)
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
                var notesResult = SearchCatalog.BuildNoteSearchItem(query);
                var openTag = SearchCatalog.BuildOpenTagItem(query);
                var openTags = openTag == null ? null : new List<SearchItem> { openTag };

                var results = await Task.Run(() => SearchCatalog.BuildSuggestions(query, snapshot, profileResult, notesResult, openTags), cancellationToken);

                if (cancellationToken.IsCancellationRequested) return;

                sender.ItemsSource = results;

                if (SearchCatalog.TagNameOf(query) == null)
                {
                    AnnounceSearchResultCount(results.Count);
                    return;
                }

                var found = await FetchTagNamesAsync(query, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                if (found.Count > 0)
                {
                    var tagItems = SearchCatalog.BuildTagSearchItems(query, found);
                    if (openTags != null) tagItems.InsertRange(0, openTags);

                    if (tagItems.Count > 0)
                    {
                        results = await Task.Run(() => SearchCatalog.BuildSuggestions(query, snapshot, profileResult, notesResult, tagItems), cancellationToken);
                        if (cancellationToken.IsCancellationRequested) return;

                        sender.ItemsSource = results;
                    }
                }

                AnnounceSearchResultCount(results.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("MainPage: search superseded before it started");
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Debounced search failed", ex);
            }
        }

        private static async Task<IReadOnlyList<string>> FetchTagNamesAsync(string query, CancellationToken cancellationToken)
        {
            var name = SearchCatalog.TagNameOf(query);
            if (name == null) return new string[0];

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.SearchHashtagsAsync(token, name, AppConstants.SearchTagSuggestionLimit + 1, cancellationToken);
                return result.Status == SocialApiStatus.Ok ? result.Value : new string[0];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: hashtag suggestions failed", ex);
                return new string[0];
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
                AppLog.Error("MainPage: Search query failed", ex);
            }
        }

        private async Task SubmitFreeTextQueryAsync(string query)
        {
            if (SearchCatalog.IsHashedQuery(query))
            {
                var tag = SearchCatalog.TagNameOf(query);
                if (tag != null)
                {
                    ShowTag(tag);
                    return;
                }
            }

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

            if (ProfileService.CurrentUser != null && query.Length >= 2)
            {
                ShowNoteSearch(query);
                return;
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
            return destinations.FirstOrDefault(item => !string.IsNullOrEmpty(item.NavigationTag) && predicate(item));
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
                var notes = item as NoteSearchItem;
                var tag = item as TagSearchItem;
                sender.Text = notes != null ? notes.Query : tag != null ? "#" + tag.Tag : item.Title;
            }
        }

        private void ShowTag(string tag)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tag)) return;

                if (HoldForUnsavedChanges(() => ShowTag(tag))) return;

                var current = ContentFrame.Content as SocialTagPage;
                if (current != null && current.Shows(tag)) return;

                SelectNavItemForTag(AppConstants.NavigationSocial);
                ClosePaneUnlessExpanded();

                if (!ContentFrame.Navigate(typeof(SocialTagPage), new SocialTagArgs(SocialContentService.CurrentAccountId(), tag)))
                {
                    AppLog.Warning("MainPage: navigation to the hashtag page returned false");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not open the hashtag", ex);
            }
        }

        private void ShowNoteSearch(string query)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                if (account == null || string.IsNullOrWhiteSpace(query)) return;

                if (HoldForUnsavedChanges(() => ShowNoteSearch(query))) return;

                var current = ContentFrame.Content as SocialTagPage;
                if (current != null && current.ShowsSearch(query)) return;

                SelectNavItemForTag(AppConstants.NavigationSocial);
                ClosePaneUnlessExpanded();

                if (!ContentFrame.Navigate(typeof(SocialTagPage), SocialTagArgs.ForSearch(account, query)))
                {
                    AppLog.Warning("MainPage: navigation to the note search returned false");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not open the note search", ex);
            }
        }

        private async Task NavigateToSearchItemAsync(SearchItem item)
        {
            if (item == null)
            {
                Debug.WriteLine("MainPage: NavigateToSearchItemAsync called with null item");
                return;
            }

            var notes = item as NoteSearchItem;
            var tag = item as TagSearchItem;
            if (notes != null)
            {
                ShowNoteSearch(notes.Query);
            }
            else if (tag != null)
            {
                ShowTag(tag.Tag);
            }
            else if (!string.IsNullOrEmpty(item.Url))
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
