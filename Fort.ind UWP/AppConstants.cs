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
        public const string SettingTileShowsNotifications = "TileShowsNotifications";
        public const string SettingLockScreenShowsSenders = "LockScreenShowsSenders";
        public const string SettingSocialTileShown = "SocialTileShown";
        public const string SettingSettingsWelcomeExpanded = "SettingsWelcomeExpanded";
        public const string SettingSettingsAboutExpanded = "SettingsAboutExpanded";
        public const string SettingSettingsProfileExpanded = "SettingsProfileExpanded";
        public const string SettingSettingsNotificationsExpanded = "SettingsNotificationsExpanded";
        public const string SettingLastNavTag = "LastNavTag";

        public const string SettingsSectionAppearance = "Appearance";
        public const string SettingsSectionTransparency = "Transparency";
        public const string SettingsSectionProfile = "Profile";
        public const string SettingsSectionNotifications = "Notifications";
        public const string SettingsSectionStorage = "Storage";
        public const string SettingsSectionTile = "Tile";
        public const string SettingsSectionWelcome = "Welcome";
        public const string SettingsSectionAbout = "About";

        public const string SettingCheckForUpdates = "CheckForUpdates";
        public const string SettingUpdateLastCheckedUtc = "UpdateLastCheckedUtc";
        public const string SettingUpdateLatestVersion = "UpdateLatestVersion";
        public const string SettingUpdateDismissedVersion = "UpdateDismissedVersion";
        public const int UpdateCheckIntervalHours = 24;

        public const string SettingSocialNotificationsEnabled = "SocialNotificationsEnabled";
        public const string SettingSocialNotificationsAccount = "SocialNotificationsAccount";
        public const string SettingSocialSignInAgainDismissed = "SocialSignInAgainDismissed";

        public const string SettingSocialToastedThrough = "SocialNotificationsToastedThrough";
        public const string SettingSocialToastedIds = "SocialNotificationsToastedIds";
        public const int SocialToastedIdLimit = 32;
        public const string SettingBackgroundAccessVersion = "BackgroundAccessAppVersion";
        public const string SocialCheckTaskName = "SocialNotificationsCheck";
        public const int SocialCheckMinimumMinutes = 15;
        public const string SettingSocialBackgroundCheck = "SocialBackgroundCheck";
        public const string SettingSocialCheckRegisteredMinutes = "SocialCheckRegisteredMinutes";
        public const string SocialToastGroup = "social";
        public const int SocialToastIndividualLimit = 4;
        public const int SocialFeedPageSize = 20;
        public const int SocialTilePreviewLimit = 5;
        public const int SocialTileListLimit = 3;
        public const int SocialLinkPreviewCacheLimit = 200;
        public const int SocialLinkPreviewConcurrency = 4;
        public const int SocialMediaGridLimit = 4;
        public const int SocialLightboxEnterMilliseconds = 300;
        public const int SocialLightboxExitMilliseconds = 150;
        public const int UnusedPageUnloadSeconds = 60;
        public const int HiddenWindowTrimSeconds = 60;

        public const int SocialThreadReplyDepthLimit = 4;
        public const int SocialThreadPageSize = 20;
        public const int SocialThreadChildPageSize = 10;
        public const int SocialThreadPrefetchBudget = 12;
        public const int SocialThreadPrefetchConcurrency = 4;
        public const int SocialConversationLimit = 30;
        public const int SocialNoteCaptureLimit = 50;
        public const int SocialNoteCaptureLingerSeconds = 30;
        public const double SocialThreadIndent = 16;
        public const int SocialReactionsTabPageSize = 20;
        public const int SocialNoteStateCacheLimit = 200;

        public const int SocialRecentPostLimit = 10;

        public const int SocialNoteMaxLength = 3000;
        public const int SocialWarningMaxLength = 500;
        public const int SocialAltTextMaxLength = 20000;
        public const int SocialAttachmentLimit = 16;
        public const ulong SocialUploadLimitBytes = 25UL * 1024 * 1024;
        public const string SocialDraftsFileName = "social-drafts.json";
        public const int SocialDraftSaveDelayMilliseconds = 800;
        public const int SocialComposeChangeDelayMilliseconds = 500;
        public const int SocialMentionResolveDelayMilliseconds = 1000;
        public const int SocialSuggestDelayMilliseconds = 250;
        public const int SocialComposeSizeSaveDelayMilliseconds = 500;
        public const string WindowKeyCompose = "compose";
        public const double SocialComposeWindowWidth = 520;
        public const double SocialComposeWindowHeight = 600;
        public const double SocialComposeMinWidth = 360;
        public const double SocialComposeMinHeight = 400;
        public const string SettingSocialLastVisibility = "SocialLastVisibility";
        public const string SettingSocialLastLocalOnly = "SocialLastLocalOnly";
        public const string SettingSocialComposeWindowSize = "SocialComposeWindowSize";

        public const string SettingSocialRecentReactions = "SocialRecentReactions";
        public const string SettingSocialEmojiSkinTone = "SocialEmojiSkinTone";
        public const string SettingSocialWriteSignInDismissed = "SocialWriteSignInDismissed";

        public const string SettingSocialRemoteListNotice = "SocialRemoteListNotice";

        public const string WindowKeyUserPrefix = "user:";
        public const double SocialUserWindowWidth = 480;
        public const double SocialUserWindowHeight = 760;
        public const double SocialWindowMinWidth = 360;
        public const double SocialWindowMinHeight = 500;

        public const string ToastArgumentOpen = "open";
        public const string ToastOpenNotifications = "notifications";
        public const string ToastOpenNote = "note";
        public const string ToastArgumentNote = "note";
        public const string ToastArgumentAccount = "account";
        public const string ToastArgumentRestore = "restore";
        public const string ToastArgumentAction = "action";
        public const string ToastActionReply = "reply";
        public const string ToastActionLike = "like";
        public const string ToastReplyInputId = "reply";
        public const string SocialToastActionTaskName = "SocialToastAction";
        public const string SocialToastFailureGroup = "socialFailed";
        public const string SettingSocialToastFailedReply = "SocialToastFailedReply";
        public const int SocialToastFailedReplyLimit = 3500;
        public const string SocialToastSendIcon = "ms-appx:///Assets/Toast/Send.png";

        public const double KeepOnTopWindowWidth = 360;
        public const double KeepOnTopWindowHeight = 480;

        public const string SettingProfileAutoRefresh = "ProfileAutoRefresh";
        public const string SettingProfileRefreshMinutes = "ProfileRefreshMinutes";
        public const int DefaultProfileRefreshMinutes = 5;

        public const string SettingMediaCacheLimitMegabytes = "MediaCacheLimitMegabytes";
        public const int DefaultMediaCacheLimitMegabytes = 500;
        public const int MediaCacheCheckDelaySeconds = 10;

        public const string JumpArgumentPrefix = "jump:";
        public const string JumpTaskCompose = "compose";

        public const string GameTileArgumentPrefix = "game:";
        public const string GameTileIdPrefix = "game.";
        public const string SettingGameTileArtRevision = "GameTileArtRevision";

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
