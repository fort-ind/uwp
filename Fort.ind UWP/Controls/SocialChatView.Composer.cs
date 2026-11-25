using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatView : UserControl
    {
        private const int RingThreshold = 1500;

        private bool _settingText;

        private bool CanWrite
        {
            get
            {
                return _key != null
                       && SocialPolicies.Current.AllowsChatWriting
                       && !IsRemote
                       && !(ScopeRefuses && _reachedOldest && !_theyMessaged);
            }
        }

        private bool IsRemote
        {
            get
            {
                if (IsRoom) return false;

                var user = _detail != null ? _detail.User : _other;
                return user != null && !string.IsNullOrWhiteSpace(user.Host);
            }
        }

        private bool ScopeRefuses
        {
            get { return !IsRoom && _detail != null && !SocialChatService.ScopeAllows(_detail, _theyMessaged); }
        }

        private void UpdateComposer()
        {
            string notice = null;
            if (_key != null)
            {
                if (!SocialPolicies.Current.AllowsChatWriting)
                {
                    notice = LocalizedStrings.Get("SocialChatReadOnlyNotice");
                }
                else if (IsRemote)
                {
                    notice = LocalizedStrings.Get("SocialChatRemoteNotice");
                }
                else if (ScopeRefuses)
                {
                    notice = SocialChatService.ScopeRefusalText(_detail);
                }
            }

            NoticeText.Text = notice ?? "";
            NoticeText.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;

            var canWrite = CanWrite;
            ChatInput.IsEnabled = canWrite;
            EmojiButton.IsEnabled = canWrite;
            UpdateSendButton();
        }

        private void UpdateSendButton()
        {
            var text = ChatInput.Text ?? "";
            var length = text.Length;
            SendButton.IsEnabled = CanWrite
                                   && !AttachmentBlocksSending
                                   && (!string.IsNullOrWhiteSpace(text) || HasReadyAttachment)
                                   && length <= AppConstants.SocialChatMessageMaxLength;
            AttachButton.IsEnabled = CanWrite;

            var showRing = length >= RingThreshold;
            Ring.Visibility = showRing ? Visibility.Visible : Visibility.Collapsed;
            if (showRing) Ring.Update(length, AppConstants.SocialChatMessageMaxLength);
        }

        private void SetMessageText(string text)
        {
            _settingText = true;
            try
            {
                ChatInput.Text = text ?? "";
                ChatInput.SelectionStart = ChatInput.Text.Length;
            }
            finally
            {
                _settingText = false;
            }

            UpdateSendButton();
        }

        private void SaveDraft()
        {
            if (_key == null) return;

            SocialChatService.SaveDraft(_key, ChatInput.Text);
        }

        private void ChatInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendButton();
            if (!_settingText) SaveDraft();
        }

        private void ChatInput_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;

            var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);
            if ((shift & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down) return;

            e.Handled = true;
            SendInBackground();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendInBackground();
            ChatInput.Focus(FocusState.Programmatic);
        }

        private void EmojiFlyout_Opening(object sender, object e)
        {
            try
            {
                var picker = SocialEmojiPicker.ForCurrentView();
                picker.AttachTo(EmojiFlyout);
                picker.Configure(SocialEmojiPickerMode.Insert, null, null, InsertEmoji, null, null);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not open the emoji picker", ex);
            }
        }

        private void InsertEmoji(string key)
        {
            var name = SocialReactions.CustomName(key);
            var text = name != null ? ":" + name + ":" : key;

            var current = ChatInput.Text ?? "";
            var start = Math.Min(ChatInput.SelectionStart, current.Length);
            var length = Math.Min(ChatInput.SelectionLength, current.Length - start);

            var before = current.Substring(0, start);
            var after = current.Substring(start + length);
            if (name != null && before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1])) text = " " + text;
            if (name != null && (after.Length == 0 || !char.IsWhiteSpace(after[0]))) text = text + " ";

            ChatInput.Text = before + text + after;
            ChatInput.SelectionStart = start + text.Length;
            ChatInput.Focus(FocusState.Programmatic);
        }

        private async void SendInBackground()
        {
            try
            {
                await SendAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: sending failed", ex);
            }
        }

        private async Task SendAsync()
        {
            var text = (ChatInput.Text ?? "").Trim();
            if (!CanWrite || AttachmentBlocksSending) return;
            if (string.IsNullOrEmpty(text) && !HasReadyAttachment) return;
            if (text.Length > AppConstants.SocialChatMessageMaxLength) return;

            if (!await SocialPermissions.EnsureAsync(this, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return;

            var file = HasReadyAttachment ? _attachment.File : null;
            var pending = SocialChatMessageItem.CreatePending(text, file, _emojis ?? SocialContentService.CachedEmojiMap,
                                                                 _account, IsRoom);
            Track(pending);
            pending.RetryRequested += Pending_RetryRequested;
            _items.Add(pending);
            Relayout();
            UpdateEmptyState();

            SetMessageText("");
            SaveDraft();
            ClearAttachment(false);
            ScrollToBottomSoon();

            await SendPendingAsync(pending);
        }

        private async void Pending_RetryRequested(object sender, EventArgs e)
        {
            try
            {
                var pending = sender as SocialChatMessageItem;
                if (pending == null || !_items.Contains(pending)) return;
                if (!await SocialPermissions.EnsureAsync(this, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return;

                pending.MarkSending();
                await SendPendingAsync(pending);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: retrying the message failed", ex);
            }
        }

        private async Task SendPendingAsync(SocialChatMessageItem pending)
        {
            var key = _key;
            var firstFromMe = !_items.Any(item => item.IsMine && item.State == SocialChatSendState.Sent);

            var fileId = pending.PendingFile == null ? null : pending.PendingFile.Id;
            var result = await SocialChatService.SendAsync(key, pending.PendingText, fileId, CancellationToken.None);
            if (!string.Equals(key, _key, StringComparison.Ordinal) || !_items.Contains(pending)) return;

            if (result.Status == SocialApiStatus.Ok)
            {
                pending.RetryRequested -= Pending_RetryRequested;

                var existing = _items.FirstOrDefault(item => !ReferenceEquals(item, pending)
                                                             && string.Equals(item.Id, result.Value.Id, StringComparison.Ordinal));
                if (existing != null)
                {
                    _items.Remove(pending);
                }
                else
                {
                    pending.MarkSent(result.Value);
                }

                Relayout();
                if (firstFromMe && !SocialChatKeys.IsRoom(key) && _watch != null) _watch.Rejoin();
                AutomationHelper.AnnounceStatus(this, LocalizedStrings.Get("SocialChatSentAnnouncement"), "SocialChatSent");
                return;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(this, SocialPermissions.WriteChat, SocialSignInPrompt.Chat);
            }

            var reason = SocialChatService.SendFailureText(result.Status, result.ErrorCode);
            pending.MarkFailed(reason);
            AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialChatNotSentFormat", reason), "SocialChatNotSent");
        }
    }
}
