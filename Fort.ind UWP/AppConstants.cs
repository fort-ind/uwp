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

        // The one-shot TeachingTip pointing at LoginPage's skip link. Kept here with the rest of
        // them: the value is the literal LoginPage shipped with, so existing installs that have
        // already seen the tip keep their state.
        public const string SettingHasSeenSkipSignInTip = "HasSeenSkipSignInTip";

        public const string SettingAppTheme = "AppTheme";
        public const string SettingAppTintColor = "AppTintColor";
        public const string SettingAppCustomTintColor = "AppCustomTintColor";

        // "Default" (the Windows accent), AccentMatchTint, or a #RRGGBB colour.
        public const string SettingAppAccentColor = "AppAccentColor";
        public const string SettingAppCustomAccentColor = "AppCustomAccentColor";
        public const string AccentMatchTint = "MatchTint";
        public const string SettingSettingsAppearanceExpanded = "SettingsAppearanceExpanded";
        public const string SettingSettingsStorageExpanded = "SettingsStorageExpanded";
        public const string SettingSettingsTileExpanded = "SettingsTileExpanded";
        public const string SettingShowTileBadge = "ShowTileBadge";
        public const string SettingLiveTileCleared = "LiveTileCleared";
        public const string SettingSettingsWelcomeExpanded = "SettingsWelcomeExpanded";
        public const string SettingSettingsAboutExpanded = "SettingsAboutExpanded";
        public const string SettingLastNavTag = "LastNavTag";

        public const string JumpArgumentPrefix = "jump:";

        public const string SettingJumpListRevision = "JumpListRevision";

        // Set once AvatarIconService has swept LocalFolder for avatar PNGs left by builds that
        // never pruned. After that, pruning only follows a write.
        public const string SettingAvatarLegacySweepDone = "AvatarLegacySweepDone";

        public const int SearchDebounceMilliseconds = 300;
        public const int SearchSuggestionLimit = 15;

        // Legacy: the on-disk sitemap URL cache these named has been removed. Kept only so
        // SitemapService can delete what older builds left behind.
        public const string LegacySitemapCacheFileName = "sitemap_urls.cache";
        public const string LegacySitemapCacheTimestampKey = "SitemapCacheUnixSeconds";
        public const string LegacySitemapCacheAppVersionKey = "SitemapCacheAppVersion";

        public const string FavoritesFileName = "favorites.json";

        // Home shares one ScrollViewer with the news cards above it, so the favorites section is
        // capped rather than allowed to grow without bound. Ordered newest-first (not
        // alphabetically) so a game starting late in the alphabet can still reach the cap.
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
                numeric = "2.2.0";
            }

            return string.IsNullOrEmpty(VersionChannel) ? numeric : $"{numeric} {VersionChannel}";
        }
    }
}
