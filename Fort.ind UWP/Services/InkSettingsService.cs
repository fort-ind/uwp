using System;
using System.Threading;
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
            get { return AppSettings.ReadBool(AppConstants.SettingInkWithMouse, true); }
            set { WriteAndBump(AppConstants.SettingInkWithMouse, value); }
        }

        public static PenTextMode PenTextMode
        {
            get { return AppSettings.ReadEnum(AppConstants.SettingInkPenTextMode, PenTextMode.WriteOnBox); }
            set { WriteAndBump(AppConstants.SettingInkPenTextMode, value.ToString()); }
        }

        public static bool HandwritingFont
        {
            get { return AppSettings.ReadBool(AppConstants.SettingInkHandwritingFont, false); }
            set { WriteAndBump(AppConstants.SettingInkHandwritingFont, value); }
        }

        public static bool TouchPansAfterPen
        {
            get { return AppSettings.ReadBool(AppConstants.SettingInkTouchPansAfterPen, true); }
            set { WriteAndBump(AppConstants.SettingInkTouchPansAfterPen, value); }
        }

        public static DrawingAspect DrawingAspect
        {
            get { return AppSettings.ReadEnum(AppConstants.SettingInkDrawingAspect, DrawingAspect.Landscape); }
            set { AppSettings.Write(AppConstants.SettingInkDrawingAspect, value.ToString()); }
        }

        public static DrawingBackground DrawingBackground
        {
            get { return AppSettings.ReadEnum(AppConstants.SettingInkDrawingBackground, DrawingBackground.White); }
            set { AppSettings.Write(AppConstants.SettingInkDrawingBackground, value.ToString()); }
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
                    AppLog.Error("InkSettingsService: could not read the handwriting font", ex);
                    return null;
                }
            }
        }

        private static void WriteAndBump(string key, object value)
        {
            AppSettings.Write(key, value);
            Interlocked.Increment(ref s_version);
        }
    }
}
