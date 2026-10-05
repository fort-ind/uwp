using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xaml.Interactivity;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed class SocialSuggestionsBehavior : Behavior<TextBox>
    {
        private const int SuggestionLimit = 8;

        private const double Gap = 4;

        private static readonly bool s_popupXamlRootSupported =
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.Primitives.Popup", "XamlRoot");

        private readonly Debouncer _debounce = new Debouncer();

        private Popup _popup;

        private SocialSuggestionList _list;

        private ScrollViewer _scroller;

        private int _tokenStart = -1;

        private int _tokenEnd = -1;

        public bool SuggestPeople { get; set; } = true;

        public bool SuggestHashtags { get; set; } = true;

        public bool SuggestEmoji { get; set; } = true;

        public event EventHandler<SocialSuggestion> Accepted;

        public bool IsOpen
        {
            get { return _popup != null && _popup.IsOpen; }
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                AssociatedObject.TextChanged += OnTextChanged;
                AssociatedObject.SelectionChanged += OnSelectionChanged;
                AssociatedObject.PreviewKeyDown += OnPreviewKeyDown;
                AssociatedObject.LostFocus += OnLostFocus;
                AssociatedObject.Loaded += OnLoaded;
                AssociatedObject.Unloaded += OnUnloaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSuggestionsBehavior: Failed to attach - {ex.Message}");
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                Hide();
                AssociatedObject.TextChanged -= OnTextChanged;
                AssociatedObject.SelectionChanged -= OnSelectionChanged;
                AssociatedObject.PreviewKeyDown -= OnPreviewKeyDown;
                AssociatedObject.LostFocus -= OnLostFocus;
                AssociatedObject.Loaded -= OnLoaded;
                AssociatedObject.Unloaded -= OnUnloaded;
                DetachScroller();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSuggestionsBehavior: Failed to detach - {ex.Message}");
            }

            base.OnDetaching();
        }

        public void Hide()
        {
            _debounce.Cancel();
            if (_popup != null && _popup.IsOpen) _popup.IsOpen = false;
            _tokenStart = -1;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                DetachScroller();
                _scroller = VisualTreeSearch.FindAncestor<ScrollViewer>(AssociatedObject);
                if (_scroller != null) _scroller.ViewChanging += OnScrollerViewChanging;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSuggestionsBehavior: could not watch the scroller - {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Hide();
            DetachScroller();
        }

        private void DetachScroller()
        {
            if (_scroller == null) return;
            _scroller.ViewChanging -= OnScrollerViewChanging;
            _scroller = null;
        }

        private void OnScrollerViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            if (IsOpen) Hide();
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSoon();
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (IsOpen) UpdateSoon();
        }

        private void UpdateSoon()
        {
            int start;
            SocialSuggestionKind kind;
            string query;
            if (!FindToken(out start, out kind, out query))
            {
                Hide();
                return;
            }

            Suggest(kind, start, query, _debounce.Restart());
        }

        private bool FindToken(out int start, out SocialSuggestionKind kind, out string query)
        {
            start = -1;
            kind = SocialSuggestionKind.Mention;
            query = null;

            var box = AssociatedObject;
            var text = box.Text ?? "";
            var caret = Math.Min(box.SelectionStart, text.Length);
            if (box.SelectionLength > 0 || caret == 0) return false;

            var begin = caret;
            while (begin > 0 && !char.IsWhiteSpace(text[begin - 1])) begin--;

            var token = text.Substring(begin, caret - begin);
            if (token.Length < 2) return false;

            switch (token[0])
            {
                case '@':
                    if (!SuggestPeople) return false;
                    kind = SocialSuggestionKind.Mention;
                    query = token.Substring(1);
                    break;
                case '#':
                    if (!SuggestHashtags) return false;
                    kind = SocialSuggestionKind.Hashtag;
                    query = token.Substring(1);
                    if (query.IndexOf('#') >= 0) return false;
                    break;
                case ':':
                    if (!SuggestEmoji) return false;
                    kind = SocialSuggestionKind.Emoji;
                    query = token.Substring(1);
                    if (query.Length < 2 || query.IndexOf(':') >= 0) return false;
                    break;
                default:
                    return false;
            }

            start = begin;
            return true;
        }

        private async void Suggest(SocialSuggestionKind kind, int start, string query, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SocialSuggestDelayMilliseconds);
                if (cancellationToken.IsCancellationRequested) return;

                var suggestions = await FetchSuggestionsAsync(kind, query, cancellationToken);
                if (cancellationToken.IsCancellationRequested || AssociatedObject == null) return;

                if (suggestions.Count == 0)
                {
                    Hide();
                    return;
                }

                EnsurePopup();
                _tokenStart = start;
                _tokenEnd = Math.Min(AssociatedObject.SelectionStart, (AssociatedObject.Text ?? "").Length);
                _list.RequestedTheme = AssociatedObject.ActualTheme;
                _list.Show(suggestions);
                Place(start, suggestions.Count);
                _popup.IsOpen = true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialSuggestionsBehavior: suggestions cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSuggestionsBehavior: suggestions failed - {ex.Message}");
            }
        }

        private static async Task<List<SocialSuggestion>> FetchSuggestionsAsync(SocialSuggestionKind kind, string query, CancellationToken cancellationToken)
        {
            var suggestions = new List<SocialSuggestion>();

            switch (kind)
            {
                case SocialSuggestionKind.Mention:
                    {
                        string username = query, host = null;
                        var at = query.IndexOf('@');
                        if (at >= 0)
                        {
                            username = query.Substring(0, at);
                            host = query.Substring(at + 1);
                        }
                        if (username.Length == 0) break;

                        var token = await MisskeyAuthService.TryGetTokenAsync();
                        var result = await SocialApiService.SearchUsersAsync(token, username, string.IsNullOrEmpty(host) ? null : host,
                                                                             SuggestionLimit, cancellationToken);
                        if (result.Status != SocialApiStatus.Ok) break;

                        foreach (var user in result.Value)
                        {
                            suggestions.Add(SocialSuggestion.ForUser(user));
                        }
                        break;
                    }
                case SocialSuggestionKind.Hashtag:
                    {
                        var token = await MisskeyAuthService.TryGetTokenAsync();
                        var result = await SocialApiService.SearchHashtagsAsync(token, query, SuggestionLimit, cancellationToken);
                        if (result.Status != SocialApiStatus.Ok) break;

                        foreach (var tag in result.Value)
                        {
                            suggestions.Add(SocialSuggestion.ForTag(tag));
                        }
                        break;
                    }
                case SocialSuggestionKind.Emoji:
                    {
                        var catalog = await SocialEmojiService.GetCatalogAsync();
                        var tone = SocialEmojiService.SkinTone;
                        AddEmoji(suggestions, catalog.Custom, query, true, tone);
                        AddEmoji(suggestions, catalog.Custom, query, false, tone);
                        AddEmoji(suggestions, catalog.Unicode, query, true, tone);
                        AddEmoji(suggestions, catalog.Unicode, query, false, tone);
                        break;
                    }
            }

            return suggestions;
        }

        private static void AddEmoji(List<SocialSuggestion> suggestions, IReadOnlyList<SocialEmojiEntry> entries, string query, bool prefix, int tone)
        {
            foreach (var entry in entries)
            {
                if (suggestions.Count >= SuggestionLimit) return;

                var startsWith = (entry.Name ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase);
                var matches = prefix ? startsWith : !startsWith && entry.Matches(query);
                if (matches) suggestions.Add(SocialSuggestion.ForEmoji(entry, tone));
            }
        }

        private void EnsurePopup()
        {
            if (_popup != null) return;

            _list = new SocialSuggestionList();
            _list.SuggestionClicked += OnSuggestionClicked;
            _popup = new Popup
            {
                Child = _list,
                IsLightDismissEnabled = false
            };
            if (s_popupXamlRootSupported) _popup.XamlRoot = AssociatedObject.XamlRoot;
        }

        private void Place(int start, int count)
        {
            var box = AssociatedObject;
            Rect rect;
            try
            {
                rect = box.GetRectFromCharacterIndex(Math.Max(0, Math.Min(start, (box.Text ?? "").Length - 1)), false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSuggestionsBehavior: could not find the caret - {ex.Message}");
                rect = new Rect(0, 0, 0, box.ActualHeight);
            }

            var transform = box.TransformToVisual(null);
            var top = transform.TransformPoint(new Point(rect.X, rect.Top));
            var bottom = transform.TransformPoint(new Point(rect.X, rect.Bottom));

            var bounds = Window.Current.Bounds;
            var height = Math.Min(_list.MaxListHeight, count * SocialSuggestionList.RowHeight) + 2;
            var x = Math.Max(0, Math.Min(bottom.X, bounds.Width - SocialSuggestionList.ListWidth));
            var below = bottom.Y + Gap;
            var y = below + height > bounds.Height && top.Y - height - Gap >= 0 ? top.Y - height - Gap : below;

            _popup.HorizontalOffset = x;
            _popup.VerticalOffset = y;
        }

        private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (!IsOpen) return;

                switch (e.Key)
                {
                    case VirtualKey.Down:
                        _list.Move(1);
                        e.Handled = true;
                        break;
                    case VirtualKey.Up:
                        _list.Move(-1);
                        e.Handled = true;
                        break;
                    case VirtualKey.Enter:
                    case VirtualKey.Tab:
                        var selected = _list.Selected;
                        if (selected != null)
                        {
                            Accept(selected);
                            e.Handled = true;
                        }
                        break;
                    case VirtualKey.Escape:
                        Hide();
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSuggestionsBehavior: suggestion key failed - {ex.Message}");
            }
        }

        private void OnSuggestionClicked(object sender, SocialSuggestion suggestion)
        {
            Accept(suggestion);
        }

        private void Accept(SocialSuggestion suggestion)
        {
            var box = AssociatedObject;
            if (box == null) return;

            var text = box.Text ?? "";
            var start = _tokenStart;
            var end = Math.Min(Math.Max(_tokenEnd, box.SelectionStart), text.Length);
            Hide();
            if (start < 0 || start > end) return;

            box.Text = text.Substring(0, start) + suggestion.Insert + text.Substring(end);
            box.SelectionStart = start + suggestion.Insert.Length;
            box.Focus(FocusState.Programmatic);

            Accepted?.Invoke(this, suggestion);
        }
    }
}
