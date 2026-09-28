using System;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
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
        private const string KeepOnTopGlyph = "";
        private const string BackToFullViewGlyph = "";

        private readonly AccessibilitySettings _accessibilitySettings = new AccessibilitySettings();

        private ViewLifetimeControl _view;

        private FrameworkElement _windowRoot;

        private DisplayInformation _displayInformation;

        private bool _handlersAttached = false;

        private bool _backButtonShown = false;

        private bool _keepOnTopSupported = false;

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
            SetupKeepOnTop();
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
                KeepOnTopButton.Height = coreTitleBar.Height;
            }

            TitleBarLeftInset.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
            TitleBarRightInset.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
            TitleBarBackButton.Margin = new Thickness(coreTitleBar.SystemOverlayLeftInset, 0, 0, 0);
            KeepOnTopButton.Margin = new Thickness(0, 0, coreTitleBar.SystemOverlayRightInset, 0);
        }

        private void SetupKeepOnTop()
        {
            try
            {
                _keepOnTopSupported = ApplicationView.GetForCurrentView().IsViewModeSupported(ApplicationViewMode.CompactOverlay);
                if (!_keepOnTopSupported) return;

                TitleBarKeepOnTopSpace.Width = new GridLength(KeepOnTopButton.Width);
                UpdateKeepOnTopButton();
                UpdateKeepOnTopButtonVisibility();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not set up keep on top - {ex.Message}");
            }
        }

        private static bool IsKeptOnTop()
        {
            return ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay;
        }

        private void UpdateKeepOnTopButton()
        {
            var onTop = IsKeptOnTop();
            var label = LocalizedStrings.Get(onTop ? "KeepOnTopExit" : "KeepOnTopEnter");

            KeepOnTopButton.Content = onTop ? BackToFullViewGlyph : KeepOnTopGlyph;
            AutomationProperties.SetName(KeepOnTopButton, label);
            ToolTipService.SetToolTip(KeepOnTopButton, label);
        }

        private void UpdateKeepOnTopButtonVisibility()
        {
            KeepOnTopButton.Visibility = _keepOnTopSupported ? AppTitleBar.Visibility : Visibility.Collapsed;
        }

        private async void KeepOnTopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var view = ApplicationView.GetForCurrentView();
                var enter = view.ViewMode != ApplicationViewMode.CompactOverlay;

                bool changed;
                if (enter)
                {
                    var preferences = ViewModePreferences.CreateDefault(ApplicationViewMode.CompactOverlay);
                    preferences.ViewSizePreference = ViewSizePreference.Custom;
                    preferences.CustomSize = new Size(AppConstants.KeepOnTopWindowWidth, AppConstants.KeepOnTopWindowHeight);
                    changed = await view.TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay, preferences);
                }
                else
                {
                    changed = await view.TryEnterViewModeAsync(ApplicationViewMode.Default);
                }

                UpdateKeepOnTopButton();

                if (changed)
                {
                    AutomationHelper.AnnounceStatus(KeepOnTopButton,
                                                    LocalizedStrings.Get(enter ? "KeepOnTopEnteredAnnouncement" : "KeepOnTopExitedAnnouncement"),
                                                    "KeepOnTop");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: switching keep on top failed - {ex.Message}");
            }
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            try
            {
                if (_keepOnTopSupported)
                {
                    UpdateKeepOnTopButton();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: Failed to follow a window size change - {ex.Message}");
            }
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
                Window.Current.SizeChanged += OnWindowSizeChanged;

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
                Window.Current.SizeChanged -= OnWindowSizeChanged;

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
                UpdateKeepOnTopButtonVisibility();
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
