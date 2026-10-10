using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileView : UserControl
    {
        private enum ListState
        {
            Loading,
            Ready,
            Empty,
            Failed,
            ProfileFailed,
            Suspended,
            Moved
        }

        private const double BannerAspectRatio = 3.0;

        private const int BannerDecodeWidth = 680;

        private const int AvatarDecodeSize = 96;

        private const int BlurhashPixelWidth = 32;

        private const int BlurhashPixelHeight = 11;

        private readonly SocialProfileFeeds _feeds = new SocialProfileFeeds();

        private string _userId;

        private string _handle;

        private SocialUser _initialUser;

        private SocialUserDetail _detail;

        private int _profileVersion;

        private ListState _state = ListState.Loading;

        private BitmapImage _bannerBitmap;

        private bool _released;

        private bool IsReleased => _released;

        public SocialProfileView()
        {
            this.InitializeComponent();

            _animationsEnabled = _uiSettings.AnimationsEnabled;
            SetUpRevealAnimations();

            _feeds.StateChanged += Feeds_StateChanged;
            Details.FollowListRequested += Details_FollowListRequested;
        }

        public event EventHandler<SocialFollowList> FollowListRequested;

        public event EventHandler<SocialUserDetail> ProfileLoaded;

        public string UserId
        {
            get { return _userId; }
        }

        public SocialUser User
        {
            get { return _detail != null ? _detail.User : _initialUser; }
        }

        public string Handle
        {
            get
            {
                var user = User;
                return user != null ? user.Handle : _handle;
            }
        }

        public void ShowUser(string userId, string handle, SocialUser user, SocialUserDetail detail)
        {
            if (string.IsNullOrEmpty(userId) || _released) return;

            _userId = userId;
            _handle = handle;
            _initialUser = user;
            _feeds.Reset(userId);
            PaintIdentity(user, handle);
            LoadProfile(detail, SocialUserNotesTab.Notes);
        }

        public void Refresh()
        {
            if (_released || string.IsNullOrEmpty(_userId)) return;

            if (_detail == null || IsUnavailable(_detail))
            {
                LoadProfile(null, SocialUserNotesTab.Notes);
                return;
            }

            _feeds.Refresh();
            LoadProfile(null, _feeds.Current);
        }

        internal bool KeepListsOnNextUnload { get; set; }

        public void Trim()
        {
            if (_released) return;

            _feeds.TrimAll();
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            DetachEffectsSettings();
            DetachFollowHandler();
            DetachNoteHandler();
            _feeds.Release();
        }

        private async void LoadProfile(SocialUserDetail known, SocialUserNotesTab firstTab)
        {
            var version = ++_profileVersion;
            var hasHeader = _detail != null && !IsUnavailable(_detail);
            if (!hasHeader)
            {
                TabBar.Visibility = Visibility.Collapsed;
                SetState(ListState.Loading);
            }

            try
            {
                var userId = _userId;
                var emojiTask = SocialContentService.GetEmojiMapAsync();
                var notesTask = FetchFirstNotesAsync(userId, firstTab);

                var detail = known;
                if (detail == null)
                {
                    var result = await SocialContentService.FetchUserAsync(userId, _feeds.Token);
                    if (version != _profileVersion || IsReleased) return;

                    if (result.Status == SocialApiStatus.Ok)
                    {
                        detail = result.Value;
                    }
                    else if (!hasHeader)
                    {
                        SetState(ListState.ProfileFailed);
                        return;
                    }
                }

                var emojis = await emojiTask;
                if (emojis != null) _feeds.Emojis = emojis;
                var notes = await notesTask;
                if (version != _profileVersion || IsReleased) return;

                ApplyDetail(detail ?? _detail, detail != null);
                if (IsUnavailable(_detail)) return;

                _feeds.ApplyFirstPage(firstTab, notes);
                if (!TabBar.IsShown()) RevealTabs(firstTab);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialProfileView: profile load cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileView: profile load failed", ex);
                if (version == _profileVersion && !IsReleased)
                {
                    if (_detail != null && !IsUnavailable(_detail))
                    {
                        _feeds.Fail(firstTab);
                    }
                    else
                    {
                        SetState(ListState.ProfileFailed);
                    }
                }
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
                AppLog.Error("SocialProfileView: first notes fetch failed", ex);
                return null;
            }
        }

        private static bool IsUnavailable(SocialUserDetail detail)
        {
            return detail != null && (detail.IsSuspended || detail.HasMoved);
        }

        private void ApplyDetail(SocialUserDetail detail, bool fromServer)
        {
            if (detail == null) return;

            _detail = detail;
            if (fromServer) _feeds.PinnedNotes = detail.PinnedNotes;
            var user = detail.User;

            PaintIdentity(user, user.Handle);
            Details.Paint(detail, _feeds.Emojis ?? SocialContentService.CachedEmojiMap);
            UpdateDetailsVisibility();
            PaintBanner(detail.BannerUrl, detail.BannerBlurhash);
            UpdateFollowButtons();

            if (fromServer)
            {
                var handler = ProfileLoaded;
                if (handler != null) handler(this, detail);
            }

            if (detail.IsSuspended)
            {
                SetState(ListState.Suspended);
            }
            else if (detail.HasMoved)
            {
                SetState(ListState.Moved);
            }
        }

        private void RevealTabs(SocialUserNotesTab tab)
        {
            TabBar.Visibility = Visibility.Visible;

            var button = TabButtonFor(tab);
            if (button.IsChecked.GetValueOrDefault())
            {
                SelectTab(tab);
            }
            else
            {
                button.IsChecked = true;
            }
        }

        private SocialTabButton TabButtonFor(SocialUserNotesTab tab)
        {
            switch (tab)
            {
                case SocialUserNotesTab.Replies:
                    return RepliesTab;
                case SocialUserNotesTab.Media:
                    return MediaTab;
                default:
                    return NotesTab;
            }
        }

        private void PaintIdentity(SocialUser user, string handle)
        {
            var name = user == null ? handle : SocialNoteItem.DisplayNameOf(user);

            IReadOnlyList<MfmSegment> nameSegments;
            if (user != null)
            {
                nameSegments = MfmText.ParseName(user.DisplayName, user.Emojis);
                HandleText.Text = SocialLinks.FormatHandle(user.Username, string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host);
            }
            else
            {
                nameSegments = new[] { new MfmSegment { Kind = MfmSegmentKind.Text, Text = name ?? "" } };
                HandleText.Text = handle ?? "";
            }

            MfmInlineBuilder.Fill(NameText, nameSegments, false, 24);
            MfmInlineBuilder.Fill(CompactNameText, nameSegments, false, MfmInlineBuilder.NameEmojiSize);
            CompactHandleText.Text = HandleText.Text;

            AvatarPicture.DisplayName = name ?? "";

            var avatarUri = user == null ? null : WebLauncher.TryCreateFetchUri(user.AvatarUrl);
            if (avatarUri != null && (AvatarPicture.ProfilePicture as BitmapImage)?.UriSource != avatarUri)
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = AvatarDecodeSize;
                bitmap.DecodePixelHeight = AvatarDecodeSize;
                bitmap.UriSource = avatarUri;
                AvatarPicture.ProfilePicture = bitmap;
            }
        }

        private void PaintBanner(string bannerUrl, string blurhash)
        {
            _bannerPixels = BlurhashImage.Decode(blurhash, BlurhashPixelWidth, BlurhashPixelHeight);
            BannerPlaceholder.Fill = BlurhashImage.CreateBrush(_bannerPixels, BlurhashPixelWidth, BlurhashPixelHeight, Stretch.Fill);
            UpdateBannerThemes();

            var bannerUri = WebLauncher.TryCreateFetchUri(bannerUrl);
            if (bannerUri == null)
            {
                _bannerBitmap = null;
                BannerImage.Fill = null;
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

        private void BannerBitmap_ImageOpened(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, _bannerBitmap)) return;
            BannerImage.Opacity = 1;
        }

        private void BannerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                ApplyBannerHeight(e.NewSize.Width);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileView: banner layout failed", ex);
            }
        }

        private void UpdateDetailsVisibility()
        {
            DetailsPanel.Visibility = Details.HasContent ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == NotesTab) SelectTab(SocialUserNotesTab.Notes);
            else if (sender == RepliesTab) SelectTab(SocialUserNotesTab.Replies);
            else if (sender == MediaTab) SelectTab(SocialUserNotesTab.Media);
        }

        private void SelectTab(SocialUserNotesTab tab)
        {
            if (_detail == null || _released) return;

            var keepPinned = IsHeaderPinned();
            _feeds.Select(tab);
            NotesList.ItemsSource = _feeds.CollectionOf(tab);
            ShowFeedState();

            if (keepPinned) RepinHeader();
        }

        private void Feeds_StateChanged(object sender, SocialUserNotesTab tab)
        {
            if (tab == _feeds.Current && _detail != null && !IsUnavailable(_detail)) ShowFeedState();
        }

        private void ShowFeedState()
        {
            switch (_feeds.CurrentState)
            {
                case SocialFeedState.Ready:
                    SetState(ListState.Ready);
                    break;
                case SocialFeedState.Empty:
                    SetState(ListState.Empty);
                    break;
                case SocialFeedState.Failed:
                    SetState(ListState.Failed);
                    break;
                default:
                    SetState(ListState.Loading);
                    break;
            }
        }

        private void SetState(ListState state)
        {
            _state = state;

            LoadingRing.IsActive = state == ListState.Loading;
            LoadingRing.Visibility = state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state != ListState.Loading && state != ListState.Ready;
            StatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            FooterHost.Visibility = state == ListState.Ready ? Visibility.Collapsed : Visibility.Visible;
            ApplyFooterHeight();
            if (!showPanel) return;

            if (state == ListState.ProfileFailed || state == ListState.Suspended || state == ListState.Moved)
            {
                TabBar.Visibility = Visibility.Collapsed;
            }

            switch (state)
            {
                case ListState.Empty:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get(_feeds.Current == SocialUserNotesTab.Media ? "SocialProfileMediaEmpty" : "SocialProfileNotesEmpty");
                    StateButton.Visibility = Visibility.Collapsed;
                    break;
                case ListState.Failed:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialProfileNotesFailed");
                    StateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
                case ListState.ProfileFailed:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialProfileFailed");
                    StateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
                case ListState.Suspended:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialProfileSuspended");
                    StateButton.Visibility = Visibility.Collapsed;
                    break;
                case ListState.Moved:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialProfileMoved");
                    StateButton.Content = LocalizedStrings.Get("SocialProfileOpenOnFortSocialButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
            }

            AutomationHelper.AnnounceLiveRegion(StateText);
        }

        private async void StateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                switch (_state)
                {
                    case ListState.ProfileFailed:
                        LoadProfile(null, SocialUserNotesTab.Notes);
                        break;
                    case ListState.Failed:
                        _feeds.Retry();
                        break;
                    case ListState.Moved:
                        await OpenOnInstanceAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileView: state action failed", ex);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private async Task OpenOnInstanceAsync()
        {
            var url = SocialLinks.UserUrl(User);
            if (url == null && _handle != null) url = SocialLinks.InstanceUrl("/" + _handle);

            await WebLauncher.LaunchAsync(url);
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
                AppLog.Error("SocialProfileView: could not open the note", ex);
            }
        }

        private void Details_FollowListRequested(object sender, SocialFollowList list)
        {
            if (_released || string.IsNullOrEmpty(_userId)) return;

            var handler = FollowListRequested;
            if (handler != null) handler(this, list);
        }
    }
}
