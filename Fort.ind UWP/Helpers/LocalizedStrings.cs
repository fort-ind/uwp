using System;
using System.Diagnostics;
using Windows.ApplicationModel.Resources;
using Windows.UI.Core;

namespace Fort.ind_UWP
{
    public static class LocalizedStrings
    {
        private static ResourceLoader s_loader;

        private static ResourceLoader Loader
        {
            get
            {
                if (s_loader != null) return s_loader;

                if (CoreWindow.GetForCurrentThread() == null) return null;

                try
                {
                    s_loader = ResourceLoader.GetForCurrentView();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LocalizedStrings: could not open the resource loader - {ex.Message}");
                }

                return s_loader;
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
                Debug.WriteLine($"LocalizedStrings: no resource for '{key}' - {ex.Message}");
                return key;
            }
        }

        public static string Format(string key, params object[] args)
        {
            return FormatPattern(Get(key), key, args);
        }

        /// <summary>
        /// <see cref="Format"/> for a pattern the caller already fetched with <see cref="Get"/>,
        /// so a loop over many items resolves the resource once rather than once per item.
        /// <paramref name="key"/> is only used for the diagnostic.
        /// </summary>
        public static string FormatPattern(string pattern, string key, params object[] args)
        {
            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException ex)
            {
                Debug.WriteLine($"LocalizedStrings: '{key}' has malformed placeholders - {ex.Message}");
                return pattern;
            }
        }
    }
}
