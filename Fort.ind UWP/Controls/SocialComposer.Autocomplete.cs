using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialSuggestionKind
    {
        Mention,
        Hashtag,
        Emoji
    }

    public sealed class SocialSuggestion
    {
        private const int ImageDecodeSize = 24;

        private SocialSuggestion()
        {
        }

        public SocialSuggestionKind Kind { get; private set; }

        public string Title { get; private set; }

        public string Detail { get; private set; }

        public string Glyph { get; private set; }

        public ImageSource Image { get; private set; }

        public string Insert { get; private set; }

        public SocialUser User { get; private set; }

        public bool HasDetail
        {
            get { return !string.IsNullOrEmpty(Detail); }
        }

        public string AutomationName
        {
            get { return HasDetail ? Title + ", " + Detail : Title; }
        }

        public static SocialSuggestion ForUser(SocialUser user)
        {
            var host = string.IsNullOrEmpty(user.Host) ? "" : "@" + user.Host;
            return new SocialSuggestion
            {
                Kind = SocialSuggestionKind.Mention,
                Title = SocialNoteItem.DisplayNameOf(user),
                Detail = user.Handle,
                Insert = "@" + user.Username + host + " ",
                Image = Bitmap(WebLauncher.TryCreateFetchUri(user.AvatarUrl)),
                User = user
            };
        }

        public static SocialSuggestion ForTag(string tag)
        {
            return new SocialSuggestion
            {
                Kind = SocialSuggestionKind.Hashtag,
                Title = "#" + tag,
                Insert = "#" + tag + " ",
                Glyph = "#"
            };
        }

        public static SocialSuggestion ForEmoji(SocialEmojiEntry entry, int tone)
        {
            var key = entry.KeyWithTone(tone);
            return new SocialSuggestion
            {
                Kind = SocialSuggestionKind.Emoji,
                Title = entry.IsCustom ? ":" + entry.Name + ":" : entry.Name,
                Glyph = entry.IsCustom ? "" : key,
                Image = entry.IsCustom ? Bitmap(entry.ImageUri) : null,
                Insert = entry.IsCustom ? ":" + entry.Name + ": " : key
            };
        }

        private static ImageSource Bitmap(Uri uri)
        {
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelHeight = ImageDecodeSize;
            bitmap.UriSource = uri;
            return bitmap;
        }
    }

    public sealed partial class SocialComposer
    {
        private const int SuggestionLimit = 8;

        private const double SuggestionWidth = 280;

        private const double SuggestionRowHeight = 48;

        private readonly Debouncer _suggestDebounce = new Debouncer();

        private int _tokenStart = -1;

        private int _tokenEnd = -1;

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!_loading && SuggestionPopup.IsOpen) UpdateSuggestionsSoon();
        }

        private void UpdateSuggestionsSoon()
        {
            int start;
            SocialSuggestionKind kind;
            string query;
            if (!FindToken(out start, out kind, out query))
            {
                HideSuggestions();
                return;
            }

            Suggest(kind, start, query, _suggestDebounce.Restart());
        }

        private bool FindToken(out int start, out SocialSuggestionKind kind, out string query)
        {
            start = -1;
            kind = SocialSuggestionKind.Mention;
            query = null;

            var text = Editor.Text ?? "";
            var caret = Math.Min(Editor.SelectionStart, text.Length);
            if (Editor.SelectionLength > 0 || caret == 0) return false;

            var begin = caret;
            while (begin > 0 && !char.IsWhiteSpace(text[begin - 1])) begin--;

            var token = text.Substring(begin, caret - begin);
            if (token.Length < 2) return false;

            switch (token[0])
            {
                case '@':
                    kind = SocialSuggestionKind.Mention;
                    query = token.Substring(1);
                    break;
                case '#':
                    kind = SocialSuggestionKind.Hashtag;
                    query = token.Substring(1);
                    if (query.IndexOf('#') >= 0) return false;
                    break;
                case ':':
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
                if (cancellationToken.IsCancellationRequested) return;

                if (suggestions.Count == 0)
                {
                    HideSuggestions();
                    return;
                }

                _tokenStart = start;
                _tokenEnd = Math.Min(Editor.SelectionStart, (Editor.Text ?? "").Length);
                SuggestionList.ItemsSource = suggestions;
                SuggestionList.SelectedIndex = 0;
                PlaceSuggestions(start, suggestions.Count);
                SuggestionPopup.IsOpen = true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialComposer: suggestions cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: suggestions failed - {ex.Message}");
            }
        }

        private async Task<List<SocialSuggestion>> FetchSuggestionsAsync(SocialSuggestionKind kind, string query, CancellationToken cancellationToken)
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

        private void PlaceSuggestions(int start, int count)
        {
            Rect rect;
            try
            {
                rect = Editor.GetRectFromCharacterIndex(Math.Max(0, Math.Min(start, (Editor.Text ?? "").Length - 1)), false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not find the caret - {ex.Message}");
                rect = new Rect(0, 0, 0, Editor.ActualHeight);
            }

            var transform = Editor.TransformToVisual(RootGrid);
            var top = transform.TransformPoint(new Point(rect.X, rect.Top));
            var bottom = transform.TransformPoint(new Point(rect.X, rect.Bottom));

            var height = Math.Min(SuggestionList.MaxHeight, count * SuggestionRowHeight) + 2;
            var x = Math.Max(0, Math.Min(bottom.X, RootGrid.ActualWidth - SuggestionWidth));
            var below = bottom.Y + 4;
            var y = below + height > RootGrid.ActualHeight && top.Y - height - 4 >= 0 ? top.Y - height - 4 : below;

            SuggestionPopup.HorizontalOffset = x;
            SuggestionPopup.VerticalOffset = y;
        }

        private void HideSuggestions()
        {
            _suggestDebounce.Cancel();
            if (SuggestionPopup.IsOpen) SuggestionPopup.IsOpen = false;
            _tokenStart = -1;
        }

        private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (e.Key == VirtualKey.Escape && _context.Mode == SocialComposerMode.Reply && !SuggestionPopup.IsOpen && IsDraftEmpty())
                {
                    SetExpanded(false);
                    return;
                }

                if (!SuggestionPopup.IsOpen) return;

                var count = SuggestionList.Items.Count;
                switch (e.Key)
                {
                    case VirtualKey.Down:
                        SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + 1) % count;
                        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                        e.Handled = true;
                        break;
                    case VirtualKey.Up:
                        SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex - 1 + count) % count;
                        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                        e.Handled = true;
                        break;
                    case VirtualKey.Enter:
                    case VirtualKey.Tab:
                        var selected = SuggestionList.SelectedItem as SocialSuggestion;
                        if (selected != null)
                        {
                            Accept(selected);
                            e.Handled = true;
                        }
                        break;
                    case VirtualKey.Escape:
                        HideSuggestions();
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: suggestion key failed - {ex.Message}");
            }
        }

        private void SuggestionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var suggestion = e.ClickedItem as SocialSuggestion;
            if (suggestion != null) Accept(suggestion);
        }

        private void Accept(SocialSuggestion suggestion)
        {
            var text = Editor.Text ?? "";
            var start = _tokenStart;
            var end = Math.Min(Math.Max(_tokenEnd, Editor.SelectionStart), text.Length);
            HideSuggestions();
            if (start < 0 || start > end) return;

            Editor.Text = text.Substring(0, start) + suggestion.Insert + text.Substring(end);
            Editor.SelectionStart = start + suggestion.Insert.Length;
            Editor.Focus(FocusState.Programmatic);

            if (suggestion.User != null && _visibility == SocialPostService.DirectVisibility && _context.Mode != SocialComposerMode.Reply)
            {
                RecipientBox.Add(suggestion.User);
            }
        }
    }
}
