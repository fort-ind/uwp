using System;
using Windows.ApplicationModel.Resources;
using Windows.UI.Core;

namespace Fort.ind_UWP
{
    public static class LocalizedStrings
    {
        private static ResourceLoader s_loader;

        private static ResourceLoader s_viewIndependentLoader;

        private static ResourceLoader Loader
        {
            get
            {
                if (s_loader != null) return s_loader;

                if (CoreWindow.GetForCurrentThread() == null) return ViewIndependentLoader;

                try
                {
                    s_loader = ResourceLoader.GetForCurrentView();
                }
                catch (Exception ex)
                {
                    AppLog.Error("LocalizedStrings: could not open the resource loader", ex);
                }

                return s_loader;
            }
        }

        private static ResourceLoader ViewIndependentLoader
        {
            get
            {
                if (s_viewIndependentLoader != null) return s_viewIndependentLoader;

                try
                {
                    s_viewIndependentLoader = ResourceLoader.GetForViewIndependentUse();
                }
                catch (Exception ex)
                {
                    AppLog.Error("LocalizedStrings: could not open the view-independent resource loader", ex);
                }

                return s_viewIndependentLoader;
            }
        }

        public static bool IsAvailable
        {
            get { return Loader != null; }
        }

        public static string Get(string key)
        {
            var loader = Loader;
            if (loader == null) return key;

            try
            {
                var value = loader.GetString(key);
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (Exception ex)
            {
                AppLog.Error($"LocalizedStrings: no resource for '{key}'", ex);
                return key;
            }
        }

        public static string Format(string key, params object[] args)
        {
            return FormatPattern(Get(key), key, args);
        }

        public static string FormatPattern(string pattern, string key, params object[] args)
        {
            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException ex)
            {
                AppLog.Error($"LocalizedStrings: '{key}' has malformed placeholders", ex);
                return pattern;
            }
        }
    }
}
