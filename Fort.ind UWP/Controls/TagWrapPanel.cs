using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class TagWrapPanel : Panel
    {
        public static readonly DependencyProperty HorizontalSpacingProperty =
            DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(TagWrapPanel),
                                        new PropertyMetadata(0d, OnLayoutPropertyChanged));

        public static readonly DependencyProperty VerticalSpacingProperty =
            DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(TagWrapPanel),
                                        new PropertyMetadata(0d, OnLayoutPropertyChanged));

        public double HorizontalSpacing
        {
            get { return (double)GetValue(HorizontalSpacingProperty); }
            set { SetValue(HorizontalSpacingProperty, value); }
        }

        public double VerticalSpacing
        {
            get { return (double)GetValue(VerticalSpacingProperty); }
            set { SetValue(VerticalSpacingProperty, value); }
        }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((TagWrapPanel)d).InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);
            foreach (var child in Children)
            {
                child.Measure(childConstraint);
            }

            return Flow(availableSize.Width, false);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Flow(finalSize.Width, true);
            return finalSize;
        }

        private Size Flow(double width, bool arrange)
        {
            double x = 0;
            double y = 0;
            double lineHeight = 0;
            double widest = 0;
            var lineHasItems = false;

            foreach (var child in Children)
            {
                if (!child.IsShown()) continue;

                var size = child.DesiredSize;
                var start = lineHasItems ? x + HorizontalSpacing : 0;

                if (lineHasItems && start + size.Width > width)
                {
                    y += lineHeight + VerticalSpacing;
                    start = 0;
                    lineHeight = 0;
                }

                if (arrange)
                {
                    child.Arrange(new Rect(start, y, Math.Min(size.Width, Math.Max(0, width)), size.Height));
                }

                x = start + size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
                widest = Math.Max(widest, x);
                lineHasItems = true;
            }

            var height = lineHasItems ? y + lineHeight : 0;
            var resultWidth = double.IsInfinity(width) ? widest : Math.Min(widest, width);
            return new Size(resultWidth, height);
        }
    }
}
