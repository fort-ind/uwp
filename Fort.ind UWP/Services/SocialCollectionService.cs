using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public static class SocialCollectionService
    {
        private const int NameLimit = 100;

        private const string AlreadyAddedCode = "ALREADY_ADDED";

        private const string TooManyListsCode = "TOO_MANY_USERLISTS";

        private const string TooManyUsersCode = "TOO_MANY_USERS";

        private const string BlockedCode = "BLOCKED";

        public static async Task<SocialApiResult<IReadOnlyList<SocialCollection>>> FetchAsync(
            SocialCollectionKind kind, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetCollectionsAsync(token, kind, cancellationToken);
        }

        public static async Task<SocialCollection> CreateListAsync(UIElement owner)
        {
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Lists)) return null;

            var name = await AskNameAsync(owner, "SocialListNewTitle", "SocialListNewConfirm", "");
            if (name == null) return null;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.CreateListAsync(token, name, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok)
            {
                AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Format("SocialListCreatedFormat", result.Value.Name), "FortIndLists");
                return result.Value;
            }

            await FailAsync(owner, result.Status, result.ErrorCode, LocalizedStrings.Get("SocialListCreateFailedTitle"));
            return null;
        }

        public static async Task<bool> RenameListAsync(UIElement owner, SocialCollection list)
        {
            if (list == null || list.Kind != SocialCollectionKind.List) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Lists)) return false;

            var name = await AskNameAsync(owner, "SocialListRenameTitle", "SocialListRenameConfirm", list.Name);
            if (name == null || string.Equals(name, list.Name, StringComparison.Ordinal)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.UpdateListAsync(token, list.Id, name, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok)
            {
                list.Rename(result.Value.Name, result.Value.IsPublic);
                return true;
            }

            await FailAsync(owner, result.Status, result.ErrorCode, LocalizedStrings.Format("SocialListRenameFailedFormat", list.Name));
            return false;
        }

        public static async Task<bool> DeleteListAsync(UIElement owner, SocialCollection list)
        {
            if (list == null || list.Kind != SocialCollectionKind.List) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Lists)) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Format("SocialListDeleteTitleFormat", list.Name),
                                                                 LocalizedStrings.Get("SocialListDeleteBody"),
                                                                 LocalizedStrings.Get("SocialListDeleteConfirm"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            if (!confirmed) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.DeleteListAsync(token, list.Id, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok || result.Status == SocialApiStatus.Refused)
            {
                AutomationHelper.AnnounceStatus(owner, LocalizedStrings.Format("SocialListDeletedFormat", list.Name), "FortIndLists");
                return true;
            }

            await FailAsync(owner, result.Status, result.ErrorCode, LocalizedStrings.Format("SocialListDeleteFailedFormat", list.Name));
            return false;
        }

        public static async Task ShowMembersAsync(UIElement owner, SocialCollection list)
        {
            if (list == null || list.Kind != SocialCollectionKind.List) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Lists)) return;

            var view = new SocialListMembers();
            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.Format("SocialListMembersTitleFormat", list.Name),
                Content = view,
                CloseButtonText = LocalizedStrings.Get("DialogDone")
            };

            try
            {
                view.Start(list);
                await DialogService.ShowAsync(owner, dialog);
            }
            finally
            {
                view.Release();
            }
        }

        public static async Task ShowAddToListAsync(UIElement owner, SocialUser user)
        {
            if (user == null || string.IsNullOrEmpty(user.Id)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Lists)) return;

            var name = SocialNoteItem.DisplayNameOf(user);
            var fetched = await FetchAsync(SocialCollectionKind.List, CancellationToken.None);
            if (fetched.Status != SocialApiStatus.Ok)
            {
                await FailAsync(owner, fetched.Status, fetched.ErrorCode, LocalizedStrings.Format("SocialListAddFailedTitleFormat", name));
                return;
            }

            if (fetched.Value.Count == 0)
            {
                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Format("SocialListAddTitleFormat", name),
                                                     LocalizedStrings.Get("SocialListAddNoLists"),
                                                     LocalizedStrings.Get("DialogOk"));
                return;
            }

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var panel = new StackPanel();
            foreach (var list in fetched.Value)
            {
                var box = new CheckBox
                {
                    Content = list.Name,
                    Tag = list,
                    IsChecked = list.UserIds.Contains(user.Id)
                };
                box.Click += (sender, args) => AddToListBoxClicked(sender as CheckBox, user, name, status);
                panel.Children.Add(box);
            }

            panel.Children.Add(status);

            await DialogService.ShowAsync(owner, new ContentDialog
            {
                Title = LocalizedStrings.Format("SocialListAddTitleFormat", name),
                Content = new ScrollViewer { Content = panel, MaxHeight = 360 },
                CloseButtonText = LocalizedStrings.Get("DialogDone")
            });
        }

        private static async void AddToListBoxClicked(CheckBox box, SocialUser user, string name, TextBlock status)
        {
            try
            {
                var list = box == null ? null : box.Tag as SocialCollection;
                if (list == null) return;

                var add = box.IsChecked.GetValueOrDefault();
                box.IsEnabled = false;
                status.Visibility = Visibility.Collapsed;

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.ChangeListMemberAsync(token, list.Id, user.Id, add, CancellationToken.None);
                var done = result.Status == SocialApiStatus.Ok
                           || (add && string.Equals(result.ErrorCode, AlreadyAddedCode, StringComparison.Ordinal));
                if (done)
                {
                    AutomationHelper.AnnounceStatus(box,
                                                    LocalizedStrings.Format(add ? "SocialListMemberAddedFormat" : "SocialListMemberRemovedFormat",
                                                                            name, list.Name),
                                                    "FortIndLists");
                }
                else
                {
                    box.IsChecked = !add;
                    status.Text = add && string.Equals(result.ErrorCode, TooManyUsersCode, StringComparison.Ordinal)
                                  ? LocalizedStrings.Get("SocialListFull")
                                  : add && string.Equals(result.ErrorCode, BlockedCode, StringComparison.Ordinal)
                                    ? LocalizedStrings.Format("SocialListMemberBlockedFormat", name)
                                    : result.Status == SocialApiStatus.PermissionDenied
                                      ? LocalizedStrings.Get("SocialListPermissionStatus")
                                      : LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric");
                    status.Visibility = Visibility.Visible;
                }

                box.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialCollectionService: could not change a list's member", ex);
            }
        }

        private static async Task<string> AskNameAsync(UIElement owner, string titleKey, string confirmKey, string current)
        {
            var input = new TextBox
            {
                Text = current ?? "",
                MaxLength = NameLimit,
                SelectionStart = 0,
                SelectionLength = (current ?? "").Length
            };
            input.PlaceholderText = LocalizedStrings.Get("SocialListNamePlaceholder");
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(input, LocalizedStrings.Get("SocialListNamePlaceholder"));

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.Get(titleKey),
                Content = input,
                PrimaryButtonText = LocalizedStrings.Get(confirmKey),
                CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(current)
            };
            input.TextChanged += (sender, args) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);

            var result = await DialogService.ShowAsync(owner, dialog);
            return result == ContentDialogResult.Primary ? input.Text.Trim() : null;
        }

        private static async Task FailAsync(UIElement owner, SocialApiStatus status, string errorCode, string title)
        {
            if (status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Lists);
                return;
            }

            var key = string.Equals(errorCode, TooManyListsCode, StringComparison.Ordinal)
                      ? "SocialListTooMany"
                      : SocialApiService.ConnectionMessageKey(status) ?? "SocialActionErrorGeneric";
            await DialogService.ShowMessageAsync(owner, title, LocalizedStrings.Get(key), LocalizedStrings.Get("DialogOk"));
        }
    }
}
