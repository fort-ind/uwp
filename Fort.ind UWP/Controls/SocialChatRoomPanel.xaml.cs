using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatRoomPanel : UserControl
    {
        private const int SuggestionLimit = 8;

        private readonly ObservableCollection<SocialChatMemberItem> _members = new ObservableCollection<SocialChatMemberItem>();

        private readonly Debouncer _searchDebounce = new Debouncer();

        private SocialChatRoom _room;

        private string _account;

        private bool _editing;

        private bool _busy;

        private bool _settingMute;

        private bool _membersKnown;

        private int _version;

        public SocialChatRoomPanel()
        {
            this.InitializeComponent();

            MemberList.ItemsSource = _members;
        }

        public event EventHandler CloseRequested;

        public event EventHandler<int> MembersCounted;

        public SocialChatRoom Room
        {
            get { return _room; }
        }

        public int MemberCount
        {
            get { return _membersKnown ? _members.Count : 0; }
        }

        public void Show(SocialChatRoom room, string account)
        {
            if (room == null) return;

            var sameRoom = _room != null && string.Equals(_room.Id, room.Id, StringComparison.Ordinal);
            _room = room;
            _account = account;

            if (!sameRoom)
            {
                _version++;
                _members.Clear();
                _membersKnown = false;
                SetEditing(false);
                ClearInvite();
            }

            Paint();
            if (!sameRoom) LoadMembersInBackground();
        }

        public void Update(SocialChatRoom room)
        {
            if (room == null || _room == null || !string.Equals(room.Id, _room.Id, StringComparison.Ordinal)) return;

            _room = room;
            Paint();
        }

        public void Reset()
        {
            _version++;
            _room = null;
            _account = null;
            _members.Clear();
            _membersKnown = false;
            _searchDebounce.Cancel();
            SetEditing(false);
            ClearInvite();
            ShowMembersLoading(false);
            MembersStatus.Visibility = Visibility.Collapsed;
        }

        public void Refresh()
        {
            if (_room != null) LoadMembersInBackground();
        }

        public void FocusFirst()
        {
            CloseButton.Focus(FocusState.Programmatic);
        }

        private bool IsOwner
        {
            get { return _room != null && _room.IsOwnedBy(_account); }
        }

        private void Paint()
        {
            if (_room == null) return;

            NameText.Text = _room.DisplayName;
            var description = (_room.Description ?? "").Trim();
            DescriptionText.Text = description;
            DescriptionText.Visibility = description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            var owner = _room.Owner;
            OwnerText.Text = IsOwner
                             ? LocalizedStrings.Get("SocialChatRoomOwnedByYou")
                             : owner == null ? "" : LocalizedStrings.Format("SocialChatRoomOwnedByFormat", owner.Handle);
            OwnerText.Visibility = string.IsNullOrEmpty(OwnerText.Text) ? Visibility.Collapsed : Visibility.Visible;

            var canWrite = SocialPolicies.Current.AllowsChatWriting;
            EditButton.Visibility = IsOwner && canWrite && !_editing ? Visibility.Visible : Visibility.Collapsed;
            DeleteButton.Visibility = IsOwner ? Visibility.Visible : Visibility.Collapsed;
            LeaveButton.Visibility = IsOwner ? Visibility.Collapsed : Visibility.Visible;

            _settingMute = true;
            try
            {
                MuteToggle.Visibility = IsOwner ? Visibility.Collapsed : Visibility.Visible;
                MuteToggle.IsOn = _room.IsMuted;
            }
            finally
            {
                _settingMute = false;
            }

            PaintMembers();
        }

        private void PaintMembers()
        {
            var count = _members.Count;
            MembersHeading.Text = _membersKnown
                                  ? LocalizedStrings.Format("SocialChatRoomMembersHeadingFormat", count)
                                  : LocalizedStrings.Get("SocialChatRoomMembersHeading");

            var full = _membersKnown && count > AppConstants.SocialChatRoomMemberLimit;
            var canInvite = IsOwner && SocialPolicies.Current.AllowsChatWriting;
            InvitePanel.Visibility = canInvite && !full ? Visibility.Visible : Visibility.Collapsed;
            FullText.Text = full ? LocalizedStrings.Format("SocialChatRoomFullFormat", AppConstants.SocialChatRoomMemberLimit) : "";
            FullText.Visibility = canInvite && full ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void LoadMembersInBackground()
        {
            var version = ++_version;
            var room = _room;
            try
            {
                if (room == null) return;

                ShowMembersLoading(_members.Count == 0);
                MembersStatus.Visibility = Visibility.Collapsed;

                var result = await SocialChatService.FetchMembersAsync(room.Id, CancellationToken.None);
                if (version != _version) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    MembersStatus.Text = LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialChatRoomMembersFailed");
                    MembersStatus.Visibility = Visibility.Visible;
                    return;
                }

                var rows = new List<SocialChatMemberItem>();
                if (room.Owner != null) rows.Add(new SocialChatMemberItem(room.Owner, true));
                rows.AddRange(result.Value.Where(membership => membership.User != null
                                                               && !string.Equals(membership.UserId, room.OwnerId, StringComparison.Ordinal))
                                          .Select(membership => new SocialChatMemberItem(membership.User, false)));

                _members.Clear();
                foreach (var row in rows)
                {
                    _members.Add(row);
                }

                _membersKnown = true;
                PaintMembers();
                MembersCounted?.Invoke(this, _members.Count);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not load the members", ex);
                if (version == _version)
                {
                    MembersStatus.Text = LocalizedStrings.Get("SocialChatRoomMembersFailed");
                    MembersStatus.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                if (version == _version) ShowMembersLoading(false);
            }
        }

        private void ShowMembersLoading(bool loading)
        {
            MembersLoading.IsActive = loading;
            MembersLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetEditing(bool editing)
        {
            _editing = editing;
            DetailsPanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            EditPanel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            EditButton.Visibility = !editing && IsOwner && SocialPolicies.Current.AllowsChatWriting ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearInvite()
        {
            InviteSearch.Text = "";
            InviteSearch.ItemsSource = null;
            InviteStatus.Visibility = Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_room == null) return;

            NameBox.Text = _room.Name ?? "";
            DescriptionBox.Text = _room.Description ?? "";
            SetEditing(true);
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
            NameBox.Focus(FocusState.Programmatic);
            NameBox.SelectAll();
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(NameBox.Text);
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            SetEditing(false);
            EditButton.Focus(FocusState.Programmatic);
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_room == null || _busy) return;

                var name = (NameBox.Text ?? "").Trim();
                if (name.Length == 0) return;

                SetBusy(true);
                var updated = await SocialChatService.UpdateRoomAsync(this, _room, name, (DescriptionBox.Text ?? "").Trim());
                if (updated == null) return;

                _room = updated;
                SetEditing(false);
                Paint();
                EditButton.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not save the room", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SaveButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(NameBox.Text);
            CancelEditButton.IsEnabled = !busy;
            LeaveButton.IsEnabled = !busy;
            DeleteButton.IsEnabled = !busy;
            MuteToggle.IsEnabled = !busy;
        }

        private async void MuteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingMute || _room == null || _busy) return;

                var mute = MuteToggle.IsOn;
                if (mute == _room.IsMuted) return;

                SetBusy(true);
                var done = await SocialChatService.SetRoomMutedAsync(this, _room, mute);
                if (done)
                {
                    _room = _room.WithMuted(mute);
                    return;
                }

                _settingMute = true;
                try
                {
                    MuteToggle.IsOn = _room.IsMuted;
                }
                finally
                {
                    _settingMute = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not change the mute", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void LeaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_room == null || _busy) return;

                SetBusy(true);
                await SocialChatService.LeaveRoomAsync(this, _room);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not leave the room", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_room == null || _busy) return;

                SetBusy(true);
                await SocialChatService.DeleteRoomAsync(this, _room);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not delete the room", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void MemberList_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var member = e.ClickedItem as SocialChatMemberItem;
                if (member != null && member.User != null) await SocialWindows.ShowUserAsync(member.User);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not open the person", ex);
            }
        }

        private async void InviteSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

                var token = _searchDebounce.Restart();
                InviteStatus.Visibility = Visibility.Collapsed;
                if (string.IsNullOrWhiteSpace(sender.Text))
                {
                    sender.ItemsSource = null;
                    return;
                }

                if (!await Debouncer.WaitAsync(AppConstants.SocialSuggestDelayMilliseconds, token)) return;
                var result = await SocialChatService.SearchPeopleAsync(sender.Text, SuggestionLimit, CancellationToken.None);
                if (token.IsCancellationRequested) return;

                if (result.Status != SocialApiStatus.Ok)
                {
                    sender.ItemsSource = null;
                    ShowInviteStatus(LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialChatSearchFailed"));
                    return;
                }

                var people = result.Value.Where(user => !_members.Any(member => string.Equals(member.User.Id, user.Id, StringComparison.Ordinal)))
                                         .ToList();
                sender.ItemsSource = people;
                if (people.Count == 0) ShowInviteStatus(LocalizedStrings.Get("SocialChatSearchNoOne"));
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatRoomPanel: the invite search was superseded");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: the invite search failed", ex);
            }
        }

        private async void InviteSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                var user = args.ChosenSuggestion as SocialUser;
                if (user == null)
                {
                    var people = sender.ItemsSource as IReadOnlyList<SocialUser>;
                    user = people == null ? null : people.FirstOrDefault();
                }

                if (user == null || _room == null) return;

                sender.Text = "";
                sender.ItemsSource = null;
                if (await SocialChatService.InviteAsync(this, _room, user))
                {
                    ShowInviteStatus(LocalizedStrings.Format("SocialChatInvitedFormat", user.Handle));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatRoomPanel: could not invite the person", ex);
            }
        }

        private void ShowInviteStatus(string text)
        {
            InviteStatus.Text = text ?? "";
            InviteStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
