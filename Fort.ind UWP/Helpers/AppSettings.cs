using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class AppSettings
    {
        private static IPropertySet Values
        {
            get { return ApplicationData.Current.LocalSettings.Values; }
        }

        public static bool ReadBool(string key, bool fallback, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                object stored;
                return Values.TryGetValue(key, out stored) && stored != null ? Convert.ToBoolean(stored, CultureInfo.InvariantCulture) : fallback;
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not read {key}", ex);
                return fallback;
            }
        }

        public static int ReadInt(string key, int fallback, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                object stored;
                return Values.TryGetValue(key, out stored) && stored != null ? Convert.ToInt32(stored, CultureInfo.InvariantCulture) : fallback;
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not read {key}", ex);
                return fallback;
            }
        }

        public static double ReadDouble(string key, double fallback, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                object stored;
                if (!Values.TryGetValue(key, out stored) || stored == null) return fallback;

                var value = Convert.ToDouble(stored, CultureInfo.InvariantCulture);
                return double.IsNaN(value) ? fallback : value;
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not read {key}", ex);
                return fallback;
            }
        }

        public static string ReadString(string key, string fallback, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                object stored;
                return Values.TryGetValue(key, out stored) ? stored as string ?? fallback : fallback;
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not read {key}", ex);
                return fallback;
            }
        }

        public static T ReadEnum<T>(string key, T fallback, [CallerFilePath] string callerFilePath = null) where T : struct
        {
            T value;
            var stored = ReadString(key, null, callerFilePath);
            return stored != null && Enum.TryParse(stored, out value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
        }

        public static bool Contains(string key, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                return Values.ContainsKey(key);
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not look for {key}", ex);
                return false;
            }
        }

        public static void Write(string key, object value, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                if (value == null)
                {
                    Values.Remove(key);
                }
                else
                {
                    Values[key] = value;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not write {key}", ex);
            }
        }

        public static void Remove(string key, [CallerFilePath] string callerFilePath = null)
        {
            try
            {
                Values.Remove(key);
            }
            catch (Exception ex)
            {
                AppLog.Error($"{SourceOf(callerFilePath)}: could not remove {key}", ex);
            }
        }

        private static string SourceOf(string callerFilePath)
        {
            return string.IsNullOrEmpty(callerFilePath) ? nameof(AppSettings) : Path.GetFileNameWithoutExtension(callerFilePath);
        }
    }
}
