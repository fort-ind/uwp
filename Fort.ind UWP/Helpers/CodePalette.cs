using System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public static class CodePalette
    {
        public const string FontName = "Consolas";

        private const int InlineCodeSlot = 10;

        private static readonly Color[] s_dark =
        {
            Rgb(0xFF, 0xFF, 0xFF),
            Rgb(0x86, 0xB0, 0x70),
            Rgb(0xD6, 0x9A, 0x80),
            Rgb(0xB5, 0xCE, 0xA8),
            Rgb(0x6C, 0xB0, 0xEA),
            Rgb(0x4E, 0xC9, 0xB0),
            Rgb(0xDC, 0xDC, 0xAA),
            Rgb(0x6C, 0xB0, 0xEA),
            Rgb(0x9C, 0xDC, 0xFE),
            Rgb(0xA8, 0xA8, 0xA8),
            Rgb(0xE5, 0xB0, 0x7A)
        };

        private static readonly Color[] s_light =
        {
            Rgb(0x00, 0x00, 0x00),
            Rgb(0x0A, 0x5C, 0x1B),
            Rgb(0x8F, 0x10, 0x10),
            Rgb(0x07, 0x5A, 0x3C),
            Rgb(0x1A, 0x1A, 0xD0),
            Rgb(0x12, 0x58, 0x6C),
            Rgb(0x5C, 0x43, 0x18),
            Rgb(0x80, 0x00, 0x00),
            Rgb(0x9E, 0x00, 0x00),
            Rgb(0x4A, 0x4A, 0x4A),
            Rgb(0x7E, 0x3B, 0x00)
        };

        private static readonly Lazy<AccessibilitySettings> s_accessibility =
            new Lazy<AccessibilitySettings>(() => new AccessibilitySettings());

        [ThreadStatic]
        private static SolidColorBrush[] t_darkBrushes;

        [ThreadStatic]
        private static SolidColorBrush[] t_lightBrushes;

        public static Brush For(CodeTokenKind kind, bool dark)
        {
            return Slot((int)kind, dark);
        }

        public static Brush InlineCode(bool dark)
        {
            return Slot(InlineCodeSlot, dark);
        }

        public static void Paint(TextElement element, Brush brush)
        {
            if (brush == null)
            {
                element.ClearValue(TextElement.ForegroundProperty);
                return;
            }

            element.Foreground = brush;
        }

        public static bool IsDark(FrameworkElement element)
        {
            var theme = element == null ? ElementTheme.Default : element.ActualTheme;
            if (theme == ElementTheme.Dark) return true;
            if (theme == ElementTheme.Light) return false;

            return AppearanceService.IsEffectiveThemeDark();
        }

        private static Brush Slot(int index, bool dark)
        {
            if (index == (int)CodeTokenKind.Plain || s_accessibility.Value.HighContrast) return null;

            var brushes = dark ? t_darkBrushes : t_lightBrushes;
            if (brushes == null)
            {
                brushes = new SolidColorBrush[s_dark.Length];
                if (dark)
                {
                    t_darkBrushes = brushes;
                }
                else
                {
                    t_lightBrushes = brushes;
                }
            }

            var brush = brushes[index];
            if (brush == null)
            {
                brush = new SolidColorBrush((dark ? s_dark : s_light)[index]);
                brushes[index] = brush;
            }
            return brush;
        }

        private static Color Rgb(byte r, byte g, byte b)
        {
            return Color.FromArgb(255, r, g, b);
        }
    }
}
