using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    internal static class SettingsSectionReveal
    {
        public static void Reveal(Control region, FrameworkElement section)
        {
            if (section == null) return;

            try
            {
                if (region != null) region.Focus(FocusState.Programmatic);

                section.StartBringIntoView(new BringIntoViewOptions
                {
                    VerticalAlignmentRatio = 0,
                    AnimationDesired = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsSectionReveal: could not reveal the section", ex);
            }
        }
    }
}
