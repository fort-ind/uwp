using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private bool _chatHandlersAttached;

        private void AttachChatHandlers()
        {
            if (!_chatHandlersAttached)
            {
                LabsService.ChatChanged += OnChatLabChanged;
                SocialChatService.UnreadChanged += OnChatUnreadChanged;
                _chatHandlersAttached = true;
            }

            UpdateChatNavItem();
        }

        private void DetachChatHandlers()
        {
            if (!_chatHandlersAttached) return;

            LabsService.ChatChanged -= OnChatLabChanged;
            SocialChatService.UnreadChanged -= OnChatUnreadChanged;
            _chatHandlersAttached = false;
        }

        private async void OnChatLabChanged(object sender, EventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        UpdateChatNavItem();
                        if (!LabsService.ChatEnabled) LeaveChat();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("MainPage: could not apply the chat lab", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: chat lab handler failed", ex);
            }
        }

        private async void OnChatUnreadChanged(object sender, EventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        UpdateChatNavItem();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("MainPage: could not update the chat nav item", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: chat unread handler failed", ex);
            }
        }

        private void UpdateChatNavItem()
        {
            var enabled = LabsService.ChatEnabled;
            ChatNavItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

            var count = enabled ? SocialChatService.UnreadCount : 0;
            ChatNavItem.Content = count > 0
                                  ? LocalizedStrings.Format("MainChatUnreadFormat", count)
                                  : LocalizedStrings.Get("MainChat/Content");
            AutomationProperties.SetName(ChatNavItem, count > 0
                                                      ? LocalizedStrings.Format("MainChatUnreadAutomationFormat", count)
                                                      : LocalizedStrings.Get("MainChat/Content"));
        }

        private void LeaveChat()
        {
            var ignored = WindowManagerService.CloseWhereAsync(key => string.Equals(key, AppConstants.NavigationChat, StringComparison.Ordinal)
                                                                      || key.StartsWith(AppConstants.WindowKeyChatPrefix, StringComparison.Ordinal));

            if (IsChatPage(ContentFrame.CurrentSourcePageType))
            {
                SelectNavItemForTag(AppConstants.NavigationLatestNews);
                ShowContent(AppConstants.NavigationLatestNews);
            }

            var stack = ContentFrame.BackStack;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (IsChatPage(stack[i].SourcePageType)) stack.RemoveAt(i);
            }

            TitleBarBackButton.IsEnabled = ContentFrame.CanGoBack;
        }

        internal void ShowChat(string userId)
        {
            try
            {
                if (!LabsService.ChatEnabled) return;
                if (HoldForUnsavedChanges(() => ShowChat(userId))) return;

                var conversation = ContentFrame.Content as SocialChatConversationPage;
                if (conversation != null && conversation.Shows(userId)) return;

                if (!(ContentFrame.Content is SocialChatPage)) NavigateToTag(AppConstants.NavigationChat);

                var page = ContentFrame.Content as SocialChatPage;
                if (page != null && !string.IsNullOrEmpty(userId)) page.OpenConversation(userId, null);
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not open the chat", ex);
            }
        }

        private static bool IsChatPage(Type pageType)
        {
            return pageType == typeof(SocialChatPage) || pageType == typeof(SocialChatConversationPage);
        }
    }
}
