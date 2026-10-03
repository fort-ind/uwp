using System;
using System.Diagnostics;
using Windows.System;

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

    public static class MemoryService
    {
        private static readonly object s_lock = new object();

        private static bool s_initialized;

        public static event EventHandler<MemoryTrimEventArgs> TrimRequested;

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
                Debug.WriteLine($"MemoryService: could not watch the memory limit - {ex.Message}");
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
                Debug.WriteLine($"MemoryService: memory limit handler failed - {ex.Message}");
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
                Debug.WriteLine($"MemoryService: memory usage handler failed - {ex.Message}");
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
                Debug.WriteLine($"MemoryService: a trim handler failed - {ex.Message}");
            }
        }
    }
}
