using System;
using System.Diagnostics;
using System.Threading;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Input.Inking;

namespace Fort.ind_UWP
{
    public enum PenTextMode
    {
        WriteOnBox,
        WindowsView,
        Off
    }

    public static class InkSettingsService
    {
        private static volatile bool s_penSeen;

        private static int s_version;

        public static bool InkWithMouse
        {
            get { return Read(AppConstants.SettingInkWithMouse, true); }
            set { Write(AppConstants.SettingInkWithMouse, value); }
        }

        public static PenTextMode PenTextMode
        {
            get
            {
                try
                {
                    PenTextMode mode;
                    var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingInkPenTextMode] as string;
                    return stored != null && Enum.TryParse(stored, out mode) ? mode : PenTextMode.WriteOnBox;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"InkSettingsService: could not read the pen text mode - {ex.Message}");
                    return PenTextMode.WriteOnBox;
                }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingInkPenTextMode] = value.ToString();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"InkSettingsService: could not save the pen text mode - {ex.Message}");
                }

                Interlocked.Increment(ref s_version);
            }
        }

        public static bool HandwritingFont
        {
            get { return Read(AppConstants.SettingInkHandwritingFont, false); }
            set { Write(AppConstants.SettingInkHandwritingFont, value); }
        }

        public static bool TouchPansAfterPen
        {
            get { return Read(AppConstants.SettingInkTouchPansAfterPen, true); }
            set { Write(AppConstants.SettingInkTouchPansAfterPen, value); }
        }

        public static DrawingAspect DrawingAspect
        {
            get { return ReadEnum(AppConstants.SettingInkDrawingAspect, DrawingAspect.Landscape); }
            set { WriteEnum(AppConstants.SettingInkDrawingAspect, value); }
        }

        public static DrawingBackground DrawingBackground
        {
            get { return ReadEnum(AppConstants.SettingInkDrawingBackground, DrawingBackground.White); }
            set { WriteEnum(AppConstants.SettingInkDrawingBackground, value); }
        }

        public static int Version
        {
            get { return Volatile.Read(ref s_version); }
        }

        public static bool PenSeen
        {
            get { return s_penSeen; }
        }

        public static void NotePenSeen()
        {
            if (s_penSeen) return;

            s_penSeen = true;
            Interlocked.Increment(ref s_version);
        }

        public static CoreInputDeviceTypes InkInputTypes
        {
            get
            {
                var types = CoreInputDeviceTypes.Pen;
                if (!(TouchPansAfterPen && PenSeen)) types |= CoreInputDeviceTypes.Touch;
                if (InkWithMouse) types |= CoreInputDeviceTypes.Mouse;
                return types;
            }
        }

        public static string HandwritingFontFamilyName
        {
            get
            {
                try
                {
                    var name = PenAndInkSettings.GetDefault().FontFamilyName;
                    return string.IsNullOrWhiteSpace(name) ? null : name;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"InkSettingsService: could not read the handwriting font - {ex.Message}");
                    return null;
                }
            }
        }

        private static T ReadEnum<T>(string key, T fallback) where T : struct
        {
            try
            {
                T value;
                var stored = ApplicationData.Current.LocalSettings.Values[key] as string;
                return stored != null && Enum.TryParse(stored, out value) ? value : fallback;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InkSettingsService: could not read {key} - {ex.Message}");
                return fallback;
            }
        }

        private static void WriteEnum<T>(string key, T value) where T : struct
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[key] = value.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InkSettingsService: could not save {key} - {ex.Message}");
            }
        }

        private static bool Read(string key, bool fallback)
        {
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[key];
                return stored == null ? fallback : Convert.ToBoolean(stored);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InkSettingsService: could not read {key} - {ex.Message}");
                return fallback;
            }
        }

        private static void Write(string key, bool value)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InkSettingsService: could not save {key} - {ex.Message}");
            }

            Interlocked.Increment(ref s_version);
        }
    }
}
