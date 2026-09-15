using System;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class LoginPage : Page
    {
        public LoginPage()
        {
            this.InitializeComponent();
        }

        private void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Guarded because an exception escaping a Loaded handler is unhandled and takes the
            // app down - and this one reads LocalSettings, which is exactly the kind of thing
            // LoadAppearanceSettings already wraps for the same reason.
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (!settings.Values.ContainsKey(AppConstants.SettingHasSeenSkipSignInTip))
                {
                    settings.Values[AppConstants.SettingHasSeenSkipSignInTip] = true;
                    SkipHintTip.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoginPage: skip-hint tip failed - {ex.Message}");
            }
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ShowLoading(true);

            try
            {
                var result = await ProfileService.LoginWithMisskeyAsync();

                // The user can leave this page while the browser is open - clicking Games, say.
                // The sign-in still completes (AuthStateChanged updates the shell), but GoBack on a
                // Frame that has since moved on would swap the content out from under the other
                // nav item. Only navigate if this page is still what the Frame is showing.
                if (Frame == null || Frame.Content != this) return;

                if (result.Success)
                {
                    GoBackToProfile();
                }
                else
                {
                    ShowError(result.Message);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SignInButton_Click error: {ex}");
                ShowError(LocalizedStrings.Get("LoginErrorGeneric"));
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void CancelSignInButton_Click(object sender, RoutedEventArgs e)
        {
            MisskeyAuthService.CancelPendingSignIn();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            GoBackToProfile();
        }

        private void GoBackToProfile()
        {
            if (Frame == null) return;

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(ProfilePage));
            }
        }

        /// <summary>
        /// Shows the sign-in error and announces it.
        /// </summary>
        /// <remarks>
        /// The announcement is not optional decoration: the error appears with no focus change and
        /// no new focusable element, so without it a screen-reader user is told nothing at all and
        /// the page simply looks like it did nothing. ErrorText's LiveSetting="Assertive" only
        /// declares the politeness level - see AutomationHelper.AnnounceLiveRegion. Raised after
        /// the text is set, which is the order the docs require.
        /// </remarks>
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;

            AutomationHelper.AnnounceLiveRegion(ErrorText);
        }

        private void ShowLoading(bool show)
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            SignInButton.IsEnabled = !show;
            SkipButton.IsEnabled = !show;

            // Only on the way in. The overlay going away is followed either by GoBackToProfile or
            // by ShowError, both of which say something of their own.
            if (show)
            {
                AutomationHelper.AnnounceLiveRegion(WaitingText);
            }
        }
    }
}
