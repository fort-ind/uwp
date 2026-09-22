using System;
using System.Collections.Generic;
using System.Linq;

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
                SettingsItem("SearchItemDataStorage", AppConstants.SettingsSectionStorage),
                SettingsItem("SearchItemLocalJsonStorage", AppConstants.SettingsSectionStorage),
                SettingsItem("SearchItemLiveTile", AppConstants.SettingsSectionTile),
                SettingsItem("SearchItemRefreshLiveTile", AppConstants.SettingsSectionTile),
                SettingsItem("SearchItemClearLiveTile", AppConstants.SettingsSectionTile),
                SettingsItem("SearchItemWelcomeDialog", AppConstants.SettingsSectionWelcome),
                SettingsItem("SearchItemShowWelcomeDialogAgain", AppConstants.SettingsSectionWelcome),
                SettingsItem("SearchItemAppearance", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemTheme", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemDarkMode", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemLightMode", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemBackgroundColor", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemBackgroundTint", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemApplyTintTo", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemTransparency", AppConstants.SettingsSectionTransparency),
                SettingsItem("SearchItemCustomBackgroundTint", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemAccentColor", AppConstants.SettingsSectionAppearance),
                SettingsItem("SearchItemCheckForUpdates", AppConstants.SettingsSectionAbout),
                SettingsItem("SearchItemProfileSettings", AppConstants.SettingsSectionProfile),
                SettingsItem("SearchItemFollowerCountRefresh", AppConstants.SettingsSectionProfile),
                new SearchItem(LocalizedStrings.Get("SearchItemAccount"), AppConstants.CategoryProfile, AppConstants.NavigationProfile),
                new SearchItem(LocalizedStrings.Get("SearchItemSignIn"), AppConstants.CategoryProfile, AppConstants.NavigationProfile)
            };
        }

        private static SearchItem SettingsItem(string titleKey, string section)
        {
            return new SearchItem(LocalizedStrings.Get(titleKey), AppConstants.CategorySettings, AppConstants.NavigationSettings)
            {
                SettingsSection = section
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
            var filtered = items.Where(item => Matches(item, query))
                                .Take(AppConstants.SearchSuggestionLimit)
                                .ToList();

            if (filtered.Count < AppConstants.SearchSuggestionLimit &&
                profileResult != null &&
                Matches(profileResult, query))
            {
                filtered.Add(profileResult);
            }

            return filtered;
        }

        public static bool Matches(SearchItem item, string query)
        {
            if (item == null) return false;

            return (item.Title != null && item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (item.Category != null && item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
