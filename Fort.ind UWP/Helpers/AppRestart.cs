using System;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    internal static class AppRestart
    {
        public static async Task RequestAsync()
        {
            try
            {
                var failureReason = await Windows.ApplicationModel.Core.CoreApplication.RequestRestartAsync("");
                AppLog.Warning($"AppRestart: the restart request did not restart the app - {failureReason}");
            }
            catch (Exception ex)
            {
                AppLog.Error("AppRestart: the restart request threw", ex);
            }
        }
    }
}
