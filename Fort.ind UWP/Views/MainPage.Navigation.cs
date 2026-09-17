using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using AnimationBuilder = Microsoft.Toolkit.Uwp.UI.Animations.AnimationBuilder;
using Axis = Microsoft.Toolkit.Uwp.UI.Animations.Axis;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private async void OnAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                    {
                        try
                        {
                            UpdateProfileNavItem();

                            UpdateStorageInfo();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"MainPage: UpdateProfileNavItem failed - {ex.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Auth state change handler failed – {ex.Message}");
            }
        }

        private void UpdateProfileNavItem()
        {
            var user = ProfileService.CurrentUser;
            if (user != null)
            {
                ProfileNavItem.Content = user.DisplayName;
                if (string.IsNullOrWhiteSpace(user.DisplayName))
                {
                    ProfileNavItem.Content = user.Username;
                }
            }
            else
            {
                ProfileNavItem.Content = LocalizedStrings.Get("ProfileNavItem/Content");
            }

            UpdateProfileNavIcon(user == null ? null : user.AvatarUrl);
        }

        private async void UpdateProfileNavIcon(string avatarUrl)
        {
            try
            {
                if (string.Equals(avatarUrl, _navAvatarUrl, StringComparison.Ordinal))
                {
                    return;
                }
                _navAvatarUrl = avatarUrl;

                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    ProfileNavItem.Icon = new SymbolIcon(Symbol.Contact);
                    return;
                }

                var iconUri = await AvatarIconService.GetCircularAvatarUriAsync(avatarUrl);

                if (!string.Equals(avatarUrl, _navAvatarUrl, StringComparison.Ordinal))
                {
                    return;
                }

                if (iconUri == null)
                {
                    _navAvatarUrl = null;
                    ProfileNavItem.Icon = new SymbolIcon(Symbol.Contact);
                    return;
                }

                var icon = new BitmapIcon();
                icon.ShowAsMonochrome = false;
                icon.UriSource = iconUri;
                icon.Width = 16;
                icon.Height = 16;
                ProfileNavItem.Icon = icon;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: nav avatar update failed - {ex.Message}");
            }
        }
        private async void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_navViewInitialized) return;
            _navViewInitialized = true;

            try
            {
                var startupTag = ResolveStartupNavTag();
                SelectNavItemForTag(startupTag);

                ShowContent(startupTag);

                ClosePaneUnlessExpanded();

                UpdateContentPadding(NavView.DisplayMode);
                UpdateAppTitleVisibility(NavView.IsPaneOpen);

                AlignPaneToggleButton();
                MarkNavigationPaneLandmark();

                LiveTileService.ClearBadge();

                var localSettings = ApplicationData.Current.LocalSettings;
                bool hideWelcome = false;
                if (localSettings.Values.ContainsKey(AppConstants.SettingHideWelcomeDialog))
                {
                    hideWelcome = Convert.ToBoolean(localSettings.Values[AppConstants.SettingHideWelcomeDialog]);
                }
                if (!hideWelcome)
                {
                    await ShowWelcomeDialogAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: NavView_Loaded failed – {ex.Message}");
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ShowContent(AppConstants.NavigationSettings, true);
            }
            else
            {
                var invokedItem = args.InvokedItemContainer as NavigationViewItem;
                if (invokedItem != null)
                {
                    var tag = invokedItem.Tag?.ToString() ?? AppConstants.NavigationLatestNews;
                    ShowContent(tag, true);
                }
            }

            ClosePaneUnlessExpanded();
        }

        private void ClosePaneUnlessExpanded()
        {
            if (NavView.DisplayMode != NavigationViewDisplayMode.Expanded)
            {
                NavView.IsPaneOpen = false;
            }
        }

        private static string HeaderFor(string panelName)
        {
            switch (panelName)
            {
                case AppConstants.NavigationGames:
                    return LocalizedStrings.Get("HeaderGames");
                case AppConstants.NavigationBetas:
                    return LocalizedStrings.Get("HeaderBetas");
                case AppConstants.NavigationProfile:
                    return LocalizedStrings.Get("HeaderProfile");
                case AppConstants.NavigationSocial:
                    return LocalizedStrings.Get("HeaderSocial");
                case AppConstants.NavigationSettings:
                    return LocalizedStrings.Get("HeaderSettings");
                default:
                    return LocalizedStrings.Get("HeaderHome");
            }
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateContentPadding(args.DisplayMode);
            UpdateAppTitleVisibility(sender.IsPaneOpen);
        }

        private void NavView_PaneOpening(NavigationView sender, object args)
        {
            UpdateAppTitleVisibility(true);
        }

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            UpdateAppTitleVisibility(false);
        }

        private void UpdateAppTitleVisibility(bool isPaneOpen)
        {
            AppTitleText.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateContentPadding(NavigationViewDisplayMode mode)
        {
            double inset = mode == NavigationViewDisplayMode.Minimal ? 12 : 24;
            ContentPanel.Padding = new Thickness(inset);
        }

        private void ShowContent(string tag, bool moveFocus = false)
        {
            var header = HeaderFor(tag);
            NavView.Header = header;
            RememberLastNavTag(tag);

            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentHost, header);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentScrollViewer, header);

            switch (tag)
            {
                case AppConstants.NavigationProfile:
                    ShowProfilePage();
                    break;
                case AppConstants.NavigationGames:
                    ShowGamesPage();
                    break;
                case AppConstants.NavigationBetas:
                    ShowInlinePanel(BetasPanel);
                    break;
                case AppConstants.NavigationSocial:
                    ShowInlinePanel(SocialPanel);
                    break;
                case AppConstants.NavigationSettings:
                    ShowInlinePanel(SettingsPanel);
                    UpdateStorageInfo();
                    break;
                default:
                    ShowInlinePanel(LatestNewsPanel);
                    break;
            }

            if (moveFocus)
            {
                FocusContentRegion();
            }
        }

        private void FocusContentRegion()
        {
            try
            {
                if (ContentScrollViewer.Visibility == Visibility.Visible)
                {
                    ContentScrollViewer.Focus(FocusState.Programmatic);
                    return;
                }

                var page = ContentFrame.Content as Control;
                if (page != null)
                {
                    page.Focus(FocusState.Programmatic);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to focus content region - {ex.Message}");
            }
        }

        private void MarkNavigationPaneLandmark()
        {
            try
            {
                var pane = VisualTreeSearch.FindDescendantByName(NavView, "PaneContentGrid");
                if (pane == null)
                {
                    Debug.WriteLine("MainPage: PaneContentGrid not found; navigation landmark not set.");
                    return;
                }

                Windows.UI.Xaml.Automation.AutomationProperties.SetLandmarkType(
                    pane, Windows.UI.Xaml.Automation.Peers.AutomationLandmarkType.Navigation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to mark navigation landmark - {ex.Message}");
            }
        }

        internal void NavigateToTag(string tag)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) return;

                SelectNavItemForTag(tag);
                ShowContent(tag, true);
                ClosePaneUnlessExpanded();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: NavigateToTag failed - {ex.Message}");
            }
        }

        private void AlignPaneToggleButton()
        {
            try
            {
                var toggleButton = VisualTreeSearch.FindDescendantByName(NavView, "TogglePaneButton") as Control;
                if (toggleButton == null) return;

                toggleButton.MinWidth = 48;
                toggleButton.Width = 48;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to align pane toggle button - {ex.Message}");
            }
        }

        private static void RememberLastNavTag(string tag)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) return;
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLastNavTag] = tag;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to record last nav tag - {ex.Message}");
            }
        }

        private static string ResolveStartupNavTag()
        {
            try
            {
                var pending = App.TakePendingLaunchNavTag();
                if (!string.IsNullOrEmpty(pending)) return pending;

                if (!App.ResumingFromTermination) return AppConstants.NavigationLatestNews;

                var saved = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLastNavTag] as string;
                if (string.IsNullOrEmpty(saved)) return AppConstants.NavigationLatestNews;

                switch (saved)
                {
                    case AppConstants.NavigationLatestNews:
                    case AppConstants.NavigationGames:
                    case AppConstants.NavigationBetas:
                    case AppConstants.NavigationProfile:
                    case AppConstants.NavigationSocial:
                    case AppConstants.NavigationSettings:
                        return saved;
                    default:
                        return AppConstants.NavigationLatestNews;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to resolve startup nav tag - {ex.Message}");
                return AppConstants.NavigationLatestNews;
            }
        }

        private void SelectNavItemForTag(string tag)
        {
            if (tag == AppConstants.NavigationSettings)
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            foreach (var item in NavView.MenuItems)
            {
                var navItem = item as NavigationViewItem;
                if (navItem != null && (navItem.Tag as string) == tag)
                {
                    NavView.SelectedItem = navItem;
                    return;
                }
            }

            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }

        private void ShowInlinePanel(UIElement panel)
        {
            CancelPendingReveal();

            ContentFrame.Visibility = Visibility.Collapsed;
            ContentScrollViewer.Visibility = Visibility.Visible;

            LatestNewsPanel.Visibility = panel == LatestNewsPanel ? Visibility.Visible : Visibility.Collapsed;
            BetasPanel.Visibility = panel == BetasPanel ? Visibility.Visible : Visibility.Collapsed;
            SocialPanel.Visibility = panel == SocialPanel ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = panel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;

            PlayPanelEnterAnimation(ContentPanel);
        }

        private static readonly TimeSpan PanelEnterDuration = TimeSpan.FromMilliseconds(300);

        private static AnimationBuilder s_panelEnterAnimation;

        private static Windows.UI.ViewManagement.UISettings s_uiSettings;

        private void PlayPanelEnterAnimation(UIElement target)
        {
            try
            {
                if (target == null) return;

                if (s_uiSettings == null)
                {
                    s_uiSettings = new Windows.UI.ViewManagement.UISettings();
                }
                if (!s_uiSettings.AnimationsEnabled)
                {
                    target.Opacity = 1;
                    return;
                }

                if (s_panelEnterAnimation == null)
                {
                    s_panelEnterAnimation = AnimationBuilder.Create()
                        .Opacity(to: 1, from: 0, duration: PanelEnterDuration,
                                 easingMode: EasingMode.EaseOut)
                        .Translation(Axis.Y, to: 0, from: 24, duration: PanelEnterDuration,
                                     easingMode: EasingMode.EaseOut);
                }

                s_panelEnterAnimation.Start(target);
            }
            catch (Exception ex)
            {
                target.Opacity = 1;
                Debug.WriteLine($"MainPage: Content enter animation failed - {ex.Message}");
            }
        }

        private bool CanGoBackInPlace()
        {
            if (ContentFrame == null) return false;
            if (ContentFrame.Visibility != Visibility.Visible) return false;
            if (!ContentFrame.CanGoBack) return false;

            return ContentFrame.Content is LoginPage;
        }

        private void OnSystemBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = TryGoBack();
        }

        private bool TryGoBack()
        {
            try
            {
                if (!CanGoBackInPlace()) return false;

                ContentFrame.GoBack();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Back navigation failed - {ex.Message}");
                return false;
            }
        }

        private void ShowProfilePage()
        {
            try
            {
                if (ContentFrame == null) return;

                if (ContentFrame.Content is ProfilePage)
                {
                    ((ProfilePage)ContentFrame.Content).RefreshUI();
                }
                else
                {
                    ContentFrame.Navigate(typeof(ProfilePage));
                    TrimContentBackStack();
                }

                RevealContentFrame();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Profile navigation failed – {ex.Message}");
                FallBackToHome();
            }
        }

        private const int MaxRevealFrames = 15;

        private FrameworkElement _revealTarget;
        private int _revealFrames;
        private bool _revealHooked;

        private void RevealContentFrame()
        {
            ContentScrollViewer.Visibility = Visibility.Collapsed;

            CancelPendingReveal();

            ContentFrame.Opacity = 0;
            ContentFrame.Visibility = Visibility.Visible;

            _revealTarget = ContentFrame.Content as FrameworkElement;
            if (_revealTarget == null)
            {
                CompletePendingReveal();
                return;
            }

            _revealFrames = 0;
            CompositionTarget.Rendering += OnRevealRendering;
            _revealHooked = true;
        }

        private void OnRevealRendering(object sender, object e)
        {
            try
            {
                _revealFrames += 1;

                var target = _revealTarget;
                var presented = target != null
                                && VisualTreeHelper.GetParent(target) != null
                                && target.ActualHeight > 0
                                && target.ActualWidth > 0;

                if (presented || _revealFrames >= MaxRevealFrames)
                {
                    Debug.WriteLine($"MainPage: content frame revealed after {_revealFrames} frame(s), presented={presented}");
                    CompletePendingReveal();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: content reveal tick failed - {ex.Message}");
                CompletePendingReveal();
            }
        }

        private void CompletePendingReveal()
        {
            CancelPendingReveal();

            ContentFrame.Opacity = 1;
        }

        private void CancelPendingReveal()
        {
            if (_revealHooked)
            {
                CompositionTarget.Rendering -= OnRevealRendering;
                _revealHooked = false;
            }

            _revealTarget = null;
        }

        private void FallBackToHome()
        {
            SelectNavItemForTag(AppConstants.NavigationLatestNews);
            ShowContent(AppConstants.NavigationLatestNews);
        }

        private void ShowGamesPage()
        {
            try
            {
                if (ContentFrame == null) return;

                if (!(ContentFrame.Content is GamesPage))
                {
                    ContentFrame.Navigate(typeof(GamesPage));
                    TrimContentBackStack();
                }

                RevealContentFrame();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Games navigation failed - {ex.GetType().Name}: {ex.Message}"
                                + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                FallBackToHome();
            }
        }

        private void TrimContentBackStack()
        {
            try
            {
                var stack = ContentFrame.BackStack;
                while (stack.Count > 1)
                {
                    stack.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to trim content back stack - {ex.Message}");
            }
        }
    }
}
