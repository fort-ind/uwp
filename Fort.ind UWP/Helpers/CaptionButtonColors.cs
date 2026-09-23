using Windows.UI;
using Windows.UI.ViewManagement;

namespace Fort.ind_UWP
{
    public static class CaptionButtonColors
    {
        public static void Apply(ApplicationViewTitleBar titleBar, bool highContrast, bool isDark)
        {
            if (titleBar == null) return;

            if (highContrast)
            {
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonInactiveBackgroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
                titleBar.ButtonInactiveForegroundColor = null;
                return;
            }

            var fgColor = isDark ? Colors.White : Colors.Black;

            var inactiveFg = isDark ? Color.FromArgb(255, 0x99, 0x99, 0x99) : Color.FromArgb(255, 0x66, 0x66, 0x66);
            var hoverBg = isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            var pressedBg = isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0);

            var hoverFg = fgColor;
            var pressedFg = fgColor;

            Color accent;
            if (AccentColorService.ActiveAccentHex != null
                && ColorHelper.TryHexToColor(AccentColorService.ActiveAccentHex, out accent))
            {
                hoverBg = accent;
                pressedBg = ColorHelper.AccentShade(accent, -1);
                hoverFg = ColorHelper.ContrastingForeground(hoverBg);
                pressedFg = ColorHelper.ContrastingForeground(pressedBg);
            }

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedBackgroundColor = pressedBg;

            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = hoverFg;
            titleBar.ButtonPressedForegroundColor = pressedFg;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
        }
    }
}
