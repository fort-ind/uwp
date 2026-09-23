using System;
using System.Diagnostics;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class LabsService
    {
        private static readonly object s_lock = new object();

        private static bool? s_multipleViewsActive;

        public static bool MultipleViewsActive
        {
            get
            {
                lock (s_lock)
                {
                    if (!s_multipleViewsActive.HasValue)
                    {
                        s_multipleViewsActive = ReadFlag(AppConstants.SettingLabMultipleViews);
                    }
                    return s_multipleViewsActive.Value;
                }
            }
        }

        public static bool MultipleViewsSaved
        {
            get { return ReadFlag(AppConstants.SettingLabMultipleViews); }
        }

        public static void SaveMultipleViews(bool enabled)
        {
            var ignored = MultipleViewsActive;
            ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLabMultipleViews] = enabled;
        }

        public static bool IsRestartPending
        {
            get { return MultipleViewsSaved != MultipleViewsActive; }
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
