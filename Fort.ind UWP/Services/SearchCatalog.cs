using System;
using System.Collections.Generic;

namespace Fort.ind_UWP
{
    public static class SearchCatalog
    {
        private static SearchItem[] s_staticItems;

        public static SearchItem[] GetStaticItems()
        {
            var cached = s_staticItems;
            if (cached != null) return cached;

            var items = BuildStaticItems();

            if (LocalizedStrings.IsAvailable)
            {
                s_staticItems = items;
            }

            return items;
        }

        private static SearchItem[] BuildStaticItems()
        {
            return new SearchItem[]
            {
                new SearchItem(LocalizedStrings.Get("SearchItemHome"), AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
                new SearchItem(LocalizedStrings.Get("SearchItemLatestNews"), AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
                new SearchItem(LocalizedStrings.Get("SearchItemGames"), AppConstants.CategoryMenu, AppConstants.NavigationGames),
                new SearchItem(LocalizedStrings.Get("SearchItemBetaPrograms"), AppConstants.CategoryMenu, AppConstants.NavigationBetas),
                new SearchItem(LocalizedStrings.Get("SearchItemYourProfile"), AppConstants.CategoryMenu, AppConstants.NavigationProfile),
                new SearchItem(LocalizedStrings.Get("SearchItemSocial"), AppConstants.CategoryMenu, AppConstants.NavigationSocial),
                new SearchItem(LocalizedStrings.Get("SearchItemSettings"), AppConstants.CategoryMenu, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemDataStorage"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemLocalJsonStorage"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemLiveTile"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemRefreshLiveTile"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemClearLiveTile"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemWelcomeDialog"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemShowWelcomeDialogAgain"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemAppearance"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemTheme"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemDarkMode"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemLightMode"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemBackgroundColor"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemBackgroundTint"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemApplyTintTo"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemTransparency"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemCustomBackgroundTint"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemAccentColor"), AppConstants.CategorySettings, AppConstants.NavigationSettings),
                new SearchItem(LocalizedStrings.Get("SearchItemAccount"), AppConstants.CategoryProfile, AppConstants.NavigationProfile),
                new SearchItem(LocalizedStrings.Get("SearchItemSignIn"), AppConstants.CategoryProfile, AppConstants.NavigationProfile)
            };
        }

        public static SearchItem BuildProfileResultItem(UserProfile currentUser)
        {
            if (currentUser == null) return null;

            var name = string.IsNullOrWhiteSpace(currentUser.DisplayName)
                       ? currentUser.Username
                       : currentUser.DisplayName;

            return new SearchItem(LocalizedStrings.Format("SearchProfileResultFormat", name),
                                  AppConstants.CategoryProfile,
                                  AppConstants.NavigationProfile);
        }

        public static List<SearchItem> BuildSuggestions(string query, IReadOnlyList<SearchItem> items, SearchItem profileResult)
        {
            List<SearchItem> filtered = new List<SearchItem>();
            foreach (var item in items)
            {
                if (Matches(item, query))
                {
                    filtered.Add(item);
                    if (filtered.Count >= AppConstants.SearchSuggestionLimit)
                    {
                        break;
                    }
                }
            }

            if (filtered.Count < AppConstants.SearchSuggestionLimit &&
                profileResult != null &&
                Matches(profileResult, query))
            {
                filtered.Add(profileResult);
            }

            return filtered;
        }

        private static bool Matches(SearchItem item, string query)
        {
            if (item == null) return false;

            return (item.Title != null && item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (item.Category != null && item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
