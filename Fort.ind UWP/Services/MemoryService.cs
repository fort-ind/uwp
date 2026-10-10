using System;
using Windows.Storage;
using Windows.System;
using Windows.System.Diagnostics;

namespace Fort.ind_UWP
{
    public sealed class MemoryTrimEventArgs : EventArgs
    {
        public MemoryTrimEventArgs(bool includeCurrentPages)
        {
            IncludeCurrentPages = includeCurrentPages;
        }

        public bool IncludeCurrentPages { get; private set; }
    }

    public enum LowMemoryModeChoice
    {
        Automatic,
        On,
        Off
    }

    public static class MemoryService
    {
        private static readonly object s_lock = new object();

        private static bool s_initialized;

        private static bool? s_smallDevice;

        public static event EventHandler<MemoryTrimEventArgs> TrimRequested;

        public static LowMemoryModeChoice LowMemoryChoice
        {
            get { return ReadChoice(); }
        }

        public static bool IsSmallDevice
        {
            get
            {
                lock (s_lock)
                {
                    if (!s_smallDevice.HasValue) s_smallDevice = ReadSmallDevice();
                    return s_smallDevice.Value;
                }
            }
        }

        public static bool IsLowMemoryMode
        {
            get
            {
                var choice = LowMemoryChoice;
                return choice == LowMemoryModeChoice.On || (choice == LowMemoryModeChoice.Automatic && IsSmallDevice);
            }
        }

        public static int FeedCap
        {
            get { return IsLowMemoryMode ? AppConstants.LowMemorySocialFeedCap : AppConstants.SocialFeedCap; }
        }

        public static TimeSpan PageUnloadDelay
        {
            get
            {
                return TimeSpan.FromSeconds(IsLowMemoryMode ? AppConstants.LowMemoryPageUnloadSeconds
                                                            : AppConstants.UnusedPageUnloadSeconds);
            }
        }

        public static double ListCacheLength
        {
            get { return IsLowMemoryMode ? AppConstants.LowMemoryListCacheLength : AppConstants.ListCacheLength; }
        }

        public static bool AllowsBannerBlur
        {
            get { return !IsLowMemoryMode; }
        }

        public static void SetLowMemoryChoice(LowMemoryModeChoice choice)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLowMemoryMode] = choice.ToString();
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: could not save the low-memory choice", ex);
            }
        }

        private static LowMemoryModeChoice ReadChoice()
        {
            try
            {
                object value;
                LowMemoryModeChoice choice;
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(AppConstants.SettingLowMemoryMode, out value)
                    && Enum.TryParse(Convert.ToString(value), out choice)
                    && Enum.IsDefined(typeof(LowMemoryModeChoice), choice))
                {
                    return choice;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: could not read the low-memory choice", ex);
            }

            return LowMemoryModeChoice.Automatic;
        }

        private static bool ReadSmallDevice()
        {
            try
            {
                var total = SystemDiagnosticInfo.GetForCurrentSystem().MemoryUsage.GetReport().TotalPhysicalSizeInBytes;
                return total > 0 && total <= AppConstants.LowMemoryDeviceBytes;
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: could not read the installed memory", ex);
                return false;
            }
        }

        public static void Initialize()
        {
            lock (s_lock)
            {
                if (s_initialized) return;
                s_initialized = true;
            }

            try
            {
                MemoryManager.AppMemoryUsageLimitChanging += MemoryManager_AppMemoryUsageLimitChanging;
                MemoryManager.AppMemoryUsageIncreased += MemoryManager_AppMemoryUsageIncreased;
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: could not watch the memory limit", ex);
            }
        }

        public static void OnEnteredBackground()
        {
            RequestTrim(false);
        }

        private static void MemoryManager_AppMemoryUsageLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs e)
        {
            try
            {
                if (MemoryManager.AppMemoryUsage >= e.NewLimit) RequestTrim(true);
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: memory limit handler failed", ex);
            }
        }

        private static void MemoryManager_AppMemoryUsageIncreased(object sender, object e)
        {
            try
            {
                var level = MemoryManager.AppMemoryUsageLevel;
                if (level == AppMemoryUsageLevel.High || level == AppMemoryUsageLevel.OverLimit) RequestTrim(true);
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: memory usage handler failed", ex);
            }
        }

        private static void RequestTrim(bool includeCurrentPages)
        {
            SocialContentService.TrimCaches();

            try
            {
                var handler = TrimRequested;
                if (handler != null) handler(null, new MemoryTrimEventArgs(includeCurrentPages));
            }
            catch (Exception ex)
            {
                AppLog.Error("MemoryService: a trim handler failed", ex);
            }
        }
    }
}
