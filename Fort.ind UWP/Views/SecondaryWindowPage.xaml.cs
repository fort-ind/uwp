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

        private string _baseTitle;

        private FrameworkElement _windowRoot;

        private DisplayInformation _displayInformation;

        private bool _handlersAttached = false;

        private bool _backButtonShown = false;

        private bool _keepOnTopSupported = false;

        private IImmersiveWindowPage _immersivePage;

        private bool _immersive = false;

        private DispatcherTimer _hiddenTrimTimer;

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
            ReleaseContent();
            ForgetImmersivePage();
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

        private void ReleaseContent()
        {
            try
            {
                var page = ContentFrame.Content as IReleasablePage;
                if (page != null) page.Release();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not release the content page - {ex.Message}");
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

            _baseTitle = title;
            ApplyTitle(title);

            if (string.IsNullOrEmpty(_view.Header))
            {
                HeaderText.Visibility = Visibility.Collapsed;
            }
            else
            {
                HeaderText.Text = _view.Header;
                AutomationProperties.SetName(ContentFrame, _view.Header);
            }

            ContentFrame.Navigate(_view.PageType, _view.Parameter);
        }

        internal void UpdateTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return;

            _baseTitle = title;
            if (ContentFrame.Content is SocialNotePage || ContentFrame.Content is SocialTagPage) return;

            ShowTitle(title);
        }

        private void ShowTitle(string title)
        {
            ApplyTitle(title);

            try
            {
                ApplicationView.GetForCurrentView().Title = title;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not retitle the view - {ex.Message}");
            }
        }

        private void FollowContentTitle(object parameter)
        {
            var tagArgs = ContentFrame.Content is SocialTagPage ? parameter as SocialTagArgs : null;
            var title = ContentFrame.Content is SocialNotePage ? LocalizedStrings.Get("WindowTitleNote")
                        : tagArgs != null ? LocalizedStrings.Format("SocialTagTitleFormat", tagArgs.Tag)
                        : _baseTitle;
            if (string.IsNullOrEmpty(title) || string.Equals(title, WindowTitleText.Text, StringComparison.OrdinalIgnoreCase)) return;

            ShowTitle(title);
        }

        private void LimitBackStack()
        {
            var stack = ContentFrame.BackStack;
            while (stack.Count > AppConstants.ContentBackStackLimit)
            {
                stack.RemoveAt(1);
            }
        }

        internal void Reopen(object parameter)
        {
            var page = ContentFrame.Content as IReopenablePage;
            if (page != null) page.Reopen(parameter);
        }

        private void ApplyTitle(string title)
        {
            WindowTitleText.Text = title.ToUpperInvariant();
            AutomationProperties.SetName(WindowTitleText, title);

            if (_view == null || string.IsNullOrEmpty(_view.Header))
            {
                AutomationProperties.SetName(ContentFrame, title);
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            try
            {
                if (e.NavigationMode == NavigationMode.New) LimitBackStack();
                FollowContentTitle(e.Parameter);

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
                    AutomationProperties.SetName(page.ContentRegion, string.IsNullOrEmpty(_view.Header) ? _view.Title : _view.Header);
                }

                WatchImmersivePage(ContentFrame.Content as IImmersiveWindowPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: content navigation bookkeeping failed - {ex.Message}");
            }
        }

        private void WatchImmersivePage(IImmersiveWindowPage page)
        {
            if (ReferenceEquals(page, _immersivePage)) return;

            ForgetImmersivePage();
            _immersivePage = page;
            if (_immersivePage != null) _immersivePage.TitleBarChanged += ImmersivePage_TitleBarChanged;

            ApplyImmersiveTitleBar();
        }

        private void ForgetImmersivePage()
        {
            if (_immersivePage == null) return;

            _immersivePage.TitleBarChanged -= ImmersivePage_TitleBarChanged;
            _immersivePage = null;
        }

        private void ImmersivePage_TitleBarChanged(object sender, EventArgs e)
        {
            try
            {
                ApplyImmersiveTitleBar();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not follow the page's title bar - {ex.Message}");
            }
        }

        private void ApplyImmersiveTitleBar()
        {
            var page = _immersivePage;
            var immersive = page != null
                            && page.ExtendsUnderTitleBar
                            && !_accessibilitySettings.HighContrast
                            && AppTitleBar.IsShown();

            if (immersive != _immersive)
            {
                _immersive = immersive;
                VisualStateManager.GoToState(this, immersive ? "TitleBarImmersiveState" : "TitleBarBandState", false);
            }

            var theme = immersive ? page.TitleBarTheme : ElementTheme.Default;
            AppTitleBar.RequestedTheme = theme;
            TitleBarBackButton.RequestedTheme = theme;
            KeepOnTopButton.RequestedTheme = theme;

            if (page != null) page.SetTitleBarInset(immersive ? AppTitleBar.Height : 0);

            UpdateTitleBarColors();
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

            if (_immersive && _immersivePage != null) _immersivePage.SetTitleBarInset(AppTitleBar.Height);
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
            var theme = AppTitleBar.RequestedTheme;
            var isDark = theme == ElementTheme.Default
                         ? AppearanceService.IsEffectiveThemeDark(_windowRoot)
                         : theme == ElementTheme.Dark;

            CaptionButtonColors.Apply(ApplicationView.GetForCurrentView().TitleBar,
                                      _accessibilitySettings.HighContrast,
                                      isDark);
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

                Window.Current.CoreWindow.VisibilityChanged += OnCoreWindowVisibilityChanged;
                MemoryService.TrimRequested += OnTrimRequested;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not attach window handlers - {ex.Message}");
            }
        }

        private void OnCoreWindowVisibilityChanged(CoreWindow sender, VisibilityChangedEventArgs args)
        {
            try
            {
                if (args.Visible)
                {
                    StopHiddenTrimTimer();
                    return;
                }

                if (_hiddenTrimTimer == null)
                {
                    _hiddenTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppConstants.HiddenWindowTrimSeconds) };
                    _hiddenTrimTimer.Tick += HiddenTrimTimer_Tick;
                }
                _hiddenTrimTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not follow the window's visibility - {ex.Message}");
            }
        }

        private void HiddenTrimTimer_Tick(object sender, object e)
        {
            StopHiddenTrimTimer();
            TrimContent();
        }

        private void StopHiddenTrimTimer()
        {
            if (_hiddenTrimTimer != null) _hiddenTrimTimer.Stop();
        }

        private async void OnTrimRequested(object sender, MemoryTrimEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, TrimContent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: memory trim handler failed - {ex.Message}");
            }
        }

        private void TrimContent()
        {
            try
            {
                var page = ContentFrame.Content as ITrimmablePage;
                if (page != null) page.Trim();

                SocialEmojiPicker.TrimCurrentView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecondaryWindowPage: could not trim the content page - {ex.Message}");
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

                Window.Current.CoreWindow.VisibilityChanged -= OnCoreWindowVisibilityChanged;
                MemoryService.TrimRequested -= OnTrimRequested;
                StopHiddenTrimTimer();
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
                ApplyImmersiveTitleBar();
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
                    ApplyImmersiveTitleBar();
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

                if (SocialMediaLightbox.CloseCurrent()) return true;

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
