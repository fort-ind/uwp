using System;
using System.Linq;

namespace Fort.ind_UWP
{
    public sealed class SettingsCategory
    {
        private static SettingsCategory[] s_all;

        private SettingsCategory(Type pageType, string titleKey, string captionKey, string glyph, params string[] sections)
        {
            PageType = pageType;
            TitleKey = titleKey;
            CaptionKey = captionKey;
            Glyph = glyph;
            Sections = sections;
        }

        public Type PageType { get; private set; }

        public string TitleKey { get; private set; }

        public string CaptionKey { get; private set; }

        public string Glyph { get; private set; }

        public string[] Sections { get; private set; }

        public string Title
        {
            get { return LocalizedStrings.Get(TitleKey); }
        }

        public string Caption
        {
            get { return LocalizedStrings.Get(CaptionKey); }
        }

        public static SettingsCategory[] All
        {
            get
            {
                if (s_all == null)
                {
                    s_all = new[]
                    {
                        new SettingsCategory(typeof(SettingsPersonalizationPage),
                                             "SettingsCategoryPersonalization", "SettingsCategoryPersonalizationCaption", "",
                                             AppConstants.SettingsSectionAppearance, AppConstants.SettingsSectionTransparency,
                                             AppConstants.SettingsSectionInk),
                        new SettingsCategory(typeof(SettingsAccountPage),
                                             "SettingsCategoryAccount", "SettingsCategoryAccountCaption", "",
                                             AppConstants.SettingsSectionProfile, AppConstants.SettingsSectionPrivacy,
                                             AppConstants.SettingsSectionMutedBlocked),
                        new SettingsCategory(typeof(SettingsNotificationsPage),
                                             "SettingsCategoryNotifications", "SettingsCategoryNotificationsCaption", "",
                                             AppConstants.SettingsSectionNotifications, AppConstants.SettingsSectionTile),
                        new SettingsCategory(typeof(SettingsStoragePage),
                                             "SettingsCategoryStorage", "SettingsCategoryStorageCaption", "",
                                             AppConstants.SettingsSectionStorage),
                        new SettingsCategory(typeof(SettingsAboutPage),
                                             "SettingsCategoryAbout", "SettingsCategoryAboutCaption", "",
                                             AppConstants.SettingsSectionAbout, AppConstants.SettingsSectionWelcome)
                    };
                }

                return s_all;
            }
        }

        public static SettingsCategory ForPage(Type pageType)
        {
            return pageType == null ? null : All.FirstOrDefault(category => category.PageType == pageType);
        }

        public static SettingsCategory ForSection(string section)
        {
            if (string.IsNullOrEmpty(section)) return null;

            return All.FirstOrDefault(category => category.Sections.Contains(section, StringComparer.Ordinal));
        }
    }
}
