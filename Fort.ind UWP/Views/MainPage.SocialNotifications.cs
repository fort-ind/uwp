using System;
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
                        AppLog.Error("MainPage: sign-in-again banner update failed", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: sign-in-again handler failed", ex);
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

            SocialAccountPermissionInfoBar.IsOpen = signedIn
                                                    && !needed
                                                    && !SocialWritePermissionInfoBar.IsOpen
                                                    && !SocialPermissions.HasAccountLists
                                                    && !IsSignInBannerDismissed(AppConstants.SettingSocialAccountSignInDismissed);
        }

        private static bool IsWriteSignInDismissed()
        {
            return IsSignInBannerDismissed(AppConstants.SettingSocialWriteSignInDismissed);
        }

        private static bool IsSignInBannerDismissed(string key)
        {
            var account = SocialContentService.CurrentAccountId();
            return account != null && string.Equals(AppSettings.ReadString(key, null), account, StringComparison.Ordinal);
        }

        private void SocialPermissionInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            SocialNotificationService.DismissSignInAgain();
        }

        private void SocialWritePermissionInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            RememberSignInBannerDismissed(AppConstants.SettingSocialWriteSignInDismissed);
        }

        private void SocialAccountPermissionInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            RememberSignInBannerDismissed(AppConstants.SettingSocialAccountSignInDismissed);
        }

        private static void RememberSignInBannerDismissed(string key)
        {
            var account = SocialContentService.CurrentAccountId();
            if (account != null) AppSettings.Write(key, account);
        }

        private void SocialPermissionSignInButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowSignIn();
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not open sign-in from the banner", ex);
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
