using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialComposerMode
    {
        Window,
        Reply,
        Share
    }

    public sealed class SocialComposeContext
    {
        public SocialComposeContext(SocialComposerMode mode, SocialNote replyTo, SocialNote quote, SocialNote editing)
        {
            Mode = mode;
            ReplyTo = replyTo;
            Quote = quote;
            Editing = editing;
        }

        public SocialComposerMode Mode { get; private set; }

        public SocialNote ReplyTo { get; private set; }

        public SocialNote Quote { get; private set; }

        public SocialNote Editing { get; private set; }
    }

    public sealed partial class SocialComposer : UserControl
    {
        private const int AvatarDecodeSize = 40;

        private const double ReplyCollapsedHeight = 36;

        private const double ReplyAvatarSize = 32;

        private const double ReplyExpandedHeight = 96;

        private const double WindowEditorHeight = 140;

        private readonly ObservableCollection<SocialAttachmentItem> _attachments = new ObservableCollection<SocialAttachmentItem>();

        private readonly Debouncer _changeDebounce = new Debouncer();

        private readonly Debouncer _mentionDebounce = new Debouncer();

        private SocialComposeContext _context = new SocialComposeContext(SocialComposerMode.Window, null, null, null);

        private string _visibility = SocialNoteActionService.PublicVisibility;

        private bool _localOnly;

        private string _acceptance;

        private string _quoteId;

        private string _replyPlaceholder;

        private bool _posting;

        private bool _expanded = true;

        private bool _loading;

        private int _recipientsLoading;

        public SocialComposer()
        {
            this.InitializeComponent();

            AttachmentList.ItemsSource = _attachments;
            _attachments.CollectionChanged += Attachments_CollectionChanged;

            AddPostAccelerator(Editor);
            AddPostAccelerator(WarningBox);
            BuildExpiryOptions();

            AddHandler(DragOverEvent, new DragEventHandler(Composer_DragOver), true);
            AddHandler(DropEvent, new DragEventHandler(Composer_Drop), true);

            Suggestions.Accepted += Suggestions_Accepted;

            Loaded += SocialComposer_Loaded;
        }

        public event EventHandler DraftChanged;

        public event EventHandler<SocialNote> Posted;

        public event EventHandler Discarded;

        public SocialComposerMode Mode
        {
            get { return _context.Mode; }
        }

        public bool IsBusy
        {
            get { return _posting || HasUploads; }
        }

        private bool HasUploads
        {
            get
            {
                foreach (var item in _attachments)
                {
                    if (item.IsUploading) return true;
                }

                return false;
            }
        }

        public static Visibility Shown(bool value)
        {
            return value ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SocialComposer_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowAvatar();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not show the avatar", ex);
            }
        }

        private void ShowAvatar()
        {
            var user = ProfileService.CurrentUser;
            if (user == null) return;

            AvatarPicture.DisplayName = user.DisplayName ?? user.Username ?? "";
            var uri = WebLauncher.TryCreateFetchUri(user.AvatarUrl);
            if (uri == null || AvatarPicture.ProfilePicture != null) return;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = AvatarDecodeSize;
            bitmap.DecodePixelHeight = AvatarDecodeSize;
            bitmap.UriSource = uri;
            AvatarPicture.ProfilePicture = bitmap;
        }

        private void AddPostAccelerator(UIElement element)
        {
            var accelerator = new KeyboardAccelerator { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += PostAccelerator_Invoked;
            element.KeyboardAccelerators.Add(accelerator);
            element.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        }

        private void PostAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            Post();
        }

        public void Load(SocialComposeDraft draft, SocialComposeContext context)
        {
            _loading = true;
            try
            {
                _context = context ?? new SocialComposeContext(SocialComposerMode.Window, null, null, null);
                draft = draft ?? SocialComposeDraft.Empty;

                ApplyModeLayout();

                Editor.Text = draft.Text ?? "";
                Editor.SelectionStart = Editor.Text.Length;

                WarningBox.Text = draft.ContentWarning ?? "";
                WarningToggle.IsChecked = draft.HasContentWarning;
                WarningBox.Visibility = Shown(draft.HasContentWarning);

                _visibility = draft.Visibility;
                _localOnly = draft.LocalOnly;
                _acceptance = draft.ReactionAcceptance;
                RecipientBox.SetRecipients(draft.Recipients);

                _attachments.Clear();
                foreach (var file in draft.Files)
                {
                    _attachments.Add(SocialAttachmentItem.ForFile(file));
                }

                LoadPoll(draft.Poll, _context.Editing != null ? SocialDraftPoll.FromPoll(_context.Editing.Poll) : null);

                _quoteId = draft.QuoteId;
                ShowQuote(_context.Quote != null && _context.Quote.Id == _quoteId ? _context.Quote : null);

                PreviewToggle.IsChecked = false;
                PreviewHost.Visibility = Visibility.Collapsed;

                ApplyVisibility();
                ApplyAcceptance();
                Suggestions.Hide();
            }
            finally
            {
                _loading = false;
            }

            if (_context.Mode == SocialComposerMode.Reply && !IsDraftEmpty()) SetExpanded(true);
            UpdateState();

            try
            {
                ShowAvatar();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not show the avatar", ex);
            }
        }

        public SocialComposeDraft GetDraft()
        {
            var files = _attachments.Where(item => item.State == SocialAttachmentState.Done && item.File != null)
                                    .Select(item => item.File)
                                    .ToList();

            return SocialComposeDraft.Create(Editor.Text,
                                             WarningToggle.IsChecked.GetValueOrDefault() ? WarningBox.Text : null,
                                             _visibility,
                                             _localOnly,
                                             _visibility == SocialPostService.DirectVisibility ? RecipientBox.Recipients : null,
                                             _acceptance,
                                             GetPoll(),
                                             files,
                                             _quoteId,
                                             _context.ReplyTo == null ? null : _context.ReplyTo.Id,
                                             _context.Editing == null ? null : _context.Editing.Id);
        }

        public void FocusEditor()
        {
            FocusEditorAt(Editor.Text.Length);
        }

        public void FocusEditorAt(int position)
        {
            if (_context.Mode == SocialComposerMode.Reply) SetExpanded(true);
            Editor.Focus(FocusState.Programmatic);
            Editor.SelectionStart = Math.Max(0, Math.Min(position, Editor.Text.Length));
        }

        public void ApplyReplyDefaults(SocialReplyDefaults defaults)
        {
            if (defaults == null) return;

            _visibility = defaults.Visibility;
            _localOnly = defaults.LocalOnly;
            ApplyVisibility();

            if (string.IsNullOrEmpty(Editor.Text) && !string.IsNullOrEmpty(defaults.Prefix))
            {
                _loading = true;
                Editor.Text = defaults.Prefix;
                Editor.SelectionStart = Editor.Text.Length;
                _loading = false;
            }

            if (defaults.RecipientIds.Count > 0) AddRecipients(defaults.RecipientIds);
            UpdateState();
        }

        public async void AddRecipients(IReadOnlyList<string> ids)
        {
            try
            {
                if (ids == null || ids.Count == 0) return;

                _recipientsLoading++;
                UpdateState();

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.GetUsersAsync(token, ids, CancellationToken.None);
                if (result.Status != SocialApiStatus.Ok) return;

                foreach (var user in result.Value)
                {
                    RecipientBox.Add(user);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not load the recipients", ex);
            }
            finally
            {
                if (ids != null && ids.Count > 0)
                {
                    _recipientsLoading = Math.Max(0, _recipientsLoading - 1);
                    UpdateState();
                }
            }
        }

        private void ApplyModeLayout()
        {
            var reply = _context.Mode == SocialComposerMode.Reply;
            var editing = _context.Editing != null;

            VisibilityButton.Visibility = Shown(!reply);
            VisibilityButton.IsEnabled = !editing;
            AttachButton.Visibility = Shown(_context.Mode != SocialComposerMode.Share);
            InkButton.Visibility = Shown(InkToolsAllowed);
            if (!InkToolsAllowed) CloseInkTools();
            PollToggle.Visibility = Shown(!reply);
            PreviewToggle.Visibility = Shown(!reply);
            MoreButton.Visibility = Shown(!reply);
            ReplyVisibilityGlyph.Visibility = Shown(reply);

            PostLabel.Text = LocalizedStrings.Get(reply ? "SocialComposeReplyButton" : editing ? "SocialComposeSaveButton" : "SocialComposePostButton");
            AutomationProperties.SetName(PostButton, PostLabel.Text);

            if (reply)
            {
                var handle = _context.ReplyTo != null && _context.ReplyTo.User != null ? _context.ReplyTo.User.Handle : "";
                _replyPlaceholder = LocalizedStrings.Format("SocialComposeReplyPlaceholderFormat", handle);
                Editor.PlaceholderText = _replyPlaceholder;
                AvatarPicture.Width = ReplyAvatarSize;
                AvatarPicture.Height = ReplyAvatarSize;
                AvatarPicture.Margin = new Thickness(0, BodyPanel.Padding.Top + (ReplyCollapsedHeight - ReplyAvatarSize) / 2, 0, 0);
                BodyScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                BodyScroller.VerticalScrollMode = ScrollMode.Disabled;
                SetExpanded(false);
            }
            else
            {
                Editor.PlaceholderText = LocalizedStrings.Get(_context.Quote != null ? "SocialComposeQuotePlaceholder" : "SocialComposePlaceholder");
                Editor.MinHeight = WindowEditorHeight;
                Toolbar.Visibility = Visibility.Visible;
                _expanded = true;
            }
        }

        private void SetExpanded(bool expanded)
        {
            if (_context.Mode != SocialComposerMode.Reply) return;

            _expanded = expanded;
            Toolbar.Visibility = Shown(expanded);
            Editor.MinHeight = expanded ? ReplyExpandedHeight : ReplyCollapsedHeight;
        }

        private bool IsDraftEmpty()
        {
            return GetDraft().IsEmpty && _attachments.Count == 0;
        }

        private void Editor_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!_expanded) SetExpanded(true);
        }

        private async void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_context.Mode != SocialComposerMode.Reply) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                var focused = FocusManager.GetFocusedElement() as DependencyObject;
                if (focused != null && VisualTreeSearch.IsDescendantOf(focused, this)) return;
                if (EmojiFlyout.IsOpen || Suggestions.IsOpen) return;

                if (IsDraftEmpty()) SetExpanded(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not collapse the reply box", ex);
            }
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            OnEdited();
            if (PreviewHost.IsShown()) RenderPreview();
            if (_visibility == SocialPostService.DirectVisibility && _context.Mode != SocialComposerMode.Reply)
            {
                AddMentionedRecipients(_mentionDebounce.Restart());
            }
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            OnEdited();
            if (PreviewHost.IsShown()) RenderPreview();
        }

        private void RecipientBox_Changed(object sender, EventArgs e)
        {
            if (!_loading) OnEdited();
        }

        private void OnEdited()
        {
            UpdateState();
            RaiseChangedSoon(_changeDebounce.Restart());
        }

        private async void RaiseChangedSoon(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SocialComposeChangeDelayMilliseconds);
                if (cancellationToken.IsCancellationRequested) return;

                DraftChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: change notification failed", ex);
            }
        }

        public void FlushChange()
        {
            _changeDebounce.Cancel();
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void AddMentionedRecipients(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SocialMentionResolveDelayMilliseconds);
                if (cancellationToken.IsCancellationRequested) return;

                var found = await SocialPostService.ResolveMentionsAsync(Editor.Text, RecipientBox.Recipients);
                if (cancellationToken.IsCancellationRequested || _visibility != SocialPostService.DirectVisibility) return;

                foreach (var user in found)
                {
                    RecipientBox.Add(user);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not add the mentioned people", ex);
            }
        }

        private void UpdateState()
        {
            var length = SocialPostService.Length(Editor.Text);
            Ring.Update(length, AppConstants.SocialNoteMaxLength);

            PostButton.IsEnabled = CanPost();
            AttachmentList.Visibility = Shown(_attachments.Count > 0);
            AttachButton.IsEnabled = _attachments.Count < AppConstants.SocialAttachmentLimit && !_posting;
            InkButton.IsEnabled = !_posting;
            UpdatePollWarning();
        }

        private bool CanPost()
        {
            if (_posting || _recipientsLoading > 0) return false;

            var text = SocialPostService.NormalizeText(Editor.Text);
            var poll = GetPoll();
            var done = 0;
            foreach (var item in _attachments)
            {
                if (item.IsUploading || item.HasError) return false;
                if (item.State == SocialAttachmentState.Done) done++;
            }

            var hasContent = text.Trim().Length > 0 || done > 0 || poll != null || _quoteId != null;
            if (!hasContent || text.Length > AppConstants.SocialNoteMaxLength) return false;

            if (WarningToggle.IsChecked.GetValueOrDefault())
            {
                var warning = WarningBox.Text ?? "";
                if (warning.Trim().Length == 0 || warning.Length > AppConstants.SocialWarningMaxLength) return false;
            }

            if (done > AppConstants.SocialAttachmentLimit) return false;
            return !PollPanel.IsShown() || IsPollValid();
        }

        private void PostButton_Click(object sender, RoutedEventArgs e)
        {
            Post();
        }

        private async void Post()
        {
            try
            {
                await PostAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: posting failed", ex);
                SetPosting(false);
            }
        }

        public async Task<bool> PostAsync()
        {
            if (!CanPost()) return false;

            Suggestions.Hide();
            SetPosting(true);
            try
            {
                if (_visibility == SocialPostService.DirectVisibility && _context.Mode != SocialComposerMode.Reply)
                {
                    _mentionDebounce.Cancel();
                    foreach (var user in await SocialPostService.ResolveMentionsAsync(Editor.Text, RecipientBox.Recipients))
                    {
                        RecipientBox.Add(user);
                    }
                }

                if (!await ConfirmDescriptionsAsync() || !await ConfirmPollResetAsync()) return false;

                var result = await SocialPostService.PostAsync(this, GetDraft());
                if (!result.Succeeded) return false;

                Posted?.Invoke(this, result.Note);
                return true;
            }
            finally
            {
                SetPosting(false);
            }
        }

        private async Task<bool> ConfirmDescriptionsAsync()
        {
            SocialAttachmentItem first = null;
            foreach (var item in _attachments)
            {
                if (item.NeedsDescription)
                {
                    first = item;
                    break;
                }
            }

            if (first == null) return true;

            var postAnyway = await DialogService.ShowConfirmAsync(this,
                                                                  LocalizedStrings.Get("SocialComposeAltDialogTitle"),
                                                                  LocalizedStrings.Get("SocialComposeAltDialogBody"),
                                                                  LocalizedStrings.Get("SocialComposeAltDialogPost"),
                                                                  LocalizedStrings.Get("SocialComposeAltDialogDescribe"),
                                                                  ContentDialogButton.Close);
            if (!postAnyway) OpenDescriptionFor(first);
            return postAnyway;
        }

        private void SetPosting(bool posting)
        {
            _posting = posting;
            PostRing.IsActive = posting;
            PostRing.Visibility = Shown(posting);
            PostLabel.Opacity = posting ? 0 : 1;
            Editor.IsReadOnly = posting;
            WarningBox.IsReadOnly = posting;
            UpdateState();
        }

        public void Clear()
        {
            Load(null, _context);
        }

        private void VisibilityItem_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(tag)) _visibility = tag;

            ApplyVisibility();
            OnEdited();
        }

        private void LocalOnlyItem_Click(object sender, RoutedEventArgs e)
        {
            _localOnly = LocalOnlyItem.IsChecked;
            ApplyVisibility();
            OnEdited();
        }

        private void ApplyVisibility()
        {
            var direct = _visibility == SocialPostService.DirectVisibility;

            PublicItem.IsChecked = _visibility == SocialNoteActionService.PublicVisibility;
            HomeItem.IsChecked = _visibility == SocialNoteActionService.HomeVisibility;
            FollowersItem.IsChecked = _visibility == SocialNoteActionService.FollowersVisibility;
            DirectItem.IsChecked = direct;
            LocalOnlyItem.IsChecked = _localOnly && !direct;
            LocalOnlyItem.IsEnabled = !direct && _context.Editing == null;

            var glyph = GlyphFor(_visibility);
            var name = LocalizedStrings.Get(NameKeyFor(_visibility));
            var label = _localOnly && !direct ? LocalizedStrings.Format("SocialComposeLocalOnlyLabelFormat", name) : name;

            VisibilityGlyph.Glyph = glyph;
            VisibilityLabel.Text = label;
            AutomationProperties.SetName(VisibilityButton, LocalizedStrings.Format("SocialComposeVisibilityAutomationFormat", label));

            ReplyVisibilityGlyph.Glyph = glyph;
            ToolTipService.SetToolTip(ReplyVisibilityGlyph, label);
            AutomationProperties.SetName(ReplyVisibilityGlyph, label);

            RecipientBox.Visibility = Shown(direct && _context.Mode != SocialComposerMode.Reply);
        }

        private static string GlyphFor(string visibility)
        {
            switch (visibility)
            {
                case SocialNoteActionService.HomeVisibility: return "";
                case SocialNoteActionService.FollowersVisibility: return "";
                case SocialPostService.DirectVisibility: return "";
                default: return "";
            }
        }

        private static string NameKeyFor(string visibility)
        {
            switch (visibility)
            {
                case SocialNoteActionService.HomeVisibility: return "SocialComposeVisibilityHomeName";
                case SocialNoteActionService.FollowersVisibility: return "SocialComposeVisibilityFollowersName";
                case SocialPostService.DirectVisibility: return "SocialComposeVisibilityDirectName";
                default: return "SocialComposeVisibilityPublicName";
            }
        }

        private void AcceptanceItem_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            _acceptance = string.IsNullOrEmpty(tag) ? null : tag;
            ApplyAcceptance();
            OnEdited();
        }

        private void ApplyAcceptance()
        {
            AcceptAllItem.IsChecked = _acceptance == null;
            AcceptLikeOnlyItem.IsChecked = _acceptance == "likeOnly";
            AcceptLikeOnlyRemoteItem.IsChecked = _acceptance == "likeOnlyForRemote";
            AcceptNonSensitiveItem.IsChecked = _acceptance == "nonSensitiveOnly";
            AcceptNonSensitiveRemoteItem.IsChecked = _acceptance == "nonSensitiveOnlyForLocalLikeOnlyForRemote";
        }

        private void WarningToggle_Click(object sender, RoutedEventArgs e)
        {
            var on = WarningToggle.IsChecked.GetValueOrDefault();
            WarningBox.Visibility = Shown(on);
            if (on) WarningBox.Focus(FocusState.Programmatic);
            OnEdited();
        }

        private void PreviewToggle_Click(object sender, RoutedEventArgs e)
        {
            var on = PreviewToggle.IsChecked.GetValueOrDefault();
            PreviewHost.Visibility = Shown(on);
            if (on) RenderPreview();
        }

        private void RenderPreview()
        {
            var warning = WarningToggle.IsChecked.GetValueOrDefault() ? (WarningBox.Text ?? "").Trim() : "";
            PreviewWarning.Text = warning;
            PreviewWarning.Visibility = Shown(warning.Length > 0);

            var segments = MfmText.Parse(SocialPostService.NormalizeText(Editor.Text), null, SocialContentService.CachedEmojiMap, null);
            MfmInlineBuilder.Fill(PreviewText, segments, false, MfmInlineBuilder.BodyEmojiSize);
        }

        private async void ShowQuote(SocialNote quote)
        {
            try
            {
                if (_quoteId == null)
                {
                    QuoteCard.Visibility = Visibility.Collapsed;
                    return;
                }

                if (quote == null)
                {
                    var result = await SocialContentService.FetchNoteAsync(_quoteId, CancellationToken.None);
                    if (result.Status != SocialApiStatus.Ok || _quoteId != result.Value.Id) return;
                    quote = result.Value;
                }

                QuoteHeader.Text = LocalizedStrings.Format("SocialComposeQuotingFormat", quote.User == null ? "" : quote.User.Handle);
                var segments = MfmText.Parse(quote.Text, quote.Emojis, null, quote.MentionHandles);
                QuoteExcerpt.Text = !string.IsNullOrWhiteSpace(quote.ContentWarning) ? quote.ContentWarning : MfmText.PlainText(segments);
                QuoteExcerpt.Visibility = Shown(QuoteExcerpt.Text.Length > 0);
                AutomationProperties.SetName(QuoteCard, QuoteHeader.Text);
                QuoteCard.Visibility = Visibility.Visible;
                UpdateState();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not show the quoted note", ex);
            }
        }

        private void RemoveQuoteButton_Click(object sender, RoutedEventArgs e)
        {
            _quoteId = null;
            QuoteCard.Visibility = Visibility.Collapsed;
            OnEdited();
            Editor.Focus(FocusState.Programmatic);
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
                AppLog.Error("SocialComposer: could not open the emoji picker", ex);
            }
        }

        private void InsertEmoji(string key)
        {
            var name = SocialReactions.CustomName(key);
            InsertText(name != null ? ":" + name + ":" : key, name != null);
        }

        private void InsertText(string text, bool spaced)
        {
            var current = Editor.Text ?? "";
            var start = Math.Min(Editor.SelectionStart, current.Length);
            var length = Math.Min(Editor.SelectionLength, current.Length - start);

            var before = current.Substring(0, start);
            var after = current.Substring(start + length);
            if (spaced && before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1])) text = " " + text;
            if (spaced && (after.Length == 0 || !char.IsWhiteSpace(after[0]))) text = text + " ";

            Editor.Text = before + text + after;
            Editor.SelectionStart = start + text.Length;
            Editor.Focus(FocusState.Programmatic);
        }

        private async void DiscardItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsDraftEmpty())
                {
                    var confirmed = await DialogService.ShowConfirmAsync(this,
                                                                         LocalizedStrings.Get("SocialComposeDiscardDialogTitle"),
                                                                         LocalizedStrings.Get("SocialComposeDiscardDialogBody"),
                                                                         LocalizedStrings.Get("SocialComposeDiscardDialogConfirm"),
                                                                         LocalizedStrings.Get("DialogCancel"),
                                                                         ContentDialogButton.Close);
                    if (!confirmed) return;
                }

                CancelUploads();
                Clear();
                Discarded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: discard failed", ex);
            }
        }

        public void Collapse()
        {
            Suggestions.Hide();
            if (IsDraftEmpty()) SetExpanded(false);
        }

        private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (e.Handled || e.Key != VirtualKey.Escape) return;
                if (_context.Mode != SocialComposerMode.Reply || Suggestions.IsOpen || !IsDraftEmpty()) return;

                SetExpanded(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialComposer: could not collapse the reply box", ex);
            }
        }

        private void Suggestions_Accepted(object sender, SocialSuggestion suggestion)
        {
            if (suggestion.User != null && _visibility == SocialPostService.DirectVisibility && _context.Mode != SocialComposerMode.Reply)
            {
                RecipientBox.Add(suggestion.User);
            }
        }
    }
}
