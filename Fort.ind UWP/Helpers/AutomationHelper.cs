using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;

namespace Fort.ind_UWP
{
    public static class AutomationHelper
    {
        public static void AnnounceStatus(UIElement source, string message, string activityId)
        {
            try
            {
                if (source == null || string.IsNullOrEmpty(message)) return;

                var peer = FrameworkElementAutomationPeer.FromElement(source)
                           ?? FrameworkElementAutomationPeer.CreatePeerForElement(source);
                if (peer == null) return;

                peer.RaiseNotificationEvent(AutomationNotificationKind.Other,
                                            AutomationNotificationProcessing.MostRecent,
                                            message,
                                            activityId ?? "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AutomationHelper: notification failed - {ex.Message}");
            }
        }

        public static void AnnounceLiveRegion(UIElement source)
        {
            try
            {
                if (source == null) return;

                var peer = FrameworkElementAutomationPeer.FromElement(source);
                if (peer == null) return;

                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AutomationHelper: live region event failed - {ex.Message}");
            }
        }
    }
}
