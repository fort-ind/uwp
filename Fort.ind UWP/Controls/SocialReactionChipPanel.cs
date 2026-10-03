using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class SocialReactionChipPanel : Panel
    {
        private const double Spacing = 6;

        public static readonly DependencyProperty SingleLineProperty =
            DependencyProperty.Register(nameof(SingleLine), typeof(bool), typeof(SocialReactionChipPanel),
                                        new PropertyMetadata(false, OnLayoutPropertyChanged));

        public static readonly DependencyProperty OverflowElementProperty =
            DependencyProperty.Register(nameof(OverflowElement), typeof(UIElement), typeof(SocialReactionChipPanel),
                                        new PropertyMetadata(null, OnLayoutPropertyChanged));

        private int _visibleCount = -1;

        public event EventHandler<int> VisibleCountChanged;

        public bool SingleLine
        {
            get { return (bool)GetValue(SingleLineProperty); }
            set { SetValue(SingleLineProperty, value); }
        }

        public UIElement OverflowElement
        {
            get { return (UIElement)GetValue(OverflowElementProperty); }
            set { SetValue(OverflowElementProperty, value); }
        }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SocialReactionChipPanel)d).InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);
            foreach (var child in Children)
            {
                child.Measure(childConstraint);
            }

            return SingleLine ? Line(availableSize.Width, false) : Flow(availableSize.Width, false);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (SingleLine)
            {
                Line(finalSize.Width, true);
            }
            else
            {
                Flow(finalSize.Width, true);
            }

            return finalSize;
        }

        private Size Line(double width, bool arrange)
        {
            var overflow = OverflowElement;
            var chips = 0;
            foreach (var child in Children)
            {
                if (!ReferenceEquals(child, overflow) && child.IsShown()) chips++;
            }

            var fitted = Fit(width, overflow, chips, false);
            var needsOverflow = fitted < chips && overflow != null;
            if (needsOverflow) fitted = Fit(width, overflow, chips, true);

            double x = 0;
            double height = 0;
            var placed = 0;
            foreach (var child in Children)
            {
                if (ReferenceEquals(child, overflow) || !child.IsShown()) continue;

                var size = child.DesiredSize;
                if (placed < fitted)
                {
                    var start = placed == 0 ? 0 : x + Spacing;
                    if (arrange) child.Arrange(new Rect(start, 0, size.Width, size.Height));
                    x = start + size.Width;
                    height = Math.Max(height, size.Height);
                }
                else if (arrange)
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                }

                placed++;
            }

            if (overflow != null)
            {
                if (needsOverflow)
                {
                    var size = overflow.DesiredSize;
                    var start = fitted == 0 ? 0 : x + Spacing;
                    if (arrange) overflow.Arrange(new Rect(start, 0, size.Width, size.Height));
                    x = start + size.Width;
                    height = Math.Max(height, size.Height);
                }
                else if (arrange)
                {
                    overflow.Arrange(new Rect(0, 0, 0, 0));
                }
            }

            if (!arrange) ReportVisibleCount(needsOverflow ? fitted : chips);

            var resultWidth = double.IsInfinity(width) ? x : Math.Min(x, width);
            return new Size(resultWidth, height);
        }

        private int Fit(double width, UIElement overflow, int chips, bool reserveOverflow)
        {
            var budget = width;
            if (reserveOverflow && overflow != null) budget -= overflow.DesiredSize.Width + Spacing;

            double x = 0;
            var fitted = 0;
            foreach (var child in Children)
            {
                if (ReferenceEquals(child, overflow) || !child.IsShown()) continue;

                var start = fitted == 0 ? 0 : x + Spacing;
                if (start + child.DesiredSize.Width > budget) break;

                x = start + child.DesiredSize.Width;
                fitted++;
            }

            return Math.Min(fitted, chips);
        }

        private Size Flow(double width, bool arrange)
        {
            var overflow = OverflowElement;
            double x = 0;
            double y = 0;
            double lineHeight = 0;
            double widest = 0;
            var lineHasItems = false;
            var count = 0;

            foreach (var child in Children)
            {
                if (ReferenceEquals(child, overflow))
                {
                    if (arrange) child.Arrange(new Rect(0, 0, 0, 0));
                    continue;
                }

                if (!child.IsShown()) continue;

                var size = child.DesiredSize;
                var start = lineHasItems ? x + Spacing : 0;

                if (lineHasItems && start + size.Width > width)
                {
                    y += lineHeight + Spacing;
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
                count++;
            }

            if (!arrange) ReportVisibleCount(count);

            var height = lineHasItems ? y + lineHeight : 0;
            var resultWidth = double.IsInfinity(width) ? widest : Math.Min(widest, width);
            return new Size(resultWidth, height);
        }

        private void ReportVisibleCount(int count)
        {
            if (count == _visibleCount) return;

            _visibleCount = count;
            VisibleCountChanged?.Invoke(this, count);
        }
    }
}
