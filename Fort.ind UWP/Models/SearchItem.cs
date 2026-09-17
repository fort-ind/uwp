using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fort.ind_UWP
{
    public class SearchItem : INotifyPropertyChanged
    {
        private bool _isFavorite;

        public string Title { get; set; }

        public string CategoryKey { get; set; }

        public string Category { get; set; }

        public string NavigationTag { get; set; }

        public string Url { get; set; }

        public string Icon { get; set; }

        public bool IsFavorite
        {
            get { return _isFavorite; }
            set
            {
                if (_isFavorite == value) return;
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged("FavoriteGlyph");
            }
        }

        public string FavoriteGlyph
        {
            get { return _isFavorite ? "\uE735" : "\uE734"; }
        }

        public string FavoriteLabel { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public SearchItem(string title, string categoryKey, string navigationTag, string url = null)
        {
            this.Title = title;
            this.CategoryKey = categoryKey ?? "";
            this.NavigationTag = navigationTag;
            this.Url = url;
            this.Icon = GetIconGlyph(this.CategoryKey);

            this.Category = GetCategoryDisplayName(this.CategoryKey);
            this.FavoriteLabel = LocalizedStrings.FormatPattern(GetFavoriteLabelPattern(), FavoriteLabelKey, this.Title);
        }

        private const string FavoriteLabelKey = "FavoriteToggleNameFormat";

        private static readonly object s_resourceCacheLock = new object();
        private static readonly Dictionary<string, string> s_categoryDisplayNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static string s_favoriteLabelPattern;

        private static string GetFavoriteLabelPattern()
        {
            var cached = s_favoriteLabelPattern;
            if (cached != null) return cached;

            var canCache = LocalizedStrings.IsAvailable;
            var pattern = LocalizedStrings.Get(FavoriteLabelKey);
            if (canCache)
            {
                s_favoriteLabelPattern = pattern;
            }
            return pattern;
        }

        private static string GetIconGlyph(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return "\uE774";
            if (categoryKey == AppConstants.CategoryMenu) return "\uE700";
            if (categoryKey == AppConstants.CategorySettings) return "\uE713";
            if (categoryKey == AppConstants.CategoryProfile) return "\uE77B";
            if (categoryKey.StartsWith(AppConstants.CategoryGames, StringComparison.Ordinal)) return "\uE768";
            if (categoryKey == AppConstants.CategorySocial) return "\uE716";
            if (categoryKey == AppConstants.CategoryEmulators) return "\uE768";
            if (categoryKey.StartsWith(AppConstants.CategoryApps, StringComparison.Ordinal)) return "\uE71D";
            if (categoryKey == AppConstants.CategoryExtras) return "\uE734";
            if (categoryKey == AppConstants.CategoryLabsAndBetas) return "\uE9D9";
            return "\uE774";
        }

        private static string GetCategoryDisplayName(string categoryKey)
        {
            string name;
            lock (s_resourceCacheLock)
            {
                if (s_categoryDisplayNames.TryGetValue(categoryKey, out name)) return name;
            }

            var canCache = LocalizedStrings.IsAvailable;
            name = ResolveCategoryDisplayName(categoryKey);
            if (canCache)
            {
                lock (s_resourceCacheLock)
                {
                    s_categoryDisplayNames[categoryKey] = name;
                }
            }
            return name;
        }

        private static string ResolveCategoryDisplayName(string categoryKey)
        {
            switch (categoryKey)
            {
                case AppConstants.CategoryMenu: return LocalizedStrings.Get("CategoryMenu");
                case AppConstants.CategorySettings: return LocalizedStrings.Get("CategorySettings");
                case AppConstants.CategoryProfile: return LocalizedStrings.Get("CategoryProfile");
                case AppConstants.CategoryGames: return LocalizedStrings.Get("CategoryGames");
                case AppConstants.CategoryGamesHtml: return LocalizedStrings.Get("CategoryGamesHtml");
                case AppConstants.CategoryGamesFlash: return LocalizedStrings.Get("CategoryGamesFlash");
                case AppConstants.CategoryGamesCodePen: return LocalizedStrings.Get("CategoryGamesCodePen");
                case AppConstants.CategoryGamesRetro: return LocalizedStrings.Get("CategoryGamesRetro");
                case AppConstants.CategoryGamesMinecraft: return LocalizedStrings.Get("CategoryGamesMinecraft");
                case AppConstants.CategorySocial: return LocalizedStrings.Get("CategorySocial");
                case AppConstants.CategoryEmulators: return LocalizedStrings.Get("CategoryEmulators");
                case AppConstants.CategoryApps: return LocalizedStrings.Get("CategoryApps");
                case AppConstants.CategoryAppsAppStone: return LocalizedStrings.Get("CategoryAppsAppStone");
                case AppConstants.CategoryExtras: return LocalizedStrings.Get("CategoryExtras");
                case AppConstants.CategoryLabsAndBetas: return LocalizedStrings.Get("CategoryLabsAndBetas");
                case AppConstants.CategoryFortWebsite: return LocalizedStrings.Get("CategoryFortWebsite");
                default: return categoryKey;
            }
        }

        public override string ToString()
        {
            return $"{Title}  —  {Category}";
        }
    }
}
