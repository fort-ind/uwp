using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialCharacterRing : UserControl
    {
        private const double Center = 10;

        private const double Radius = 8.75;

        private const int CountThreshold = 100;

        private bool? _over;

        public SocialCharacterRing()
        {
            this.InitializeComponent();
        }

        public void Update(int length, int max)
        {
            if (max <= 0) max = 1;

            var remaining = max - length;
            var over = remaining < 0;
            if (_over != over)
            {
                _over = over;
                VisualStateManager.GoToState(this, over ? "OverLimit" : "UnderLimit", false);
            }

            DrawArc(over ? 1 : Math.Max(0, (double)length / max));

            var showCount = remaining < CountThreshold;
            CountText.Text = showCount ? NumberText(remaining) : "";
            CountText.Visibility = showCount ? Visibility.Visible : Visibility.Collapsed;

            AutomationProperties.SetName(this, over
                                               ? LocalizedStrings.Format("SocialComposeOverLimitFormat", NumberText(-remaining))
                                               : LocalizedStrings.Format("SocialComposeRemainingFormat", NumberText(remaining)));
        }

        private static string NumberText(int value)
        {
            return value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }

        private void DrawArc(double fraction)
        {
            if (fraction <= 0)
            {
                RingArc.Visibility = Visibility.Collapsed;
                return;
            }

            var angle = Math.Min(fraction, 0.9999) * 2 * Math.PI;
            RingSegment.Point = new Point(Center + Radius * Math.Sin(angle), Center - Radius * Math.Cos(angle));
            RingSegment.IsLargeArc = fraction > 0.5;
            RingArc.Visibility = Visibility.Visible;
        }
    }
}
