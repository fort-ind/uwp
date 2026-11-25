using System;

namespace Fort.ind_UWP
{
    public static class LabsService
    {
        public static event EventHandler ChatChanged;

        public static bool ChatEnabled
        {
            get { return AppSettings.ReadBool(AppConstants.SettingLabChat, false); }
        }

        public static void SetChatEnabled(bool enabled)
        {
            if (ChatEnabled == enabled) return;

            AppSettings.Write(AppConstants.SettingLabChat, enabled);

            try
            {
                ChatChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("LabsService: a chat lab handler failed", ex);
            }
        }
    }
}
