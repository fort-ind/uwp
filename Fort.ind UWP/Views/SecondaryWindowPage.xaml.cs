using System;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Display;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SecondaryWindowPage : Page
    {
        private readonly AccessibilitySettings _accessibilitySettings = new AccessibilitySettings();

        private ViewLifetimeControl _view;

        private FrameworkElement _windowRoot;

        private DisplayInformation _displayInformation;

        private bool _handlersAttached = false;

        private bool _backButtonShown = false;

        public SecondaryWindowPage()
        {
            this.InitializeComponent();

            AddBackAccelerators();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _view = e.Parameter as ViewLifetimeControl;
            if (_view == null) return;

            _windowRoot = Window.Current.Content as FrameworkElement;

            ApplyAppearance();
            SetupTitleBar();
            AttachHandlers();

            ShowContent();
        }

        internal void Release()
        {
            DetachHandlers();

            try
            {
                AppearanceService.DetachWindowSurface(_windowRoot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not detach the window surface - {ex.Message}");
            }
        }

        private void ApplyAppearance()
        {
            try
            {
                RootGrid.Background = AppearanceService.AttachWindowSurface(_windowRoot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not apply the appearance - {ex.Message}");
            }
        }

        private void ShowContent()
        {
            var title = _view.Title ?? string.Empty;
            var header = _view.Header ?? title;

            WindowTitleText.Text = title.ToUpperInvariant();
            AutomationProperties.SetName(WindowTitleText, title);

            HeaderText.Text = header;
            AutomationProperties.SetName(ContentFrame, header);

            ContentFrame.Navigate(_view.PageType, _view.NavTag);
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            try
            {
                if (ContentFrame.CanGoBack && !_backButtonShown)
                {
                    _backButtonShown = true;
                    TitleBarBackButtonSpace.Width = new GridLength(TitleBarBackButton.Width);
                    UpdateBackButtonVisibility();
                }

                TitleBarBackButton.IsEnabled = ContentFrame.CanGoBack;

                var page = ContentFrame.Content as IShellContentPage;
                if (page != null && page.ContentRegion != null && _view != null)
                {
                    AutomationProperties.SetName(page.ContentRegion, _view.Header ?? _view.Title);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: content navigation bookkeeping failed - {ex.Message}");
            }
        }

        private void SetupTitleBar()
        {
            try
            {
                var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
                coreTitleBar.ExtendViewIntoTitleBar = true;

                Window.Current.SetTitleBar(AppTitleBar);

                ApplyTitleBarLayoutMetrics(coreTitleBar);
                UpdateTitleBarColors();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not set up the title bar - {ex.Message}");
            }
        }

        private void ApplyTitleBarLayoutMetrics(CoreApplicationViewTitleBar coreTitleBar)
        {
            if (coreTitleBar == null) return;

            if (coreTitleBar.Height > 0)
            {
                AppTitleBar.Height = coreTitleBar.Height;
                TitleBarBackButton.Height = coreTitleBar.Height;
            }

            TitleBarLeftInset.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
            TitleBarRightInset.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
            TitleBarBackButton.Margin = new Thickness(coreTitleBar.SystemOverlayLeftInset, 0, 0, 0);
        }

        private void UpdateTitleBarColors()
        {
            CaptionButtonColors.Apply(ApplicationView.GetForCurrentView().TitleBar,
                                      _accessibilitySettings.HighContrast,
                                      AppearanceService.IsEffectiveThemeDark(_windowRoot));
        }

        private void AttachHandlers()
        {
            if (_handlersAttached) return;
            _handlersAttached = true;

            try
            {
                var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
                coreTitleBar.LayoutMetricsChanged += OnTitleBarLayoutMetricsChanged;
                coreTitleBar.IsVisibleChanged += OnTitleBarIsVisibleChanged;

                _accessibilitySettings.HighContrastChanged += OnHighContrastChanged;
                ActualThemeChanged += OnActualThemeChanged;

                SystemNavigationManager.GetForCurrentView().BackRequested += OnSystemBackRequested;
                Window.Current.CoreWindow.PointerPressed += OnCoreWindowPointerPressed;

                _displayInformation = DisplayInformation.GetForCurrentView();
                _displayInformation.DpiChanged += OnDpiChanged;
                ApplyTitleBarHairline();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not attach window handlers - {ex.Message}");
            }
        }

        private void DetachHandlers()
        {
            if (!_handlersAttached) return;
            _handlersAttached = false;

            try
            {
                var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
                coreTitleBar.LayoutMetricsChanged -= OnTitleBarLayoutMetricsChanged;
                coreTitleBar.IsVisibleChanged -= OnTitleBarIsVisibleChanged;

                _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged;
                ActualThemeChanged -= OnActualThemeChanged;

                SystemNavigationManager.GetForCurrentView().BackRequested -= OnSystemBackRequested;
                Window.Current.CoreWindow.PointerPressed -= OnCoreWindowPointerPressed;

                if (_displayInformation != null)
                {
                    _displayInformation.DpiChanged -= OnDpiChanged;
                    _displayInformation = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not detach window handlers - {ex.Message}");
            }
        }

        private void OnDpiChanged(DisplayInformation sender, object args)
        {
            try
            {
                ApplyTitleBarHairline();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: Failed to follow a DPI change - {ex.Message}");
            }
        }

        private void ApplyTitleBarHairline()
        {
            var scale = _displayInformation == null ? 1.0 : _displayInformation.LogicalDpi / 96.0;
            if (scale <= 0) scale = 1.0;

            AppTitleBar.BorderThickness = new Thickness(0, 0, 0, 1.0 / scale);
        }

        private void UpdateBackButtonVisibility()
        {
            TitleBarBackButton.Visibility = _backButtonShown ? AppTitleBar.Visibility : Visibility.Collapsed;
        }

        private void OnTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                ApplyTitleBarLayoutMetrics(sender);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: Failed to apply title bar layout metrics - {ex.Message}");
            }
        }

        private void OnTitleBarIsVisibleChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                AppTitleBar.Visibility = sender.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                UpdateBackButtonVisibility();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: Failed to follow title bar visibility - {ex.Message}");
            }
        }

        private void OnHighContrastChanged(AccessibilitySettings sender, object args)
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    UpdateTitleBarColors();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SecondaryWindowPage: title bar repaint after high contrast change failed - {ex.Message}");
                }
            });
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                AppearanceService.RepaintWindowSurface(_windowRoot);
                UpdateTitleBarColors();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: repaint after theme change failed - {ex.Message}");
            }
        }

        private void TitleBarBackButton_Click(object sender, RoutedEventArgs e)
        {
            TryGoBack();
        }

        private void OnSystemBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = TryGoBack();
        }

        private void AddBackAccelerators()
        {
            AddBackAccelerator(VirtualKey.GoBack, VirtualKeyModifiers.None);
            AddBackAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu);
        }

        private void AddBackAccelerator(VirtualKey key, VirtualKeyModifiers modifiers)
        {
            var accelerator = new KeyboardAccelerator();
            accelerator.Key = key;
            accelerator.Modifiers = modifiers;
            accelerator.Invoked += BackAccelerator_Invoked;
            KeyboardAccelerators.Add(accelerator);
        }

        private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = TryGoBack();
        }

        private void OnCoreWindowPointerPressed(CoreWindow sender, PointerEventArgs e)
        {
            if (!e.CurrentPoint.Properties.IsXButton1Pressed) return;

            TryGoBack();
        }

        private bool TryGoBack()
        {
            try
            {
                if (DialogService.IsDialogOpen) return false;

                if (!ContentFrame.CanGoBack) return false;

                ContentFrame.GoBack();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: Back navigation failed - {ex.Message}");
                return false;
            }
        }
    }
}
