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

        // AcrylicBrush.TintOpacity for the two surfaces the user can tune, stored as 0..1.
        public const string SettingAppBodyAcrylicOpacity = "AppBodyAcrylicOpacity";
        public const string SettingAppPaneAcrylicOpacity = "AppPaneAcrylicOpacity";

        // Which surfaces the chosen background tint colour is painted onto: one of the
        // TintScope* values below.
        public const string SettingAppTintScope = "AppTintScope";

        public const string TintScopeContent = "Content";
        public const string TintScopeSidebar = "Sidebar";
        public const string TintScopeBoth = "Both";

        // Content-only is what the app did before the scope existed, so an existing install
        // looks unchanged after the update. Nothing else depends on which value this is.
        public const string TintScopeDefault = TintScopeContent;

        // The values these two replaced: 0.8 is the window surface MainPage.Appearance.cs used
        // to hardcode and the doc dump's general-purpose default (chunk_030, "Acrylic theme
        // resources"); 0.9 is what App.xaml's pane brushes shipped with.
        public const double DefaultBodyAcrylicOpacity = 0.8;
        public const double DefaultPaneAcrylicOpacity = 0.9;

        // The lowest tint opacity the sliders offer. Not a legibility threshold - that is the pair
        // below, and the warning still covers 20-70% in dark theme. This is the point past which a
        // HostBackdrop surface stops reading as a surface at all: with almost no tint left it is a
        // clear window onto whatever is behind the app. Background acrylic also falls back to a
        // solid FallbackColor whenever the window deactivates (chunk_030, "Usability and
        // adaptability"), so a near-zero value gave the harshest possible pairing - invisible while
        // focused, fully solid the moment the window lost focus.
        //
        // LoadAcrylicSettings pushes this onto both sliders' Minimum and ReadOpacity clamps to it,
        // so a value saved by a build that allowed less migrates up on load instead of leaving the
        // slider and the painted surface disagreeing.
        public const double MinimumAcrylicOpacity = 0.20;

        // Below these, text over acrylic stops meeting contrast ratios and the settings panel
        // says so. chunk_030, "Legibility considerations": "In dark mode, tint opacity can be
        // 70%, while light mode acrylic will meet contrast ratios at 50%."
        public const double AcrylicLegibilityFloorDark = 0.70;
        public const double AcrylicLegibilityFloorLight = 0.50;

        // A slider drag raises ValueChanged continuously. The brushes are repainted on every
        // one of those (a dependency property set, which is cheap); only the LocalSettings
        // write waits for the drag to settle.
        public const int AcrylicPersistDebounceMilliseconds = 400;
        public const string SettingSettingsAppearanceExpanded = "SettingsAppearanceExpanded";
        public const string SettingSettingsTransparencyExpanded = "SettingsTransparencyExpanded";
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
