using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed partial class SocialComposer
    {
        private SocialAttachmentItem _markUpTarget;

        private int _lastWordStart = -1;

        private string _lastWord;

        private List<string> _lastAlternates;

        private bool InkToolsAllowed
        {
            get { return _context.Mode != SocialComposerMode.Reply; }
        }

        public bool IsDrawing
        {
            get { return DrawSurface != null && DrawSurface.IsShown(); }
        }

        public void CloseInkTools()
        {
            HideHandwritePad();
            if (IsDrawing)
            {
                DrawSurface.Reset();
                HideDrawing();
            }
        }

        private void InkMenu_Opening(object sender, object e)
        {
            HandwriteItem.IsChecked = HandwritePad != null && HandwritePad.IsShown();
            DrawItem.IsEnabled = _attachments.Count < AppConstants.SocialAttachmentLimit && !_posting;
        }

        private void HandwriteItem_Click(object sender, RoutedEventArgs e)
        {
            if (HandwriteItem.IsChecked) ShowHandwritePad();
            else HideHandwritePad();
        }

        private void ShowHandwritePad()
        {
            if (HandwritePad == null) FindName("HandwritePad");
            HandwritePad.Reset();
            HandwritePad.Visibility = Visibility.Visible;
            ForgetLastWord();
            if (Editor.FocusState == FocusState.Unfocused) FocusEditorAt(Editor.SelectionStart);
        }

        private void HideHandwritePad()
        {
            if (HandwritePad == null || !HandwritePad.IsShown()) return;

            HandwritePad.Reset();
            HandwritePad.Visibility = Visibility.Collapsed;
            ForgetLastWord();
        }

        private void HandwritePad_CloseRequested(object sender, EventArgs e)
        {
            HideHandwritePad();
            Editor.Focus(FocusState.Programmatic);
        }

        private void HandwritePad_Recognized(object sender, IReadOnlyList<InkRecognitionWord> words)
        {
            try
            {
                if (_posting || words.Count == 0) return;

                var text = InkRecognition.Join(words);
                var current = Editor.Text ?? "";
                var start = Math.Min(Editor.SelectionStart, current.Length);
                var length = Math.Min(Editor.SelectionLength, current.Length - start);
                var before = current.Substring(0, start);
                var after = current.Substring(start + length);

                if (before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1]) && !InkRecognition.StartsWithClosingPunctuation(text))
                {
                    text = " " + text;
                }

                var last = words[words.Count - 1];
                Editor.Text = before + text + after;
                Editor.SelectionStart = start + text.Length;
                Editor.SelectionLength = 0;
                Editor.Focus(FocusState.Programmatic);

                _lastWord = last.Text;
                _lastWordStart = start + text.Length - last.Text.Length;
                _lastAlternates = last.Alternates.ToList();
                HandwritePad.ShowAlternates(_lastAlternates);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not insert the handwriting - {ex.Message}");
            }
        }

        private void HandwritePad_AlternateChosen(object sender, string alternate)
        {
            try
            {
                var current = Editor.Text ?? "";
                if (_lastWord == null || _lastWordStart < 0 || _lastWordStart + _lastWord.Length > current.Length
                    || !string.Equals(current.Substring(_lastWordStart, _lastWord.Length), _lastWord, StringComparison.Ordinal))
                {
                    ForgetLastWord();
                    return;
                }

                Editor.Text = current.Substring(0, _lastWordStart) + alternate + current.Substring(_lastWordStart + _lastWord.Length);
                Editor.SelectionStart = _lastWordStart + alternate.Length;
                Editor.SelectionLength = 0;
                Editor.Focus(FocusState.Programmatic);

                _lastAlternates.Remove(alternate);
                _lastAlternates.Insert(0, _lastWord);
                _lastWord = alternate;
                HandwritePad.ShowAlternates(_lastAlternates);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not swap the word - {ex.Message}");
            }
        }

        private void HandwritePad_KeyPressed(object sender, HandwritingPadKey key)
        {
            try
            {
                if (_posting) return;

                ForgetLastWord();
                switch (key)
                {
                    case HandwritingPadKey.Space:
                        InsertText(" ", false);
                        break;
                    case HandwritingPadKey.Enter:
                        InsertText("\r", false);
                        break;
                    case HandwritingPadKey.Backspace:
                        DeleteBackward();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: pad key failed - {ex.Message}");
            }
        }

        private void DeleteBackward()
        {
            var current = Editor.Text ?? "";
            var start = Math.Min(Editor.SelectionStart, current.Length);
            var length = Math.Min(Editor.SelectionLength, current.Length - start);

            if (length == 0)
            {
                if (start == 0) return;

                var remove = start >= 2 && char.IsSurrogatePair(current[start - 2], current[start - 1]) ? 2 : 1;
                start -= remove;
                length = remove;
            }

            Editor.Text = current.Substring(0, start) + current.Substring(start + length);
            Editor.SelectionStart = start;
            Editor.Focus(FocusState.Programmatic);
        }

        private void ForgetLastWord()
        {
            _lastWord = null;
            _lastWordStart = -1;
            _lastAlternates = null;
            if (HandwritePad != null) HandwritePad.ClearAlternates();
        }

        private void DrawItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_attachments.Count >= AppConstants.SocialAttachmentLimit) return;

                _markUpTarget = null;
                ShowDrawing();
                DrawSurface.OpenBlank();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not open the drawing - {ex.GetType().Name}: {ex.Message}");
                HideDrawing();
            }
        }

        private async void MarkUpAttachment_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as SocialAttachmentItem;
            if (item == null || !item.CanMarkUp || _posting) return;

            try
            {
                using (var stream = await OpenAttachmentAsync(item))
                {
                    if (stream == null)
                    {
                        await DialogService.ShowMessageAsync(this,
                                                             LocalizedStrings.Get("InkMarkupOpenFailedTitle"),
                                                             LocalizedStrings.Get("SocialActionErrorGeneric"),
                                                             LocalizedStrings.Get("DialogOk"));
                        return;
                    }

                    _markUpTarget = item;
                    ShowDrawing();
                    var type = item.File != null && !string.IsNullOrEmpty(item.File.Type) ? item.File.Type : item.ContentType;
                    await DrawSurface.OpenPhotoAsync(stream, item.Name, type);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not open the image to mark up - {ex.GetType().Name}: {ex.Message}");
                _markUpTarget = null;
                HideDrawing();
            }
        }

        private static async Task<IRandomAccessStream> OpenAttachmentAsync(SocialAttachmentItem item)
        {
            if (item.OpenAsync != null) return await item.OpenAsync();

            var uri = item.File == null ? null : WebLauncher.TryCreateFetchUri(item.File.Url);
            if (uri == null) return null;

            var buffer = await SocialApiService.DownloadMediaAsync(uri, CancellationToken.None);
            if (buffer == null) return null;

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(buffer);
            stream.Seek(0);
            return stream;
        }

        private void ShowDrawing()
        {
            if (DrawSurface == null) FindName("DrawSurface");

            Suggestions.Hide();
            AvatarPicture.Visibility = Visibility.Collapsed;
            BodyScroller.Visibility = Visibility.Collapsed;
            BottomPanel.Visibility = Visibility.Collapsed;
            DrawSurface.Visibility = Visibility.Visible;
        }

        private void HideDrawing()
        {
            if (DrawSurface != null) DrawSurface.Visibility = Visibility.Collapsed;
            AvatarPicture.Visibility = Visibility.Visible;
            BodyScroller.Visibility = Visibility.Visible;
            BottomPanel.Visibility = Visibility.Visible;
        }

        private void DrawSurface_Cancelled(object sender, EventArgs e)
        {
            _markUpTarget = null;
            HideDrawing();
            Editor.Focus(FocusState.Programmatic);
        }

        private async void DrawSurface_Attached(object sender, SocialDrawingResult result)
        {
            var target = _markUpTarget;
            _markUpTarget = null;
            HideDrawing();

            try
            {
                var index = target == null ? -1 : _attachments.IndexOf(target);
                if (target != null && index < 0) target = null;

                if (target == null && _attachments.Count >= AppConstants.SocialAttachmentLimit) return;

                SocialAttachmentItem added;
                if (target != null)
                {
                    try
                    {
                        if (target.Cancellation != null) target.Cancellation.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialComposer: could not cancel the replaced upload - {ex.Message}");
                    }

                    _attachments.Remove(target);
                    added = await AddBytesAsync(result.Bytes, result.Name, result.ContentType, index,
                                                target.File == null ? null : target.File.Comment,
                                                target.File != null && target.File.IsSensitive);

                    AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("InkMarkupReplacedFormat", target.Name), "InkMarkupReplaced");
                }
                else
                {
                    added = await AddBytesAsync(result.Bytes, result.Name, result.ContentType, -1, null, false);
                    AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("InkDrawingAttachedFormat", added.Name), "InkDrawingAttached");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not attach the drawing - {ex.GetType().Name}: {ex.Message}");
            }

            Editor.Focus(FocusState.Programmatic);
        }
    }
}
