using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public enum SocialSignInPrompt
    {
        Follow,
        React,
        Renote,
        Vote,
        Favorite,
        Pin,
        Mute,
        Delete,
        Report,
        Post,
        Profile,
        MuteUser,
        Block
    }

    public static class SocialPermissions
    {
        public const string ReadAccount = "read:account";

        public const string ReadNotifications = "read:notifications";

        public const string WriteFollowing = "write:following";

        public const string WriteNotes = "write:notes";

        public const string WriteReactions = "write:reactions";

        public const string WriteVotes = "write:votes";

        public const string ReadFavorites = "read:favorites";

        public const string WriteFavorites = "write:favorites";

        public const string WriteDrive = "write:drive";

        public const string WriteAccount = "write:account";

        public const string WriteReportAbuse = "write:report-abuse";

        public const string WriteMutes = "write:mutes";

        public const string WriteBlocks = "write:blocks";

        public const string Requested = ReadAccount + "," + ReadNotifications + "," + WriteFollowing + ","
                                        + WriteNotes + "," + WriteReactions + "," + WriteVotes + ","
                                        + ReadFavorites + "," + WriteFavorites + "," + WriteDrive + ","
                                        + WriteAccount + "," + WriteReportAbuse + "," + WriteMutes + "," + WriteBlocks;

        private static readonly string[] s_postingPermissions =
        {
            WriteNotes, WriteReactions, WriteVotes, ReadFavorites, WriteFavorites, WriteDrive, WriteAccount, WriteReportAbuse
        };

        public static event EventHandler Changed;

        public static bool Has(string permission)
        {
            return MisskeyAuthService.HasGrantedPermission(permission);
        }

        public static bool HasAllPosting
        {
            get
            {
                foreach (var permission in s_postingPermissions)
                {
                    if (!Has(permission)) return false;
                }

                return true;
            }
        }

        public static void Forget(string permission)
        {
            MisskeyAuthService.ForgetGrantedPermission(permission);

            try
            {
                Changed?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPermissions: a permission change handler failed", ex);
            }
        }

        public static async Task<bool> EnsureAsync(UIElement owner, string permission, SocialSignInPrompt prompt)
        {
            if (Has(permission)) return true;

            await OfferSignInAsync(owner, prompt);
            return false;
        }

        public static async Task HandleDeniedAsync(UIElement owner, string permission, SocialSignInPrompt prompt)
        {
            Forget(permission);
            await OfferSignInAsync(owner, prompt);
        }

        public static async Task OfferSignInAsync(UIElement owner, SocialSignInPrompt prompt)
        {
            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Get("SocialSignInPrompt" + prompt + "Title"),
                                                                 LocalizedStrings.Get("SocialSignInPromptBody"),
                                                                 LocalizedStrings.Get("SocialSignInPromptConfirm"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Primary);
            if (!confirmed) return;

            await WindowManagerService.ShowSignInInMainWindowAsync();
        }
    }
}
