using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace Fort.ind_UWP
{
    public static class SocialWindows
    {
        public static Task<bool> ShowUserAsync(SocialUser user)
        {
            if (user == null || string.IsNullOrEmpty(user.Id)) return Task.FromResult(false);
            if (SocialContentService.CurrentAccountId() == null) return LaunchSignedOutAsync(SocialLinks.UserUrl(user));

            return ShowUserCoreAsync(user.Id, user.Handle, user, null);
        }

        public static async Task<bool> ShowMentionAsync(MfmSegment mention)
        {
            if (mention == null) return false;
            if (SocialContentService.CurrentAccountId() == null)
            {
                return await LaunchSignedOutAsync(SocialLinks.UserUrl(mention.Username, mention.Host));
            }

            if (!string.IsNullOrEmpty(mention.UserId))
            {
                return await ShowUserCoreAsync(mention.UserId, SocialLinks.FormatHandle(mention.Username, mention.Host), null, null);
            }

            return await ShowUserByHandleAsync(mention.Username, mention.Host);
        }

        public static async Task<bool> ShowUserByHandleAsync(string username, string host)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            var result = await SocialContentService.FetchUserByHandleAsync(username, host, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok)
            {
                var user = result.Value.User;
                return await ShowUserCoreAsync(user.Id, user.Handle, user, result.Value);
            }

            await WebLauncher.LaunchAsync(SocialLinks.UserUrl(username, host));
            return false;
        }

        private static async Task<bool> LaunchSignedOutAsync(string url)
        {
            await WebLauncher.LaunchAsync(url);
            return false;
        }

        private static Task<bool> ShowUserCoreAsync(string userId, string handle, SocialUser user, SocialUserDetail detail)
        {
            var account = SocialContentService.CurrentAccountId();
            if (string.IsNullOrEmpty(userId) || account == null) return Task.FromResult(false);

            if (string.Equals(userId, account, StringComparison.Ordinal)) return ShowOwnProfileAsync();

            var title = user != null ? SocialNoteItem.DisplayNameOf(user) : handle;
            var args = new SocialUserWindowArgs(account, userId, handle, user, detail);

            var request = new WindowRequest(AppConstants.WindowKeyUserPrefix + userId,
                                            string.IsNullOrWhiteSpace(title) ? LocalizedStrings.Get("WindowTitleProfile") : title,
                                            null,
                                            typeof(SocialUserPage),
                                            args,
                                            new Size(AppConstants.SocialUserWindowWidth, AppConstants.SocialUserWindowHeight),
                                            true);

            return WindowManagerService.ShowKeyedAsync(request);
        }

        public static async Task<bool> ShowComposeAsync(Windows.UI.Xaml.UIElement owner, SocialComposeMode mode, SocialNote note)
        {
            var account = SocialContentService.CurrentAccountId();
            if (account == null) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteNotes, SocialSignInPrompt.Post)) return false;

            var request = new WindowRequest(AppConstants.WindowKeyCompose,
                                            SocialComposePage.TitleFor(mode),
                                            null,
                                            typeof(SocialComposePage),
                                            new SocialComposeArgs(account, mode, note),
                                            SocialComposePage.RememberedSize(),
                                            true);

            return await WindowManagerService.ShowKeyedAsync(request);
        }

        private static async Task<bool> ShowOwnProfileAsync()
        {
            if (WindowManagerService.IsSecondaryView)
            {
                await WindowManagerService.ShowInMainWindowAsync(AppConstants.NavigationProfile);
                return true;
            }

            var shell = MainPage.Current;
            if (shell == null) return false;

            shell.NavigateToTag(AppConstants.NavigationProfile);
            return true;
        }
    }
}
