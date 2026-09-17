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

            if (show)
            {
                AutomationHelper.AnnounceLiveRegion(WaitingText);
            }
        }
    }
}
