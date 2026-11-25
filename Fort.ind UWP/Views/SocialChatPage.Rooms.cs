using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatPage
    {
        private bool _showingRooms;

        private bool _selectingTab;

        private bool _openInvitationsWhenLoaded;

        public static bool LastShowedRooms { get; set; }

        private bool ShowingRooms
        {
            get { return _showingRooms; }
        }

        private void InitializeTabs()
        {
            _showingRooms = LastShowedRooms;
            ConversationList.ItemsSource = CurrentList;

            _selectingTab = true;
            try
            {
                PeopleTab.IsChecked = !_showingRooms;
                RoomsTab.IsChecked = _showingRooms;
            }
            finally
            {
                _selectingTab = false;
            }

            PaintTabs();
        }

        public void ShowInvitations()
        {
            try
            {
                EnsureAccount();
                SelectTab(true);

                if (SocialChatService.Invitations.Count == 0)
                {
                    _openInvitationsWhenLoaded = true;
                    return;
                }

                OpenInvitationsSoon();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not show the invitations", ex);
            }
        }

        private void OpenPendingInvitations()
        {
            if (!_openInvitationsWhenLoaded) return;

            _openInvitationsWhenLoaded = false;
            if (SocialChatService.Invitations.Count > 0) OpenInvitationsSoon();
        }

        private void OpenInvitationsSoon()
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                try
                {
                    UpdateInvitations();
                    if (InvitationsButton.IsShown()) InvitationsFlyout.ShowAt(InvitationsButton);
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialChatPage: could not open the invitations", ex);
                }
            });
        }

        private void SelectTab(bool rooms)
        {
            if (rooms == _showingRooms) return;

            _selectingTab = true;
            try
            {
                if (rooms) RoomsTab.IsChecked = true;
                else PeopleTab.IsChecked = true;
            }
            finally
            {
                _selectingTab = false;
            }

            ApplyTab(rooms);
        }

        private void ChatTab_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_selectingTab) ApplyTab(ReferenceEquals(sender, RoomsTab));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not switch tabs", ex);
            }
        }

        private void ApplyTab(bool rooms)
        {
            if (rooms == _showingRooms) return;

            _showingRooms = rooms;
            LastShowedRooms = rooms;
            ConversationList.ItemsSource = CurrentList;
            ConversationList.SelectedItem = _selected != null && _twoPanes ? Find(_selected.Key) : null;
            UpdateInvitations();
            UpdateListState();
        }

        private void PaintTabs()
        {
            PaintTab(PeopleTab, PeopleTabText, "SocialChatPeopleTab", SocialChatService.UnreadPeopleCount);
            PaintTab(RoomsTab, RoomsTabText, "SocialChatRoomsTab", SocialChatService.UnreadRoomsCount);
        }

        private static void PaintTab(Control tab, TextBlock label, string key, int count)
        {
            var name = LocalizedStrings.Get(key);
            label.Text = count > 0 ? LocalizedStrings.Format("SocialChatTabCountFormat", name, count) : name;
            AutomationProperties.SetName(tab, count > 0 ? LocalizedStrings.Format("SocialChatTabUnreadAutomationFormat", name, count) : name);
        }

        private void UpdateInvitations()
        {
            var invitations = SocialChatService.Invitations;
            InvitationList.ItemsSource = invitations;

            var shown = _showingRooms && invitations.Count > 0;
            InvitationsButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            InvitationsText.Text = LocalizedStrings.Format("SocialChatInvitationsFormat", invitations.Count);
            AutomationProperties.SetName(InvitationsButton, LocalizedStrings.Format("SocialChatInvitationsAutomationFormat", invitations.Count));

            if (invitations.Count == 0) InvitationsFlyout.Hide();
        }

        private void ApplyRooms(IReadOnlyList<SocialChatRoom> rooms, IReadOnlyList<SocialChatMessage> history)
        {
            var newest = history.Where(message => message.IsRoomMessage)
                                .GroupBy(message => message.ToRoomId, StringComparer.Ordinal)
                                .ToDictionary(group => group.Key, group => group.OrderByDescending(message => message.CreatedAt).First(),
                                              StringComparer.Ordinal);

            var rows = rooms.Select(room =>
                            {
                                SocialChatMessage last;
                                newest.TryGetValue(room.Id, out last);
                                return SocialChatConversation.ForRoom(_account, room, last, SocialChatService.IsUnread(SocialChatKeys.ForRoom(room.Id)));
                            })
                            .Where(row => row != null)
                            .OrderByDescending(row => row.SortTime)
                            .ToList();

            Replace(_rooms, rows);
        }

        private void ApplyRoom(string key, SocialChatRoom room)
        {
            var row = Find(key);
            if (room == null)
            {
                if (row != null) _rooms.Remove(row);
                if (_selected != null && string.Equals(_selected.Key, key, StringComparison.Ordinal))
                {
                    _selected = null;
                    ShowPicker(true);
                }

                UpdateListState();
                return;
            }

            if (row != null)
            {
                row.SetRoom(room);
                if (_selected != null && string.Equals(_selected.Key, key, StringComparison.Ordinal))
                {
                    _selected = SocialChatArgs.ForRoom(_selected.Account, room.Id, room);
                }
                return;
            }

            row = SocialChatConversation.ForRoom(_account, room, null, SocialChatService.IsUnread(key));
            if (row == null) return;

            _rooms.Insert(0, row);
            UpdateListState();
        }

        private async void JoinButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            try
            {
                var invitation = button == null ? null : button.Tag as SocialChatInvitation;
                if (invitation == null) return;

                button.IsEnabled = false;
                if (!await SocialChatService.JoinAsync(this, invitation)) return;

                InvitationsFlyout.Hide();
                OpenConversation(SocialChatArgs.ForRoom(_account, invitation.RoomId,
                                                        invitation.Room ?? SocialChatService.RoomFor(invitation.RoomId)));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not join the room", ex);
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        private async void IgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            try
            {
                var invitation = button == null ? null : button.Tag as SocialChatInvitation;
                if (invitation == null) return;

                button.IsEnabled = false;
                await SocialChatService.IgnoreAsync(this, invitation);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not ignore the invitation", ex);
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        private async void NewRoomLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NewChatFlyout.Hide();
                if (!await SocialPermissions.EnsureAsync(this, SocialPermissions.WriteChat, SocialSignInPrompt.Chat)) return;

                var nameBox = new TextBox
                {
                    Header = LocalizedStrings.Get("SocialChatNewRoomNameHeader"),
                    MaxLength = AppConstants.SocialChatRoomNameMaxLength
                };

                var descriptionBox = new TextBox
                {
                    Header = LocalizedStrings.Get("SocialChatNewRoomDescriptionHeader"),
                    Margin = new Thickness(0, 12, 0, 0),
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLength = AppConstants.SocialChatRoomDescriptionMaxLength,
                    MinHeight = 80,
                    MaxHeight = 160
                };
                ScrollViewer.SetVerticalScrollBarVisibility(descriptionBox, ScrollBarVisibility.Auto);

                var invitees = new SocialRecipientBox
                {
                    LocalOnly = true,
                    Limit = AppConstants.SocialChatRoomMemberLimit,
                    Margin = new Thickness(0, 12, 0, 0)
                };
                invitees.HeaderText = LocalizedStrings.Get("SocialChatNewRoomInviteHeader");

                var panel = new StackPanel { MinWidth = 320 };
                panel.Children.Add(nameBox);
                panel.Children.Add(descriptionBox);
                panel.Children.Add(invitees);

                var dialog = new ContentDialog
                {
                    Title = LocalizedStrings.Get("SocialChatNewRoomTitle"),
                    PrimaryButtonText = LocalizedStrings.Get("SocialChatNewRoomCreate"),
                    CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    IsPrimaryButtonEnabled = false,
                    Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
                };
                nameBox.TextChanged += (box, args) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);

                var answer = await DialogService.ShowAsync(this, dialog);
                if (answer != ContentDialogResult.Primary) return;

                var room = await SocialChatService.CreateRoomAsync(this, nameBox.Text.Trim(), (descriptionBox.Text ?? "").Trim(), invitees.Recipients);
                if (room != null) OpenConversation(SocialChatArgs.ForRoom(_account, room.Id, room));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatPage: could not create the room", ex);
            }
        }
    }
}
