using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class ProfilePage : Page
    {
        private const double BannerAspectRatio = 3.0;

        private const double BannerRingWidth = 4;

        private const int BannerDecodeWidth = 600;

        private const int BlurhashPixelWidth = 32;

        private const int BlurhashPixelHeight = 11;

        private bool _authHandlerAttached = false;

        private string _lastAvatarUrl = null;

        private bool _avatarApplied = false;

        private string _lastBannerUrl = null;

        private string _lastBannerBlurhash = null;

        private bool _bannerApplied = false;

        private bool _bannerHasPlaceholder = false;

        private BitmapImage _bannerBitmap = null;

        private string _failedBannerUrl = null;

        private bool _bannerRevealPending = false;

        public ProfilePage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

            Loaded += ProfilePage_Loaded;
            Unloaded += ProfilePage_Unloaded;
        }

        private void ProfilePage_Unloaded(object sender, RoutedEventArgs e)
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
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
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
            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }
            RefreshUI();
        }

        public void RefreshUI()
        {
            if (ProfileService.CurrentUser != null)
            {
                ShowLoggedInState();
            }
            else
            {
                ShowNotLoggedInState();
            }
        }

        private void ShowLoggedInState()
        {
            NotLoggedInPanel.Visibility = Visibility.Collapsed;
            LoggedInPanel.Visibility = Visibility.Visible;

            var user = ProfileService.CurrentUser;
            var host = string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host;

            DisplayNameText.Text = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
            UsernameText.Text = LocalizedStrings.Format("ProfileHandleFormat", user.Username, host);

            if (user.CreatedDate > DateTime.MinValue)
            {
                MemberSinceText.Text = LocalizedStrings.Format("ProfileMemberSinceFormat", FormatMemberSince(user.CreatedDate));
                MemberSinceText.Visibility = Visibility.Visible;
            }
            else
            {
                MemberSinceText.Visibility = Visibility.Collapsed;
            }

            if (user.LastLoginDate > DateTime.MinValue)
            {
                LastLoginText.Text = LocalizedStrings.Format("ProfileLastSignedInFormat", FormatLastLogin(user.LastLoginDate));
                LastLoginText.Visibility = Visibility.Visible;
            }
            else
            {
                LastLoginText.Visibility = Visibility.Collapsed;
            }

            var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
            ProfileInitials.Text = GetInitials(name);

            BioText.Text = string.IsNullOrWhiteSpace(user.Bio) ? LocalizedStrings.Get("ProfileNoBio") : user.Bio;

            if (UpdateAvatarUI(user.AvatarUrl))
            {
                PlayAvatarFadeIn();
            }

            AvatarGrid.Opacity = 1;

            UpdateBannerUI(user.BannerUrl, user.BannerBlurhash);
        }

        private void PlayAvatarFadeIn()
        {
            try
            {
                var storyboard = this.Resources["AvatarFadeIn"] as Storyboard;
                if (storyboard == null)
                {
                    AvatarGrid.Opacity = 1;
                    return;
                }

                storyboard.Stop();
                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: avatar fade-in failed - {ex.Message}");
                AvatarGrid.Opacity = 1;
            }
        }

        private void ShowNotLoggedInState()
        {
            NotLoggedInPanel.Visibility = Visibility.Visible;
            LoggedInPanel.Visibility = Visibility.Collapsed;
            _lastAvatarUrl = null;
            _avatarApplied = false;
            _lastBannerUrl = null;
            _lastBannerBlurhash = null;
            _bannerApplied = false;
            _failedBannerUrl = null;
            HideBanner();
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

        private async void ManageOnFortSocialButton_Click(object sender, RoutedEventArgs e)
        {
            var user = ProfileService.CurrentUser;
            if (user == null) return;

            try
            {
                var host = string.IsNullOrWhiteSpace(user.Host) ? MisskeyAuthService.InstanceHost : user.Host;
                if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                {
                    host = MisskeyAuthService.InstanceHost;
                }

                await WebLauncher.LaunchAsync($"https://{host}/@{Uri.EscapeDataString(user.Username)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Failed to open fort.social profile - {ex.Message}");
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
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

        private static string FormatMemberSince(DateTime value)
        {
            try
            {
                var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter("month year");
                return formatter.Format(new DateTimeOffset(value));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Member-since formatting failed - {ex.Message}");
                return value.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static string FormatLastLogin(DateTime value)
        {
            try
            {
                var dateFormatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter("shortdate");
                var timeFormatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter("shorttime");

                DateTimeOffset offset = new DateTimeOffset(value);

                return $"{dateFormatter.Format(offset)} {timeFormatter.Format(offset)}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Date formatting failed - {ex.Message}");
                return value.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "?";
            }

            var parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                return (TextHelper.FirstTextElements(parts[0], 1) +
                        TextHelper.FirstTextElements(parts[1], 1)).ToUpperInvariant();
            }

            return TextHelper.FirstTextElements(parts[0], 1).ToUpperInvariant();
        }

        private bool UpdateAvatarUI(string avatarUrl)
        {
            if (_avatarApplied && string.Equals(avatarUrl, _lastAvatarUrl, StringComparison.Ordinal))
            {
                return false;
            }
            _lastAvatarUrl = avatarUrl;
            _avatarApplied = true;

            try
            {
                var avatarUri = WebLauncher.TryCreateWebUri(avatarUrl);
                if (avatarUri == null)
                {
                    ShowAvatarInitials();
                    return true;
                }

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = 80;
                bitmap.DecodePixelHeight = 80;
                bitmap.ImageOpened += AvatarBitmap_ImageOpened;
                bitmap.ImageFailed += AvatarBitmap_ImageFailed;
                bitmap.UriSource = avatarUri;
                ProfileImage.Source = bitmap;
                ProfileImage.Visibility = Visibility.Visible;
                ProfileInitials.Visibility = Visibility.Visible;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Avatar load failed - {ex.Message}");
                ShowAvatarInitials();
                return true;
            }
        }

        private void ShowAvatarInitials()
        {
            ProfileImage.Source = null;
            ProfileImage.Visibility = Visibility.Collapsed;
            ProfileInitials.Visibility = Visibility.Visible;
        }

        private void AvatarBitmap_ImageOpened(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, ProfileImage.Source)) return;

            ProfileInitials.Visibility = Visibility.Collapsed;
        }

        private void AvatarBitmap_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, ProfileImage.Source)) return;

            Debug.WriteLine($"ProfilePage: avatar image failed to load - {e.ErrorMessage}");
            ShowAvatarInitials();
            _avatarApplied = false;
        }

        private void UpdateBannerUI(string bannerUrl, string blurhash)
        {
            if (_bannerApplied
                && string.Equals(bannerUrl, _lastBannerUrl, StringComparison.Ordinal)
                && string.Equals(blurhash, _lastBannerBlurhash, StringComparison.Ordinal))
            {
                return;
            }
            _lastBannerUrl = bannerUrl;
            _lastBannerBlurhash = blurhash;
            _bannerApplied = true;

            try
            {
                var bannerUri = WebLauncher.TryCreateWebUri(bannerUrl);
                if (bannerUri == null)
                {
                    HideBanner();
                    return;
                }

                StopBannerFadeIn();

                var placeholder = CreateBlurhashBrush(blurhash);
                _bannerHasPlaceholder = placeholder != null;
                BannerPlaceholderPath.Fill = placeholder;

                _bannerRevealPending = placeholder == null
                                       && string.Equals(bannerUrl, _failedBannerUrl, StringComparison.Ordinal);

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = BannerDecodeWidth;
                bitmap.ImageOpened += BannerBitmap_ImageOpened;
                bitmap.ImageFailed += BannerBitmap_ImageFailed;
                bitmap.UriSource = bannerUri;
                _bannerBitmap = bitmap;

                BannerImagePath.Opacity = 0;
                BannerImagePath.Fill = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };

                VisualStateManager.GoToState(this, _bannerRevealPending ? "NoBannerState" : "BannerState", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: Banner load failed - {ex.Message}");
                HideBanner();
            }
        }

        private void HideBanner()
        {
            StopBannerFadeIn();
            _bannerBitmap = null;
            _bannerHasPlaceholder = false;
            _bannerRevealPending = false;
            BannerImagePath.Fill = null;
            BannerImagePath.Opacity = 0;
            BannerPlaceholderPath.Fill = null;
            VisualStateManager.GoToState(this, "NoBannerState", false);
        }

        private void BannerBitmap_ImageOpened(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, _bannerBitmap)) return;

            _failedBannerUrl = null;
            if (_bannerRevealPending)
            {
                _bannerRevealPending = false;
                VisualStateManager.GoToState(this, "BannerState", false);
            }

            PlayBannerFadeIn();
        }

        private void BannerBitmap_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, _bannerBitmap)) return;

            Debug.WriteLine($"ProfilePage: banner image failed to load - {e.ErrorMessage}");
            _bannerApplied = false;

            if (_bannerHasPlaceholder)
            {
                StopBannerFadeIn();
                _bannerBitmap = null;
                BannerImagePath.Fill = null;
                BannerImagePath.Opacity = 0;
            }
            else
            {
                _failedBannerUrl = _lastBannerUrl;
                HideBanner();
            }
        }

        private void PlayBannerFadeIn()
        {
            try
            {
                var storyboard = this.Resources["BannerFadeIn"] as Storyboard;
                if (storyboard == null)
                {
                    BannerImagePath.Opacity = 1;
                    return;
                }

                storyboard.Stop();
                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: banner fade-in failed - {ex.Message}");
                BannerImagePath.Opacity = 1;
            }
        }

        private void StopBannerFadeIn()
        {
            try
            {
                var storyboard = this.Resources["BannerFadeIn"] as Storyboard;
                storyboard?.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: banner fade-in could not be stopped - {ex.Message}");
            }
        }

        private static Brush CreateBlurhashBrush(string blurhash)
        {
            try
            {
                var pixels = Blurhash.DecodeToBgra(blurhash, BlurhashPixelWidth, BlurhashPixelHeight);
                if (pixels == null) return null;

                WriteableBitmap bitmap = new WriteableBitmap(BlurhashPixelWidth, BlurhashPixelHeight);
                using (var stream = bitmap.PixelBuffer.AsStream())
                {
                    stream.Write(pixels, 0, pixels.Length);
                }
                bitmap.Invalidate();

                return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: blurhash placeholder failed - {ex.Message}");
                return null;
            }
        }

        private void BannerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                var width = e.NewSize.Width;
                if (width <= 0) return;

                var height = Math.Round(width / BannerAspectRatio);
                if (double.IsNaN(BannerHost.Height) || Math.Abs(BannerHost.Height - height) >= 1)
                {
                    BannerHost.Height = height;
                    return;
                }

                UpdateBannerGeometry(width, e.NewSize.Height);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: banner layout failed - {ex.Message}");
            }
        }

        private void UpdateBannerGeometry(double width, double height)
        {
            Geometry hole = null;
            Geometry holeCopy = null;

            Point center;
            double radius;
            if (TryGetAvatarHole(width, height, out center, out radius))
            {
                hole = BuildHoleGeometry(height, center, radius);
                holeCopy = BuildHoleGeometry(height, center, radius);
            }

            BannerPlaceholderPath.Data = BuildBannerGeometry(width, height, hole);
            BannerImagePath.Data = BuildBannerGeometry(width, height, holeCopy);
        }

        private bool TryGetAvatarHole(double width, double height, out Point center, out double radius)
        {
            center = default(Point);
            radius = 0;

            try
            {
                if (AvatarGrid.ActualWidth <= 0 || AvatarGrid.ActualHeight <= 0) return false;

                radius = AvatarGrid.ActualWidth / 2 + BannerRingWidth;
                center = AvatarGrid.TransformToVisual(BannerHost)
                                   .TransformPoint(new Point(AvatarGrid.ActualWidth / 2, AvatarGrid.ActualHeight / 2));

                return center.X - radius >= 0
                       && center.X + radius <= width
                       && center.Y - radius >= 0
                       && center.Y - radius < height;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProfilePage: avatar position unavailable for the banner - {ex.Message}");
                return false;
            }
        }

        private static Geometry BuildBannerGeometry(double width, double height, Geometry hole)
        {
            GeometryGroup group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, width, height) });

            if (hole != null)
            {
                group.Children.Add(hole);
            }

            return group;
        }

        private static Geometry BuildHoleGeometry(double bottom, Point center, double radius)
        {
            if (center.Y + radius <= bottom)
            {
                return new EllipseGeometry { Center = center, RadiusX = radius, RadiusY = radius };
            }

            double offset = bottom - center.Y;
            double halfChord = Math.Sqrt(radius * radius - offset * offset);
            if (halfChord < 0.5) return null;

            PathFigure figure = new PathFigure
            {
                StartPoint = new Point(center.X - halfChord, bottom),
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(center.X + halfChord, bottom),
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = center.Y < bottom
            });

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
    }
}
