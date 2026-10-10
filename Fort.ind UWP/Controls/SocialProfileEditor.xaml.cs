using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileEditor : UserControl
    {
        private const double BannerAspectRatio = 2.0;

        private const int AvatarDecodeSize = 80;

        private const int BannerDecodeWidth = 480;

        private const int BlurhashPixelWidth = 32;

        private const int BlurhashPixelHeight = 16;

        private bool _authHandlerAttached;

        private string _accountId;

        private string _savedName = "";

        private string _savedBio = "";

        private string _avatarUrl;

        private string _bannerUrl;

        private bool _loading;

        private bool _saving;

        private bool _settingFlags;

        public SocialProfileEditor()
        {
            this.InitializeComponent();

            Loaded += SocialProfileEditor_Loaded;
            Unloaded += SocialProfileEditor_Unloaded;
        }

        private bool IsSaving
        {
            get { return _saving; }
        }

        private void SocialProfileEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }

            ShowProfile(ProfileService.CurrentUser);
        }

        private void SocialProfileEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_authHandlerAttached)
            {
                ProfileService.AuthStateChanged -= OnAuthStateChanged;
                _authHandlerAttached = false;
            }
        }

        private async void OnAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        ShowProfile(ProfileService.CurrentUser);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialProfileEditor: could not show the profile", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: auth state change handler failed", ex);
            }
        }

        public void RefreshFromServer()
        {
            if (ProfileService.CurrentUser != null) ProfileService.RefreshNow();
        }

        private void ShowProfile(UserProfile user)
        {
            var detail = user == null ? null : SocialUserDetail.FromProfile(user);
            var signedIn = detail != null;

            SignedOutPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
            EditorPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            AccountTypePanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;

            if (!signedIn)
            {
                _accountId = null;
                ResetEdits("", "");
                return;
            }

            var sameAccount = string.Equals(_accountId, detail.User.Id, StringComparison.Ordinal);
            _accountId = detail.User.Id;

            var name = user.DisplayName ?? "";
            var bio = user.Bio ?? "";
            if (!sameAccount)
            {
                ResetEdits(name, bio);
            }
            else
            {
                RebaseField(NameBox, ref _savedName, name);
                RebaseField(BioBox, ref _savedBio, bio);
            }

            AvatarPicture.DisplayName = SocialNoteItem.DisplayNameOf(detail.User);
            if (_pendingAvatar == null) PaintAvatar(detail.User.AvatarUrl);
            if (_pendingBanner == null) PaintBanner(detail.BannerUrl, detail.BannerBlurhash);

            ShowFlags(detail.User.IsBot, detail.IsCat, detail.SpeaksAsCat);
            UpdateState();
        }

        private void RebaseField(TextBox box, ref string saved, string current)
        {
            var untouched = string.Equals(Normalize(box.Text), Normalize(saved), StringComparison.Ordinal);
            saved = current;
            if (untouched) SetText(box, current);
        }

        private void ResetEdits(string name, string bio)
        {
            _savedName = name;
            _savedBio = bio;
            SetText(NameBox, name);
            SetText(BioBox, bio);
            ClearPendingImages();
            _avatarUrl = null;
            _bannerUrl = null;
            StatusText.Visibility = Visibility.Collapsed;
        }

        private void SetText(TextBox box, string text)
        {
            _loading = true;
            try
            {
                box.Text = text ?? "";
            }
            finally
            {
                _loading = false;
            }
        }

        private static string Normalize(string text)
        {
            return SocialPostService.NormalizeText(text).Trim();
        }

        private bool NameChanged
        {
            get { return !string.Equals(Normalize(NameBox.Text), Normalize(_savedName), StringComparison.Ordinal); }
        }

        private bool BioChanged
        {
            get { return !string.Equals(Normalize(BioBox.Text), Normalize(_savedBio), StringComparison.Ordinal); }
        }

        private bool HasChanges
        {
            get { return NameChanged || BioChanged || _pendingAvatar != null || _pendingBanner != null; }
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            StatusText.Visibility = Visibility.Collapsed;
            UpdateState();
        }

        private void UpdateState()
        {
            var nameLength = SocialProfileEditService.Length(NameBox.Text);
            var bioLength = SocialProfileEditService.Length(BioBox.Text);
            NameRing.Update(nameLength, SocialProfileEditService.NameMaxLength);
            BioRing.Update(bioLength, SocialProfileEditService.BioMaxLength);

            var withinLimits = nameLength <= SocialProfileEditService.NameMaxLength && bioLength <= SocialProfileEditService.BioMaxLength;
            var changed = HasChanges;

            SaveButton.IsEnabled = !_saving && changed && withinLimits;
            DiscardButton.IsEnabled = !_saving && changed;
            ChangeAvatarButton.IsEnabled = !_saving;
            ChangeBannerButton.IsEnabled = !_saving;
            NameBox.IsReadOnly = _saving;
            BioBox.IsReadOnly = _saving;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_saving || !HasChanges) return;

                var edit = new SocialProfileEdit
                {
                    NameChanged = NameChanged,
                    Name = NameBox.Text,
                    BioChanged = BioChanged,
                    Bio = BioBox.Text,
                    Avatar = _pendingAvatar,
                    Banner = _pendingBanner
                };
                var savedName = NameBox.Text;
                var savedBio = BioBox.Text;
                var account = _accountId;

                SetSaving(true, edit.Avatar != null || edit.Banner != null);
                var progress = new Progress<double>(value => SaveProgress.Value = value);

                var saved = await SocialProfileEditService.SaveAsync(this, edit, progress);
                if (IsSaving && saved && string.Equals(account, _accountId, StringComparison.Ordinal))
                {
                    _savedName = savedName;
                    _savedBio = savedBio;
                    ClearPendingImages();
                    ShowStatus(LocalizedStrings.Get("SocialProfileSaved"));
                    ShowProfile(ProfileService.CurrentUser);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: save failed", ex);
            }
            finally
            {
                SetSaving(false, false);
            }
        }

        private void SetSaving(bool saving, bool withImages)
        {
            _saving = saving;
            SaveProgress.Value = 0;
            SaveProgress.IsIndeterminate = saving && !withImages;
            SaveProgress.Visibility = saving ? Visibility.Visible : Visibility.Collapsed;
            if (saving) StatusText.Visibility = Visibility.Collapsed;
            UpdateState();
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
            StatusText.Visibility = Visibility.Visible;
            AutomationHelper.AnnounceLiveRegion(StatusText);
        }

        private void DiscardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_saving) return;

            SetText(NameBox, _savedName);
            SetText(BioBox, _savedBio);
            ClearPendingImages();
            StatusText.Visibility = Visibility.Collapsed;

            var detail = SocialUserDetail.FromProfile(ProfileService.CurrentUser);
            if (detail != null)
            {
                _avatarUrl = null;
                _bannerUrl = null;
                PaintAvatar(detail.User.AvatarUrl);
                PaintBanner(detail.BannerUrl, detail.BannerBlurhash);
            }

            UpdateState();
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WindowManagerService.ShowSignInInMainWindowAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: could not open sign-in", ex);
            }
        }

        private void ShowFlags(bool isBot, bool isCat, bool speaksAsCat)
        {
            _settingFlags = true;
            try
            {
                BotToggle.IsOn = isBot;
                CatToggle.IsOn = isCat;
                SpeakAsCatToggle.IsOn = speaksAsCat;
                SpeakAsCatPanel.Visibility = isCat ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _settingFlags = false;
            }
        }

        private async void FlagToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            try
            {
                if (_settingFlags || toggle == null) return;

                var field = toggle == BotToggle ? SocialProfileEditService.BotField
                            : toggle == CatToggle ? SocialProfileEditService.CatField
                            : SocialProfileEditService.SpeakAsCatField;
                var value = toggle.IsOn;
                if (toggle == CatToggle) SpeakAsCatPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;

                toggle.IsEnabled = false;
                var applied = await SocialProfileEditService.SetFlagAsync(this, field, value);
                if (!applied)
                {
                    _settingFlags = true;
                    toggle.IsOn = !value;
                    _settingFlags = false;
                    if (toggle == CatToggle) SpeakAsCatPanel.Visibility = !value ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: could not change a flag", ex);
            }
            finally
            {
                if (toggle != null) toggle.IsEnabled = true;
            }
        }

        private void PaintAvatar(string url)
        {
            if (string.Equals(url, _avatarUrl, StringComparison.Ordinal)) return;
            _avatarUrl = url;

            var uri = WebLauncher.TryCreateFetchUri(url);
            if (uri == null)
            {
                AvatarPicture.ProfilePicture = null;
                return;
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = AvatarDecodeSize;
            bitmap.DecodePixelHeight = AvatarDecodeSize;
            bitmap.UriSource = uri;
            AvatarPicture.ProfilePicture = bitmap;
        }

        private void PaintBanner(string url, string blurhash)
        {
            if (string.Equals(url, _bannerUrl, StringComparison.Ordinal)) return;
            _bannerUrl = url;

            BannerPlaceholder.Fill = BlurhashImage.CreateBrush(blurhash, BlurhashPixelWidth, BlurhashPixelHeight, Stretch.Fill);

            var uri = WebLauncher.TryCreateFetchUri(url);
            if (uri == null)
            {
                BannerImage.Fill = null;
                return;
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = BannerDecodeWidth;
            bitmap.UriSource = uri;
            BannerImage.Fill = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }

        private void BannerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = e.NewSize.Width;
            if (width <= 0) return;

            var height = Math.Round(width / BannerAspectRatio);
            if (double.IsNaN(BannerHost.Height) || Math.Abs(BannerHost.Height - height) >= 1) BannerHost.Height = height;
        }
    }
}
