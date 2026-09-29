using System;
using System.Diagnostics;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class LabsService
    {
        public static bool SocialNotificationsEnabled
        {
            get { return ReadFlag(AppConstants.SettingLabSocialNotifications); }
        }

        public static void SaveSocialNotifications(bool enabled)
        {
            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLabSocialNotifications] = enabled;
        }

        private static bool ReadFlag(string key)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                return values.ContainsKey(key) && Convert.ToBoolean(values[key]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LabsService: could not read {key} - {ex.Message}");
                return false;
            }
        }
    }
}
