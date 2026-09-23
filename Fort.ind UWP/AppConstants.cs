namespace Fort.ind_UWP
{
    public sealed class AppConstants
    {
        private AppConstants()
        {
        }

        
        public const string CategoryMenu = "Menu";
        public const string CategorySettings = "Settings";
        public const string CategoryProfile = "Profile";
        public const string CategoryGames = "Games";
        public const string CategoryGamesHtml = "Games.Html";
        public const string CategoryGamesFlash = "Games.Flash";
        public const string CategoryGamesCodePen = "Games.CodePen";
        public const string CategoryGamesRetro = "Games.Retro";
        public const string CategoryGamesMinecraft = "Games.Minecraft";
        public const string CategorySocial = "Social";
        public const string CategoryEmulators = "Emulators";
        public const string CategoryApps = "Apps";
        public const string CategoryAppsAppStone = "Apps.AppStone";
        public const string CategoryExtras = "Extras";
        public const string CategoryLabsAndBetas = "Labs & Betas";
        public const string CategoryFortWebsite = "fort1nd.com";

        public const string NavigationLatestNews = "LatestNews";
        public const string NavigationGames = "Games";
        public const string NavigationBetas = "Betas";
        public const string NavigationProfile = "Profile";
        public const string NavigationSocial = "Social";
        public const string NavigationSettings = "Settings";

        public const string ThemeDefault = "Default";
        public const string ThemeLight = "Light";
        public const string ThemeDark = "Dark";

        public const string SettingHideWelcomeDialog = "HideWelcomeDialog";

        
        public const string SettingHasSeenSkipSignInTip = "HasSeenSkipSignInTip";

        public const string SettingAppTheme = "AppTheme";
        public const string SettingAppTintColor = "AppTintColor";
        public const string SettingAppCustomTintColor = "AppCustomTintColor";

        
        public const string SettingAppAccentColor = "AppAccentColor";
        public const string SettingAppCustomAccentColor = "AppCustomAccentColor";
        public const string AccentMatchTint = "MatchTint";

        public const string SettingAppBodyAcrylicOpacity = "AppBodyAcrylicOpacity";
        public const string SettingAppPaneAcrylicOpacity = "AppPaneAcrylicOpacity";
        public const string SettingAppTintScope = "AppTintScope";
        public const string TintScopeContent = "Content";
        public const string TintScopeSidebar = "Sidebar";
        public const string TintScopeBoth = "Both";

        
        public const string TintScopeDefault = TintScopeContent;

        
        public const double DefaultBodyAcrylicOpacity = 0.95;
        public const double DefaultPaneAcrylicOpacity = 0.75;

        public const double MinimumAcrylicOpacity = 0.20;

        
        public const double AcrylicLegibilityFloorDark = 0.70;
        public const double AcrylicLegibilityFloorLight = 0.50;

        
        public const int AcrylicPersistDebounceMilliseconds = 400;
        public const string SettingSettingsAppearanceExpanded = "SettingsAppearanceExpanded";
        public const string SettingSettingsTransparencyExpanded = "SettingsTransparencyExpanded";
        public const string SettingSettingsStorageExpanded = "SettingsStorageExpanded";
        public const string SettingSettingsTileExpanded = "SettingsTileExpanded";
        public const string SettingShowTileBadge = "ShowTileBadge";
        public const string SettingLiveTileCleared = "LiveTileCleared";
        public const string SettingSettingsWelcomeExpanded = "SettingsWelcomeExpanded";
        public const string SettingSettingsAboutExpanded = "SettingsAboutExpanded";
        public const string SettingSettingsProfileExpanded = "SettingsProfileExpanded";
        public const string SettingLastNavTag = "LastNavTag";

        public const string SettingsSectionAppearance = "Appearance";
        public const string SettingsSectionTransparency = "Transparency";
        public const string SettingsSectionProfile = "Profile";
        public const string SettingsSectionStorage = "Storage";
        public const string SettingsSectionTile = "Tile";
        public const string SettingsSectionWelcome = "Welcome";
        public const string SettingsSectionAbout = "About";

        public const string SettingCheckForUpdates = "CheckForUpdates";
        public const string SettingUpdateLastCheckedUtc = "UpdateLastCheckedUtc";
        public const string SettingUpdateLatestVersion = "UpdateLatestVersion";
        public const string SettingUpdateDismissedVersion = "UpdateDismissedVersion";
        public const int UpdateCheckIntervalHours = 24;

        public const string SettingLabMultipleViews = "LabMultipleViews";

        public const string SettingProfileAutoRefresh = "ProfileAutoRefresh";
        public const string SettingProfileRefreshMinutes = "ProfileRefreshMinutes";
        public const int DefaultProfileRefreshMinutes = 5;

        public const string JumpArgumentPrefix = "jump:";

        public const string SettingJumpListRevision = "JumpListRevision";

        
        public const string SettingAvatarLegacySweepDone = "AvatarLegacySweepDone";

        public const int SearchDebounceMilliseconds = 300;
        public const int SearchSuggestionLimit = 15;

        public const int ContentBackStackLimit = 20;

        
        public const string LegacySitemapCacheFileName = "sitemap_urls.cache";
        public const string LegacySitemapCacheTimestampKey = "SitemapCacheUnixSeconds";
        public const string LegacySitemapCacheAppVersionKey = "SitemapCacheAppVersion";

        public const string FavoritesFileName = "favorites.json";

        
        public const int HomeFavoritesMaxCount = 8;

        public const string VersionChannel = "";

        public static string AppVersionDisplay
        {
            get
            {
                return s_appVersionDisplay;
            }
        }

        private static readonly string s_appVersionDisplay = ResolveAppVersionDisplay();

        private static string ResolveAppVersionDisplay()
        {
            string numeric;
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                numeric = $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                numeric = "3.0.0";
            }

            return string.IsNullOrEmpty(VersionChannel) ? numeric : $"{numeric} {VersionChannel}";
        }
    }
}
