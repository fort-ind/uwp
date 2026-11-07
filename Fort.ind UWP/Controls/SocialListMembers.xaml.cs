using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialListMembers : UserControl
    {
        private const int PageSize = 100;

        private const int MaxMembers = 1000;

        private const int SuggestionLimit = 8;

        private const string AlreadyAddedCode = "ALREADY_ADDED";

        private const string NoSuchUserCode = "NO_SUCH_USER";

        private readonly ObservableCollection<SocialUserItem> _members = new ObservableCollection<SocialUserItem>();

        private readonly Debouncer _searchDebounce = new Debouncer();

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private SocialCollection _list;

        private bool _busy;

        private bool _released;

        private bool IsReleased => _released;

        public SocialListMembers()
        {
            this.InitializeComponent();

            MemberList.ItemsSource = _members;
        }

        public async void Start(SocialCollection list)
        {
            try
            {
                _list = list;
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialListMembers: could not load the members", ex);
            }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            try
            {
                _searchDebounce.Cancel();
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialListMembers: could not cancel pending requests", ex);
            }
        }

        private async Task ReloadAsync()
        {
            SetLoading(true);
            ShowStatus(null);

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var entries = new List<SocialFollowEntry>();
                string untilId = null;

                while (entries.Count < MaxMembers)
                {
                    var result = await SocialApiService.GetListMembersAsync(token, _list.Id, untilId, PageSize, _cancellation.Token);
                    if (IsReleased) return;

                    if (result.Status != SocialApiStatus.Ok)
                    {
                        ShowStatus(LocalizedStrings.Get(FailureKeyOf(result)));
                        break;
                    }

                    entries.AddRange(result.Value);
                    if (result.Value.Count < PageSize) break;

                    untilId = result.Value[result.Value.Count - 1].Id;
                }

                _members.Clear();
                foreach (var item in SocialUserItem.CreateAll(entries))
                {
                    _members.Add(item);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialListMembers: load cancelled");
            }
            finally
            {
                if (!IsReleased) SetLoading(false);
            }
        }

        private void SetLoading(bool loading)
        {
            LoadingRing.IsActive = loading;
            LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = !loading && _members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text ?? "";
            StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrEmpty(text)) AutomationHelper.AnnounceLiveRegion(StatusText);
        }

        private static string FailureKeyOf(SocialApiResult<IReadOnlyList<SocialFollowEntry>> result)
        {
            return SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric";
        }

        private async void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = (sender as FrameworkElement)?.Tag as SocialUserItem;
                if (item == null || _busy || IsReleased) return;

                _busy = true;
                ShowStatus(null);

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.ChangeListMemberAsync(token, _list.Id, item.User.Id, false, CancellationToken.None);
                if (IsReleased) return;

                if (result.Status == SocialApiStatus.Ok || string.Equals(result.ErrorCode, NoSuchUserCode, StringComparison.Ordinal))
                {
                    _members.Remove(item);
                    SetLoading(false);
                    AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialListMemberRemovedFormat", item.DisplayName, _list.Name),
                                                    "FortIndListMembers");
                }
                else
                {
                    ShowStatus(StatusFor(result, "SocialListMemberRemoveFailedFormat", item.DisplayName));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialListMembers: could not remove the member", ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private static string StatusFor(SocialApiResult<bool> result, string failureKey, string name)
        {
            if (result.Status == SocialApiStatus.PermissionDenied) return LocalizedStrings.Get("SocialListPermissionStatus");

            return LocalizedStrings.Format(failureKey, name) + " "
                   + LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status) ?? "SocialActionErrorGeneric");
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            Search(sender.Text, _searchDebounce.Restart());
        }

        private async void Search(string text, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SearchDebounceMilliseconds);
                if (cancellationToken.IsCancellationRequested || IsReleased) return;

                var query = (text ?? "").Trim().TrimStart('@');
                if (query.Length == 0)
                {
                    SearchBox.ItemsSource = null;
                    return;
                }

                string username = query, host = null;
                var at = query.IndexOf('@');
                if (at > 0)
                {
                    username = query.Substring(0, at);
                    host = query.Substring(at + 1);
                }

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.SearchUsersAsync(token, username, host, SuggestionLimit, cancellationToken);
                if (cancellationToken.IsCancellationRequested || IsReleased || result.Status != SocialApiStatus.Ok) return;

                var memberIds = new HashSet<string>(_members.Select(member => member.User.Id), StringComparer.Ordinal);
                SearchBox.ItemsSource = result.Value.Where(user => !memberIds.Contains(user.Id))
                                                    .Select(user => new SocialRecipientSuggestion(user))
                                                    .ToList();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialListMembers: search cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialListMembers: search failed", ex);
            }
        }

        private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                var suggestion = args.ChosenSuggestion as SocialRecipientSuggestion;
                if (suggestion == null)
                {
                    var suggestions = sender.ItemsSource as IReadOnlyList<SocialRecipientSuggestion>;
                    if (suggestions != null && suggestions.Count > 0) suggestion = suggestions[0];
                }

                if (suggestion == null || _busy || IsReleased) return;

                var limit = SocialPolicies.Current.ListMemberLimit;
                var already = _members.Any(member => string.Equals(member.User.Id, suggestion.User.Id, StringComparison.Ordinal));
                if (limit.HasValue && !already && _members.Count >= limit.Value)
                {
                    ShowStatus(LocalizedStrings.Get("SocialListFull"));
                    return;
                }

                _busy = true;
                ShowStatus(null);
                _searchDebounce.Cancel();

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.ChangeListMemberAsync(token, _list.Id, suggestion.User.Id, true, CancellationToken.None);
                if (IsReleased) return;

                if (result.Status == SocialApiStatus.Ok || string.Equals(result.ErrorCode, AlreadyAddedCode, StringComparison.Ordinal))
                {
                    sender.Text = "";
                    sender.ItemsSource = null;
                    await ReloadAsync();
                    AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialListMemberAddedFormat", suggestion.Title, _list.Name),
                                                    "FortIndListMembers");
                }
                else
                {
                    ShowStatus(AddFailureText(result, suggestion.Title));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialListMembers: could not add the member", ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private static string AddFailureText(SocialApiResult<bool> result, string name)
        {
            if (string.Equals(result.ErrorCode, "TOO_MANY_USERS", StringComparison.Ordinal))
            {
                return LocalizedStrings.Get("SocialListFull");
            }

            if (string.Equals(result.ErrorCode, "BLOCKED", StringComparison.Ordinal))
            {
                return LocalizedStrings.Format("SocialListMemberBlockedFormat", name);
            }

            return StatusFor(result, "SocialListMemberAddFailedFormat", name);
        }
    }
}
