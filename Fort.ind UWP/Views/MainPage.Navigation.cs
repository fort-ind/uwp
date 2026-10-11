using System;
using System.Diagnostics;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private string _launchNavTag;

        private bool _confirmingLeave;

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
                        }
                        catch (Exception ex)
                        {
                            AppLog.Error("MainPage: UpdateProfileNavItem failed", ex);
                        }
                    });
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Auth state change handler failed", ex);
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
                AppLog.Error("MainPage: nav avatar update failed", ex);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _launchNavTag = e.Parameter as string;
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

                UpdateAppTitleVisibility(NavView.IsPaneOpen);

                AlignPaneToggleButton();
                MarkNavigationPaneLandmark();

                if (!SocialNotificationService.OwnsBadge)
                {
                    LiveTileService.ClearBadge();
                }

                if (!AppSettings.ReadBool(AppConstants.SettingHideWelcomeDialog, false))
                {
                    await ShowWelcomeDialogAsync();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: NavView_Loaded failed", ex);
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

        private bool ClosePaneUnlessExpanded()
        {
            var displayMode = NavView.DisplayMode;
            if (displayMode == NavigationViewDisplayMode.Expanded || !NavView.IsPaneOpen) return false;

            NavView.IsPaneOpen = false;
            return true;
        }

        private static string HeaderFor(string tag)
        {
            switch (tag)
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

        private static Type PageTypeFor(string tag)
        {
            switch (tag)
            {
                case AppConstants.NavigationGames:
                    return typeof(GamesPage);
                case AppConstants.NavigationBetas:
                    return typeof(BetasPage);
                case AppConstants.NavigationProfile:
                    return typeof(ProfilePage);
                case AppConstants.NavigationSocial:
                    return typeof(SocialPage);
                case AppConstants.NavigationSettings:
                    return typeof(SettingsPage);
                default:
                    return typeof(HomePage);
            }
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
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

        private void ShowContent(string tag, bool moveFocus = false)
        {
            var pageType = PageTypeFor(tag);

            if (ContentFrame.CurrentSourcePageType != pageType
                && HoldForUnsavedChanges(() =>
                {
                    SelectNavItemForTag(tag);
                    ShowContent(tag, moveFocus);
                }))
            {
                return;
            }

            var header = TitleSection(tag);

            try
            {
                if (ContentFrame.CurrentSourcePageType != pageType)
                {
                    if (!ContentFrame.Navigate(pageType, tag))
                    {
                        AppLog.Warning($"MainPage: navigation to {pageType.Name} returned false");
                        FallBackToHome(pageType);
                        return;
                    }
                }
                else
                {
                    var profile = ContentFrame.Content as ProfilePage;
                    if (profile != null) profile.RefreshUI();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"MainPage: navigation to {pageType.Name} failed", ex);
                FallBackToHome(pageType);
                return;
            }

            NameContentRegion(header);

            if (moveFocus)
            {
                FocusContentRegion();
            }
        }

        private string TitleSection(string tag)
        {
            var header = HeaderFor(tag);
            NavView.Header = header;
            RememberLastNavTag(tag);

            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentHost, header);

            return header;
        }

        private void FallBackToHome(Type failedPageType)
        {
            if (failedPageType == typeof(HomePage)) return;

            SelectNavItemForTag(AppConstants.NavigationLatestNews);
            ShowContent(AppConstants.NavigationLatestNews);
        }

        private void NameContentRegion(string header)
        {
            var region = ContentRegionOfCurrentPage();
            if (region == null) return;

            Windows.UI.Xaml.Automation.AutomationProperties.SetName(region, header);
        }

        private Control ContentRegionOfCurrentPage()
        {
            var page = ContentFrame.Content as IShellContentPage;
            return page == null ? null : page.ContentRegion;
        }

        private void FocusContentRegion()
        {
            try
            {
                var target = ContentRegionOfCurrentPage() ?? ContentFrame.Content as Control;
                if (target == null) return;

                target.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Failed to focus content region", ex);
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
                AppLog.Error("MainPage: Failed to mark navigation landmark", ex);
            }
        }

        internal void NavigateToTag(string tag, string settingsSection = null)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) return;

                var settingsCategory = tag == AppConstants.NavigationSettings
                    ? SettingsCategory.ForSection(settingsSection)
                    : null;

                var targetType = settingsCategory != null ? settingsCategory.PageType : PageTypeFor(tag);
                if (ContentFrame.CurrentSourcePageType != targetType
                    && HoldForUnsavedChanges(() => NavigateToTag(tag, settingsSection)))
                {
                    return;
                }

                SelectNavItemForTag(tag);
                ClosePaneUnlessExpanded();

                if (settingsCategory == null)
                {
                    ShowContent(tag, true);
                    return;
                }

                var subpage = ContentFrame.Content as ISettingsSubpage;
                if (subpage != null && ContentFrame.CurrentSourcePageType == settingsCategory.PageType)
                {
                    subpage.RevealSection(settingsSection);
                }
                else if (!ContentFrame.Navigate(settingsCategory.PageType, settingsSection))
                {
                    AppLog.Warning($"MainPage: navigation to {settingsCategory.PageType.Name} returned false");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: NavigateToTag failed", ex);
            }
        }

        internal void ShowSocialNotifications()
        {
            if (HoldForUnsavedChanges(ShowSocialNotifications)) return;

            NavigateToTag(AppConstants.NavigationSocial);

            var social = ContentFrame.Content as SocialPage;
            if (social != null)
            {
                social.ShowNotifications();
            }
        }

        internal void ShowSocialThread(string noteId, bool restoredReply)
        {
            try
            {
                if (HoldForUnsavedChanges(() => ShowSocialThread(noteId, restoredReply))) return;

                var thread = ContentFrame.Content as SocialNotePage;
                if (thread != null && thread.Shows(noteId))
                {
                    if (restoredReply) thread.ReloadReplyDraft();
                    SocialThreads.Open(thread, null, noteId, restoredReply, SocialThreadTab.Replies);
                    return;
                }

                if (!(ContentFrame.Content is SocialPage)) NavigateToTag(AppConstants.NavigationSocial);

                var origin = ContentFrame.Content as SocialPage;
                if (origin != null) SocialThreads.Open(origin, null, noteId, restoredReply, SocialThreadTab.Replies);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not open the note from a toast", ex);
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
                AppLog.Error("MainPage: Failed to align pane toggle button", ex);
            }
        }

        private static void RememberLastNavTag(string tag)
        {
            if (!string.IsNullOrEmpty(tag)) AppSettings.Write(AppConstants.SettingLastNavTag, tag);
        }

        private string ResolveStartupNavTag()
        {
            try
            {
                if (!string.IsNullOrEmpty(_launchNavTag)) return _launchNavTag;

                if (!App.ResumingFromTermination) return AppConstants.NavigationLatestNews;

                var saved = AppSettings.ReadString(AppConstants.SettingLastNavTag, null);
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
                AppLog.Error("MainPage: Failed to resolve startup nav tag", ex);
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

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            try
            {
                if (e.NavigationMode == NavigationMode.New)
                {
                    PruneContentBackStack(e.SourcePageType);
                }

                TitleBarBackButton.IsEnabled = ContentFrame.CanGoBack;
                TrackContentPage(e.Content as Page);

                var tagArgs = e.Content is SocialTagPage ? e.Parameter as SocialTagArgs : null;
                var settingsCategory = SettingsCategory.ForPage(e.SourcePageType);
                if (settingsCategory != null)
                {
                    TitleSettingsSubpage(settingsCategory, e.NavigationMode == NavigationMode.New ? e.Parameter as string : null);
                }
                else if (e.Content is SocialNotePage)
                {
                    TitleSubPage(LocalizedStrings.Get("HeaderNote"));
                }
                else if (tagArgs != null)
                {
                    TitleSubPage(LocalizedStrings.Format("SocialTagTitleFormat", tagArgs.Tag));
                }
                else if (e.NavigationMode == NavigationMode.Back)
                {
                    ShowSectionAfterBack(e.Parameter as string);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: content navigation bookkeeping failed", ex);
            }
        }

        private void TitleSubPage(string header)
        {
            NavView.Header = header;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentHost, header);
            NameContentRegion(header);
        }

        private void TitleSettingsSubpage(SettingsCategory category, string revealedSection)
        {
            var header = category.Title;

            SelectNavItemForTag(AppConstants.NavigationSettings);
            RememberLastNavTag(AppConstants.NavigationSettings);

            NavView.Header = category;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ContentHost, header);
            NameContentRegion(header);

            if (string.IsNullOrEmpty(revealedSection)) FocusContentRegion();
        }

        private void SettingsBreadcrumb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (HoldForUnsavedChanges(() => SettingsBreadcrumb_Click(sender, e))) return;

                var stack = ContentFrame.BackStack;
                if (stack.Count > 0 && stack[stack.Count - 1].SourcePageType == typeof(SettingsPage))
                {
                    ContentFrame.GoBack();
                    return;
                }

                ShowContent(AppConstants.NavigationSettings, true);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not go to the Settings home", ex);
            }
        }

        private void PruneContentBackStack(Type currentPageType)
        {
            try
            {
                var stack = ContentFrame.BackStack;
                var intoThread = SocialThreads.IsInPlacePage(currentPageType);

                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    var type = stack[i].SourcePageType;
                    if (type == typeof(LoginPage)
                        || type == typeof(SocialUserListPage)
                        || (!intoThread && SocialThreads.IsInPlacePage(type)))
                    {
                        stack.RemoveAt(i);
                    }
                }

                if (!intoThread && stack.Count > 0 && stack[stack.Count - 1].SourcePageType == currentPageType)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                while (stack.Count > AppConstants.ContentBackStackLimit)
                {
                    stack.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Failed to prune content back stack", ex);
            }
        }

        private void ShowSectionAfterBack(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            SelectNavItemForTag(tag);
            NameContentRegion(TitleSection(tag));
            FocusContentRegion();
        }

        private void OnSystemBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = TryGoBack();
        }

        private void TitleBarBackButton_Click(object sender, RoutedEventArgs e)
        {
            TryGoBack();
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

        private bool HoldForUnsavedChanges(Action proceed)
        {
            var page = ContentFrame.Content as IUnsavedChangesPage;
            if (page == null || !page.HasUnsavedChanges) return false;

            SelectNavItemForContentPage();
            ConfirmLeaveThen(page, proceed);
            return true;
        }

        private async void ConfirmLeaveThen(IUnsavedChangesPage page, Action proceed)
        {
            try
            {
                if (_confirmingLeave) return;

                bool leave;
                _confirmingLeave = true;
                try
                {
                    leave = await page.ConfirmLeaveAsync();
                }
                finally
                {
                    _confirmingLeave = false;
                }

                if (!ReferenceEquals(ContentFrame.Content, page)) return;

                if (leave)
                {
                    proceed();
                }
                else
                {
                    SelectNavItemForContentPage();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not confirm leaving a page with unsaved changes", ex);
            }
        }

        private void SelectNavItemForContentPage()
        {
            if (SettingsCategory.ForPage(ContentFrame.CurrentSourcePageType) != null)
            {
                SelectNavItemForTag(AppConstants.NavigationSettings);
            }
        }

        private bool TryGoBack()
        {
            try
            {
                if (DialogService.IsDialogOpen) return false;

                if (SocialMediaLightbox.CloseCurrent()) return true;

                if (ClosePaneUnlessExpanded()) return true;

                if (!ContentFrame.CanGoBack) return false;

                if (HoldForUnsavedChanges(() => TryGoBack())) return true;

                ContentFrame.GoBack();

                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: Back navigation failed", ex);
                return false;
            }
        }
    }
}
