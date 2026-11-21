using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialLoadingIndicator : UserControl
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(SocialLoadingIndicator),
                                        new PropertyMetadata(false, OnIsActiveChanged));

        public static readonly DependencyProperty RingSizeProperty =
            DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(SocialLoadingIndicator),
                                        new PropertyMetadata(32.0, OnRingSizeChanged));

        private readonly DispatcherTimer _slowTimer;

        private bool _loaded;

        public SocialLoadingIndicator()
        {
            this.InitializeComponent();
            _slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppConstants.SocialSlowResponseSeconds) };
            _slowTimer.Tick += OnSlowTimerTick;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public bool IsActive
        {
            get { return (bool)GetValue(IsActiveProperty); }
            set { SetValue(IsActiveProperty, value); }
        }

        public double RingSize
        {
            get { return (double)GetValue(RingSizeProperty); }
            set { SetValue(RingSizeProperty, value); }
        }

        private static void OnIsActiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ((SocialLoadingIndicator)sender).Apply();
        }

        private static void OnRingSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var indicator = (SocialLoadingIndicator)sender;
            indicator.Ring.Width = indicator.RingSize;
            indicator.Ring.Height = indicator.RingSize;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            AlignContent();
            Apply();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            _slowTimer.Stop();
            SlowText.Visibility = Visibility.Collapsed;
        }

        private void Apply()
        {
            Ring.IsActive = IsActive;
            AutomationProperties.SetName(Ring, AutomationProperties.GetName(this) ?? "");

            if (!IsActive || !_loaded)
            {
                _slowTimer.Stop();
                SlowText.Visibility = Visibility.Collapsed;
                return;
            }

            if (!_slowTimer.IsEnabled && !SlowText.IsShown()) _slowTimer.Start();
        }

        private void AlignContent()
        {
            var alignment = HorizontalAlignment;
            var left = alignment == HorizontalAlignment.Left;
            Ring.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            SlowText.HorizontalAlignment = Ring.HorizontalAlignment;
            SlowText.TextAlignment = left ? TextAlignment.Left : TextAlignment.Center;
        }

        private void OnSlowTimerTick(object sender, object e)
        {
            try
            {
                _slowTimer.Stop();
                if (!IsActive || !_loaded) return;

                SlowText.Visibility = Visibility.Visible;
                AutomationHelper.AnnounceLiveRegion(SlowText);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialLoadingIndicator: showing the slow-response message failed", ex);
            }
        }
    }
}
