using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed class SocialRecipientSuggestion
    {
        public SocialRecipientSuggestion(SocialUser user)
        {
            User = user;
            Title = SocialNoteItem.DisplayNameOf(user);
            Handle = user.Handle;
        }

        public SocialUser User { get; private set; }

        public string Title { get; private set; }

        public string Handle { get; private set; }

        public string AutomationName
        {
            get { return Title + " " + Handle; }
        }

        public override string ToString()
        {
            return Handle;
        }
    }

    public sealed partial class SocialRecipientBox : UserControl
    {
        private const int SuggestionLimit = 8;

        private readonly List<SocialUser> _recipients = new List<SocialUser>();

        private readonly Debouncer _searchDebounce = new Debouncer();

        public SocialRecipientBox()
        {
            this.InitializeComponent();
        }

        public event EventHandler Changed;

        public IReadOnlyList<SocialUser> Recipients
        {
            get { return _recipients.ToArray(); }
        }

        public void SetRecipients(IEnumerable<SocialUser> users)
        {
            _recipients.Clear();
            if (users != null)
            {
                foreach (var user in users)
                {
                    if (user != null && !Contains(user)) _recipients.Add(user);
                }
            }

            RenderChips();
        }

        public bool Add(SocialUser user)
        {
            if (user == null || string.IsNullOrEmpty(user.Id) || Contains(user)) return false;
            if (string.Equals(user.Id, SocialContentService.CurrentAccountId(), StringComparison.Ordinal)) return false;

            _recipients.Add(user);
            RenderChips();
            RaiseChanged();
            return true;
        }

        private bool Contains(SocialUser user)
        {
            foreach (var existing in _recipients)
            {
                if (string.Equals(existing.Id, user.Id, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private void RenderChips()
        {
            Chips.Children.Clear();
            foreach (var user in _recipients)
            {
                var name = SocialNoteItem.DisplayNameOf(user);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                content.Children.Add(new TextBlock { Text = user.Handle, VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new FontIcon { Glyph = "", FontSize = 10, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center });

                var chip = new Button
                {
                    Content = content,
                    Tag = user,
                    Padding = new Thickness(10, 4, 8, 4)
                };
                var label = LocalizedStrings.Format("SocialRecipientRemoveFormat", name);
                AutomationProperties.SetName(chip, label);
                ToolTipService.SetToolTip(chip, label);
                chip.Click += Chip_Click;
                Chips.Children.Add(chip);
            }

            Chips.Visibility = _recipients.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            var user = (sender as FrameworkElement)?.Tag as SocialUser;
            if (user == null) return;

            _recipients.RemoveAll(existing => string.Equals(existing.Id, user.Id, StringComparison.Ordinal));
            RenderChips();
            RaiseChanged();
            SearchBox.Focus(FocusState.Programmatic);
        }

        private void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
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
                if (cancellationToken.IsCancellationRequested) return;

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
                if (cancellationToken.IsCancellationRequested || result.Status != SocialApiStatus.Ok) return;

                var me = SocialContentService.CurrentAccountId();
                var suggestions = new List<SocialRecipientSuggestion>();
                foreach (var user in result.Value)
                {
                    if (user.Id != me && !Contains(user)) suggestions.Add(new SocialRecipientSuggestion(user));
                }

                SearchBox.ItemsSource = suggestions;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialRecipientBox: search cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialRecipientBox: search failed - {ex.Message}");
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var suggestion = args.ChosenSuggestion as SocialRecipientSuggestion;
            if (suggestion == null)
            {
                var list = sender.ItemsSource as IReadOnlyList<SocialRecipientSuggestion>;
                if (list != null && list.Count > 0) suggestion = list[0];
            }

            if (suggestion == null) return;

            Add(suggestion.User);
            sender.Text = "";
            sender.ItemsSource = null;
        }
    }
}
