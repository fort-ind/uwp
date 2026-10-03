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
                SocialPermissions.Changed += OnNeedsSignInAgainChanged;
                ProfileService.AuthStateChanged += OnSignInAgainAuthChanged;
                _signInAgainHandlerAttached = true;
            }

            UpdateSignInAgainInfoBar();
        }

        private void DetachSignInAgainHandler()
        {
            if (!_signInAgainHandlerAttached) return;

            SocialNotificationService.NeedsSignInAgainChanged -= OnNeedsSignInAgainChanged;
            SocialPermissions.Changed -= OnNeedsSignInAgainChanged;
            ProfileService.AuthStateChanged -= OnSignInAgainAuthChanged;
            _signInAgainHandlerAttached = false;
        }

        private void OnSignInAgainAuthChanged(object sender, bool isSignedIn)
        {
            OnNeedsSignInAgainChanged(sender, EventArgs.Empty);
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
            var signedIn = ProfileService.CurrentUser != null;
            var needed = signedIn
                         && SocialNotificationService.NeedsSignInAgain
                         && SocialNotificationService.Enabled
                         && !SocialNotificationService.SignInAgainDismissed;

            SocialPermissionInfoBar.IsOpen = needed;

            SocialWritePermissionInfoBar.IsOpen = signedIn
                                                  && !needed
                                                  && !SocialPermissions.HasAllPosting
                                                  && !IsWriteSignInDismissed();
        }

        private static bool IsWriteSignInDismissed()
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                var stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialWriteSignInDismissed] as string;
                return account != null && string.Equals(stored, account, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not read the permission banner dismissal - {ex.Message}");
                return false;
            }
        }

        private void SocialPermissionInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            SocialNotificationService.DismissSignInAgain();
        }

        private void SocialWritePermissionInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                if (account != null)
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialWriteSignInDismissed] = account;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not remember the permission banner dismissal - {ex.Message}");
            }
        }

        private void SocialPermissionSignInButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowSignIn();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not open sign-in from the banner - {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal void ShowSignIn()
        {
            NavigateToTag(AppConstants.NavigationProfile);

            if (ContentFrame.Content is ProfilePage)
            {
                ContentFrame.Navigate(typeof(LoginPage));
            }
        }
    }
}
