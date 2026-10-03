using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class ProfilePage : Page, IReleasablePage, ITrimmablePage
    {
        private const double TwoColumnMinWidth = 840;

        private const double CardGap = 24;

        private const double NotesMaxWidth = 720;

        private const double NarrowGutter = 12;

        private const double WideGutter = 24;

        private const double BannerAspectRatio = 3.0;

        private const int BannerDecodeWidth = 720;

        private const int AvatarDecodeSize = 80;

        private const int BlurhashPixelWidth = 32;

        private const int BlurhashPixelHeight = 11;

        private const double FooterMinHeight = 160;

        private const string SignOutGlyph = "";

        private const string StickyExpression = "Vector3(0, Max(0, -scroll.Translation.Y - props.tabTop), 0)";

        private const string BackdropExpression = "Clamp((-scroll.Translation.Y - props.tabTop) / 12, 0, 1)";

        private readonly SocialProfileFeeds _feeds = new SocialProfileFeeds();

        private bool _authHandlerAttached = false;

        private bool _noteHandlerAttached;

        private int _pinsVersion;

        private int _seenPostsVersion = SocialNoteService.PostsVersion;

        private bool _keepListsOnUnload;

        private UserProfile _shownProfile;

        private SocialUserDetail _detail;

        private bool _detailFromServer;

        private int _loadVersion;

        private bool _twoColumns = true;

        private string _avatarUrl;

        private BitmapImage _bannerBitmap;

        private ScrollViewer _scroller;

        private CompositionPropertySet _scrollProperties;

        private CompositionPropertySet _stickyProperties;

        private bool _stickyAttached;

        private double _tabTop;

        private SocialFeedState _state = SocialFeedState.Loading;

        private bool _released;

        private AcrylicBrush _tabBackdropBrush;

        public ProfilePage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            _feeds.StateChanged += Feeds_StateChanged;
            Details.FollowListRequested += Details_FollowListRequested;

            Loaded += ProfilePage_Loaded;
            Unloaded += ProfilePage_Unloaded;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            _keepListsOnUnload = e.SourcePageType == typeof(SocialNotePage);
        }

        private void ProfilePage_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachAuthHandler();
            AppearanceService.DetachInAppSurface(_tabBackdropBrush);
            if (!_keepListsOnUnload) Trim();
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            DetachAuthHandler();
            AppearanceService.DetachInAppSurface(_tabBackdropBrush);
            _feeds.Release();
        }

        public void Trim()
        {
            if (_released) return;

            _feeds.TrimAll();
        }

        private void DetachAuthHandler()
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }

            if (_noteHandlerAttached)
            {
                SocialNoteService.Changed -= OnNoteChanged;
                _noteHandlerAttached = false;
            }
        }

        private async void OnNoteChanged(object sender, SocialNoteChange change)
        {
            try
            {
                if (change == null || change.Kind == SocialNoteChangeKind.Reset) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        ApplyNoteChange(change);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ProfilePage: could not apply a note change - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: note change handler failed - {ex.Message}");
            }
        }

        private void ApplyNoteChange(SocialNoteChange change)
        {
            if (_released) return;

            if (change.Kind == SocialNoteChangeKind.PinChanged)
            {
                ReloadPinned();
                return;
            }

            _feeds.ApplyChange(change);
            if (change.Kind == SocialNoteChangeKind.Posted) _seenPostsVersion = SocialNoteService.PostsVersion;
        }

        private void CatchUpNoteChanges()
        {
            _feeds.Sweep();

            foreach (var note in SocialNoteService.PostsSince(_seenPostsVersion))
            {
                _feeds.ApplyChange(SocialNoteChange.Posted(note));
            }
            _seenPostsVersion = SocialNoteService.PostsVersion;

            if (_feeds.FavoritesChangedSinceLoad) _feeds.Invalidate(SocialUserNotesTab.Favorites);
            if (_detailFromServer && _pinsVersion != SocialNoteService.PinsVersion) ReloadPinned();
        }

        private void ReloadPinned()
        {
            _pinsVersion = SocialNoteService.PinsVersion;
            if (string.IsNullOrEmpty(_feeds.UserId)) return;

            if (_feeds.Current == SocialUserNotesTab.Notes)
            {
                _feeds.Refresh();
                LoadOnline(SocialUserNotesTab.Notes);
            }
            else
            {
                _feeds.Invalidate(SocialUserNotesTab.Notes);
            }
        }

        private async void OnAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        try
                        {
                            RefreshUI();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ProfilePage: RefreshUI failed - {ex.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Auth state change handler failed - {ex.Message}");
            }
        }

        private void ProfilePage_Loaded(object sender, RoutedEventArgs e)
        {
            _keepListsOnUnload = false;
            if (_released) return;

            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }

            if (!_noteHandlerAttached)
            {
                SocialNoteService.Changed += OnNoteChanged;
                _noteHandlerAttached = true;
            }

            _tabBackdropBrush = AppearanceService.AttachInAppSurface(_tabBackdropBrush);
            TabBackdrop.Background = _tabBackdropBrush;

            RefreshUI();
            CatchUpNoteChanges();

            ProfileService.RefreshOnProfileVisit();
        }

        public void RefreshUI()
        {
            if (_released) return;

            var user = ProfileService.CurrentUser;
            var detail = user == null ? null : SocialUserDetail.FromProfile(user);

            if (detail == null)
            {
                ShowSignedOut();
                return;
            }

            SetSignedIn(true);

            if (ReferenceEquals(user, _shownProfile)) return;

            var sameAccount = _shownProfile != null
                              && string.Equals(_shownProfile.UserId, user.UserId, StringComparison.Ordinal);
            _shownProfile = user;

            if (sameAccount)
            {
                PaintDetail(detail, false);
                return;
            }

            ShowAccount(detail);
        }

        private void ShowAccount(SocialUserDetail detail)
        {
            _loadVersion++;
            _detail = null;
            _detailFromServer = false;
            _avatarUrl = null;
            AvatarPicture.ProfilePicture = null;
            ClearBanner();

            _feeds.Reset(detail.User.Id);
            _feeds.Emojis = SocialContentService.CachedEmojiMap;
            PaintDetail(detail, false);

            _feeds.BeginLoading(SocialUserNotesTab.Notes);
            if (NotesTab.IsChecked == true)
            {
                SelectTab(SocialUserNotesTab.Notes);
            }
            else
            {
                NotesTab.IsChecked = true;
            }

            ScrollToTop();
            LoadOnline(SocialUserNotesTab.Notes);
        }

        private void ShowSignedOut()
        {
            SetSignedIn(false);

            if (_shownProfile == null) return;

            _shownProfile = null;
            _loadVersion++;
            _detail = null;
            _detailFromServer = false;
            _feeds.Reset(null);
            Details.Clear();
        }

        private void SetSignedIn(bool signedIn)
        {
            DashboardGrid.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            SignedOutScroller.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void LoadOnline(SocialUserNotesTab tab)
        {
            var version = ++_loadVersion;

            try
            {
                var userId = _feeds.UserId;
                if (string.IsNullOrEmpty(userId)) return;

                var emojiTask = SocialContentService.GetEmojiMapAsync();
                var notesTask = FetchFirstNotesAsync(userId, tab);

                var result = await SocialContentService.FetchUserAsync(userId, _feeds.Token);
                if (version != _loadVersion || _released) return;

                var emojis = await emojiTask;
                if (emojis != null) _feeds.Emojis = emojis;
                var notes = await notesTask;
                if (version != _loadVersion || _released) return;

                if (result.Status == SocialApiStatus.Ok)
                {
                    PaintDetail(result.Value, true);
                }
                else if (_detail != null)
                {
                    PaintDetail(_detail, false);
                }

                _feeds.ApplyFirstPage(tab, notes);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("ProfilePage: profile load cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: profile load failed - {ex.GetType().Name}: {ex.Message}");
                if (version == _loadVersion && !_released) _feeds.Fail(tab);
            }
        }

        private async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchFirstNotesAsync(string userId, SocialUserNotesTab tab)
        {
            try
            {
                return await SocialContentService.FetchUserNotesAsync(userId, tab, null, _feeds.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: first notes fetch failed - {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private void PaintDetail(SocialUserDetail detail, bool fromServer)
        {
            _detail = detail;
            if (fromServer)
            {
                _detailFromServer = true;
                _feeds.PinnedNotes = detail.PinnedNotes;
                RememberPinned(detail);
            }

            PaintIdentity(detail.User);
            Details.Paint(detail, _feeds.Emojis ?? SocialContentService.CachedEmojiMap);
            PaintBanner(detail.BannerUrl, detail.BannerBlurhash);
        }

        private void RememberPinned(SocialUserDetail detail)
        {
            if (detail.User == null || !SocialContentService.IsCurrentAccount(detail.User.Id)) return;

            var ids = new List<string>();
            foreach (var note in detail.PinnedNotes)
            {
                ids.Add(note.Id);
            }

            SocialNoteService.RememberPinned(ids);
            _pinsVersion = SocialNoteService.PinsVersion;
        }

        private void PaintIdentity(SocialUser user)
        {
            MfmInlineBuilder.Fill(NameText, MfmText.ParseName(user.DisplayName, user.Emojis), false, 24);
            HandleText.Text = SocialLinks.FormatHandle(user.Username,
                                                       string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host);
            AvatarPicture.DisplayName = SocialNoteItem.DisplayNameOf(user);

            if (string.Equals(user.AvatarUrl, _avatarUrl, StringComparison.Ordinal)) return;
            _avatarUrl = user.AvatarUrl;

            var avatarUri = WebLauncher.TryCreateFetchUri(user.AvatarUrl);
            if (avatarUri == null)
            {
                AvatarPicture.ProfilePicture = null;
                return;
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = AvatarDecodeSize;
            bitmap.DecodePixelHeight = AvatarDecodeSize;
            bitmap.UriSource = avatarUri;
            AvatarPicture.ProfilePicture = bitmap;
        }

        private void PaintBanner(string bannerUrl, string blurhash)
        {
            BannerPlaceholder.Fill = BlurhashImage.CreateBrush(blurhash, BlurhashPixelWidth, BlurhashPixelHeight, Stretch.Fill);

            var bannerUri = WebLauncher.TryCreateFetchUri(bannerUrl);
            if (bannerUri == null)
            {
                _bannerBitmap = null;
                BannerImage.Fill = null;
                BannerImage.Opacity = 0;
                return;
            }

            if (_bannerBitmap != null && _bannerBitmap.UriSource == bannerUri) return;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = BannerDecodeWidth;
            bitmap.ImageOpened += BannerBitmap_ImageOpened;
            bitmap.UriSource = bannerUri;
            _bannerBitmap = bitmap;

            BannerImage.Opacity = 0;
            BannerImage.Fill = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }

        private void ClearBanner()
        {
            _bannerBitmap = null;
            BannerImage.Fill = null;
            BannerImage.Opacity = 0;
            BannerPlaceholder.Fill = null;
        }

        private void BannerBitmap_ImageOpened(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, _bannerBitmap)) return;
            BannerImage.Opacity = 1;
        }

        private void BannerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = e.NewSize.Width;
            if (width <= 0) return;

            var height = Math.Round(width / BannerAspectRatio);
            if (double.IsNaN(BannerHost.Height) || Math.Abs(BannerHost.Height - height) >= 1)
            {
                BannerHost.Height = height;
            }
        }

        private void DashboardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyDashboardLayout();
        }

        private void WindowWidthStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        {
            ApplyDashboardLayout();
        }

        private void ApplyDashboardLayout()
        {
            try
            {
                var width = DashboardGrid.ActualWidth;
                if (width <= 0) return;

                var gutter = WindowWidthStates.CurrentState == NarrowState ? NarrowGutter : WideGutter;
                var content = Math.Max(0, width - 2 * gutter);
                var twoColumns = content >= TwoColumnMinWidth;
                ApplyLayout(twoColumns);

                var aside = twoColumns ? CardScroller.Width + CardGap : 0;
                var block = Math.Min(content, aside + NotesMaxWidth);
                var left = Math.Floor((width - block) / 2);

                var cardMargin = new Thickness(left, 0, 0, 0);
                if (CardScroller.Margin != cardMargin) CardScroller.Margin = cardMargin;

                var notesPadding = new Thickness(left + aside, 0, width - left - block, 0);
                if (NotesList.Padding != notesPadding) NotesList.Padding = notesPadding;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not lay out the dashboard - {ex.Message}");
            }
        }

        private void ApplyLayout(bool twoColumns)
        {
            if (twoColumns == _twoColumns) return;
            _twoColumns = twoColumns;

            if (twoColumns)
            {
                NarrowCardSlot.Child = null;
                NarrowCardSlot.Visibility = Visibility.Collapsed;
                CardScroller.Content = ProfileCard;
                CardScroller.Visibility = Visibility.Visible;
            }
            else
            {
                CardScroller.Content = null;
                CardScroller.Visibility = Visibility.Collapsed;
                NarrowCardSlot.Child = ProfileCard;
                NarrowCardSlot.Visibility = Visibility.Visible;
            }
        }

        private void ListHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTabTop();
        }

        private void UpdateTabTop()
        {
            _tabTop = NarrowCardSlot.Visibility == Visibility.Visible
                      ? NarrowCardSlot.ActualHeight + NarrowCardSlot.Margin.Bottom
                      : 0;

            if (_stickyProperties != null) _stickyProperties.InsertScalar("tabTop", (float)_tabTop);
            ApplyFooterHeight();
        }

        private void NotesList_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AttachStickyTabs();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: the tabs could not be made sticky - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void AttachStickyTabs()
        {
            if (_stickyAttached || _released) return;

            var scroller = VisualTreeSearch.FindDescendantByName(NotesList, "ScrollViewer") as ScrollViewer;
            if (scroller == null)
            {
                Debug.WriteLine("ProfilePage: the list has no ScrollViewer yet; the tabs will scroll away");
                return;
            }

            _stickyAttached = true;
            _scroller = scroller;
            RaiseHeaderAboveItems();

            _scrollProperties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_scroller);
            var compositor = _scrollProperties.Compositor;
            _stickyProperties = compositor.CreatePropertySet();
            _stickyProperties.InsertScalar("tabTop", (float)_tabTop);

            ElementCompositionPreview.SetIsTranslationEnabled(TabBar, true);
            Animate(ElementCompositionPreview.GetElementVisual(TabBar), "Translation", StickyExpression);
            Animate(ElementCompositionPreview.GetElementVisual(TabBackdrop), "Opacity", BackdropExpression);

            ApplyFooterHeight();
        }

        private void Animate(CompositionObject target, string property, string expression)
        {
            var animation = target.Compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("scroll", _scrollProperties);
            animation.SetReferenceParameter("props", _stickyProperties);
            target.StartAnimation(property, animation);
        }

        private void RaiseHeaderAboveItems()
        {
            var presenter = VisualTreeHelper.GetParent(ListHeader) as UIElement;
            var container = presenter == null ? null : VisualTreeHelper.GetParent(presenter) as UIElement;
            if (container == null)
            {
                Debug.WriteLine("ProfilePage: no header container found; notes may draw over the pinned tabs");
                return;
            }

            Canvas.SetZIndex(container, 1);
        }

        private void NotesList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyFooterHeight();
        }

        private void ApplyFooterHeight()
        {
            var minHeight = FooterMinHeight;
            if (_scroller != null && _state != SocialFeedState.Ready)
            {
                minHeight = Math.Max(minHeight, _scroller.ViewportHeight - TabBar.ActualHeight);
            }

            if (Math.Abs(FooterHost.MinHeight - minHeight) >= 1)
            {
                FooterHost.MinHeight = minHeight;
            }
        }

        private bool IsTabsPinned()
        {
            return _scroller != null && _scroller.VerticalOffset >= _tabTop - 1;
        }

        private void Repin()
        {
            try
            {
                NotesList.UpdateLayout();
                _scroller.ChangeView(null, _tabTop, null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not keep the tabs pinned - {ex.Message}");
            }
        }

        private void ScrollToTop()
        {
            try
            {
                if (_scroller != null) _scroller.ChangeView(null, 0, null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not scroll back to the top - {ex.Message}");
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == NotesTab) SelectTab(SocialUserNotesTab.Notes);
            else if (sender == RepliesTab) SelectTab(SocialUserNotesTab.Replies);
            else if (sender == MediaTab) SelectTab(SocialUserNotesTab.Media);
            else if (sender == FavoritesTab) SelectTab(SocialUserNotesTab.Favorites);
        }

        private void SelectTab(SocialUserNotesTab tab)
        {
            if (_released || string.IsNullOrEmpty(_feeds.UserId)) return;

            var keepPinned = IsTabsPinned();
            _feeds.Select(tab);
            NotesList.ItemsSource = _feeds.CollectionOf(tab);
            SetState(_feeds.CurrentState);

            if (keepPinned) Repin();
        }

        private void Feeds_StateChanged(object sender, SocialUserNotesTab tab)
        {
            if (tab == _feeds.Current) SetState(_feeds.StateOf(tab));
        }

        private void SetState(SocialFeedState state)
        {
            _state = state;

            LoadingRing.IsActive = state == SocialFeedState.Loading;
            LoadingRing.Visibility = state == SocialFeedState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state == SocialFeedState.Empty || state == SocialFeedState.Failed || state == SocialFeedState.NeedsSignIn;
            StatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            FooterHost.Visibility = state == SocialFeedState.Ready ? Visibility.Collapsed : Visibility.Visible;
            ApplyFooterHeight();
            if (!showPanel) return;

            if (state == SocialFeedState.Empty)
            {
                StateGlyph.Glyph = "";
                StateText.Text = LocalizedStrings.Get(_feeds.Current == SocialUserNotesTab.Favorites ? "SocialProfileFavoritesEmpty"
                                                      : _feeds.Current == SocialUserNotesTab.Media ? "SocialProfileMediaEmpty" : "SocialProfileNotesEmpty");
                if (_feeds.Current == SocialUserNotesTab.Favorites) StateGlyph.Glyph = "";
                StateButton.Visibility = Visibility.Collapsed;
            }
            else if (state == SocialFeedState.NeedsSignIn)
            {
                StateGlyph.Glyph = "";
                StateText.Text = LocalizedStrings.Get("SocialProfileFavoritesNeedsSignIn");
                StateButton.Content = LocalizedStrings.Get("SocialSignInAgainButton");
                StateButton.Visibility = Visibility.Visible;
            }
            else
            {
                StateGlyph.Glyph = "";
                StateText.Text = LocalizedStrings.Get("SocialProfileNotesFailed");
                StateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                StateButton.Visibility = Visibility.Visible;
            }

            AutomationHelper.AnnounceLiveRegion(StateText);
        }

        private void StateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state == SocialFeedState.NeedsSignIn)
            {
                SignInButton_Click(sender, e);
                return;
            }

            if (_state != SocialFeedState.Failed) return;

            if (!_detailFromServer)
            {
                _feeds.BeginLoading(_feeds.Current);
                LoadOnline(_feeds.Current);
                return;
            }

            _feeds.Retry();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_released || string.IsNullOrEmpty(_feeds.UserId)) return;

            _feeds.Refresh();
            ProfileService.RefreshNow();
            LoadOnline(_feeds.Current);
        }

        private async void NotesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as SocialNoteItem;
                if (item == null) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                if (MfmInlineBuilder.WasLinkJustInvoked) return;

                if (!SocialThreads.Open(this, item.Note, item.Note.Id, false, SocialThreadTab.Replies))
                {
                    await WebLauncher.LaunchAsync(item.NoteUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not open the note - {ex.Message}");
            }
        }

        private async void ManageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WebLauncher.LaunchAsync(SocialLinks.ProfileSettingsUrl());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not open the profile settings - {ex.Message}");
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var user = _detail == null ? null : _detail.User;
                var flyout = SocialMenus.BuildForUser(user, null, this) ?? new MenuFlyout();
                if (flyout.Items.Count > 0) flyout.Items.Add(new MenuFlyoutSeparator());

                var signOut = new MenuFlyoutItem
                {
                    Text = LocalizedStrings.Get("SocialProfileSignOutMenuItem"),
                    Icon = new FontIcon { Glyph = SignOutGlyph }
                };
                signOut.Click += SignOutItem_Click;
                flyout.Items.Add(signOut);

                SocialMenus.ShowAt(flyout, MoreButton, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not show the profile menu - {ex.Message}");
            }
        }

        private async void SignOutItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirmed = await DialogService.ShowConfirmAsync(
                    this,
                    LocalizedStrings.Get("SignOutDialogTitle"),
                    LocalizedStrings.Get("SignOutDialogBody"),
                    LocalizedStrings.Get("SignOutDialogConfirm"),
                    LocalizedStrings.Get("DialogCancel"),
                    ContentDialogButton.Close);

                if (!confirmed) return;

                await ProfileService.LogoutAsync();
                RefreshUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Logout failed – {ex.Message}");
            }
        }

        private void Details_FollowListRequested(object sender, SocialFollowList list)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                if (account == null || Frame == null || string.IsNullOrEmpty(_feeds.UserId)) return;

                var user = _detail == null ? null : _detail.User;
                Frame.Navigate(typeof(SocialUserListPage),
                               new SocialUserListArgs(account, _feeds.UserId, user == null ? null : user.Handle, user, list));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: could not open the follow list - {ex.GetType().Name}: {ex.Message}"
                                + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
            }
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            bool showNavigationError = false;

            try
            {
                Frame.Navigate(typeof(LoginPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Sign-in navigation failed - {ex.GetType().Name}: {ex.Message}"
                                + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                showNavigationError = true;
            }

            if (showNavigationError)
            {
                try
                {
                    await DialogService.ShowMessageAsync(this,
                                                         LocalizedStrings.Get("NavigationErrorDialogTitle"),
                                                         LocalizedStrings.Get("NavigationErrorDialogBody"),
                                                         LocalizedStrings.Get("DialogOk"));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProfilePage: could not report the navigation failure - {ex.Message}");
                }
            }
        }
    }
}
