using System;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private bool _signInAgainHandlerAttached = false;

        private void AttachSignInAgainHandler()
        {
            if (!_signInAgainHandlerAttached)
            {
                SocialNotificationService.NeedsSignInAgainChanged += OnNeedsSignInAgainChanged;
                _signInAgainHandlerAttached = true;
            }

            UpdateSignInAgainInfoBar();
        }

        private void DetachSignInAgainHandler()
        {
            if (!_signInAgainHandlerAttached) return;

            SocialNotificationService.NeedsSignInAgainChanged -= OnNeedsSignInAgainChanged;
            _signInAgainHandlerAttached = false;
        }

        private async void OnNeedsSignInAgainChanged(object sender, EventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        UpdateSignInAgainInfoBar();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"MainPage: sign-in-again banner update failed - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: sign-in-again handler failed - {ex.Message}");
            }
        }

        private void UpdateSignInAgainInfoBar()
        {
            var needed = SocialNotificationService.NeedsSignInAgain
                         && SocialNotificationService.Enabled
                         && ProfileService.CurrentUser != null
                         && !SocialNotificationService.SignInAgainDismissed;

            SocialPermissionInfoBar.IsOpen = needed;
        }

        private void SocialPermissionInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            SocialNotificationService.DismissSignInAgain();
        }

        private void SocialPermissionSignInButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NavigateToTag(AppConstants.NavigationProfile);

                if (ContentFrame.Content is ProfilePage)
                {
                    ContentFrame.Navigate(typeof(LoginPage));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not open sign-in from the banner - {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
