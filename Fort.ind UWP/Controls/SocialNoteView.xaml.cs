using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialNotePresentation
    {
        Row,
        Focused
    }

    public sealed partial class SocialNoteView : UserControl
    {
        private const int SingleDecodeWidth = 640;

        private const int GridDecodeWidth = 360;

        private const int BlurhashSize = 32;

        private const int VideoPosterBlurhashHeight = 18;

        private const double MaximumSingleHeight = 400;

        private const double QuoteAvatarSize = 20;

        private const int LinkThumbnailSize = 64;

        private static readonly Color ScrimColor = Color.FromArgb(0x99, 0, 0, 0);

        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register("Item", typeof(SocialNoteItem), typeof(SocialNoteView),
                                        new PropertyMetadata(null, OnItemChanged));

        public static readonly DependencyProperty PresentationProperty =
            DependencyProperty.Register("Presentation", typeof(SocialNotePresentation), typeof(SocialNoteView),
                                        new PropertyMetadata(SocialNotePresentation.Row, OnItemChanged));

        private readonly List<MediaTile> _tiles = new List<MediaTile>();

        private SocialNoteItem _subscribed;

        private bool _isLoaded;

        private int _mediaShown;

        private double _singleAspect = 9.0 / 16.0;

        public SocialNoteView()
        {
            this.InitializeComponent();

            Loaded += SocialNoteView_Loaded;
            Unloaded += SocialNoteView_Unloaded;
        }

        public SocialNoteItem Item
        {
            get { return (SocialNoteItem)GetValue(ItemProperty); }
            set { SetValue(ItemProperty, value); }
        }

        public SocialNotePresentation Presentation
        {
            get { return (SocialNotePresentation)GetValue(PresentationProperty); }
            set { SetValue(PresentationProperty, value); }
        }

        private bool IsFocused
        {
            get { return Presentation == SocialNotePresentation.Focused; }
        }

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = d as SocialNoteView;
            if (view == null) return;

            try
            {
                view.Bind(view.Item);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not show a note - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void SocialNoteView_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            Subscribe(Item);
        }

        private void SocialNoteView_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            Subscribe(null);

            try
            {
                SocialMediaLightbox.CloseFor(this);
                StopVideos();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not stop the note's media - {ex.Message}");
            }
        }

        private void Subscribe(SocialNoteItem item)
        {
            if (ReferenceEquals(_subscribed, item)) return;

            if (_subscribed != null) _subscribed.PropertyChanged -= Item_PropertyChanged;
            _subscribed = item;
            if (_subscribed != null) _subscribed.PropertyChanged += Item_PropertyChanged;
        }

        private void Bind(SocialNoteItem item)
        {
            SocialMediaLightbox.CloseFor(this);
            Subscribe(_isLoaded ? item : null);
            if (item == null) return;

            ApplyDepth(item);
            RenderContext(item);
            RenderHeader(item);
            RenderContent(item);
            RenderReactions(item);
            RenderCounts(item);
            RenderContinuation(item);
            ApplyDeleted(item);

            item.RequestLinkPreview();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                var item = sender as SocialNoteItem;
                if (item == null || !ReferenceEquals(item, Item)) return;

                switch (e.PropertyName)
                {
                    case "Note":
                        Bind(item);
                        break;
                    case "TimeText":
                        TimeTextBlock.Text = item.TimeText ?? "";
                        break;
                    case "IsContentRevealed":
                        ApplyContentWarning(item);
                        break;
                    case "IsMediaRevealed":
                        RenderMedia(item);
                        break;
                    case "LinkPreview":
                        RenderLinkCard(item);
                        break;
                    case "Reactions":
                        RenderReactions(item);
                        RenderCounts(item);
                        break;
                    case "RepliesCount":
                    case "RenoteCount":
                    case "IsRenotedByMe":
                        RenderCounts(item);
                        break;
                    case "Poll":
                    case "IsPollRevealed":
                        RenderPoll(item);
                        break;
                    case "IsDeleted":
                        ApplyDeleted(item);
                        RenderCounts(item);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not update a note - {ex.Message}");
            }
        }

        private void RenderContext(SocialNoteItem item)
        {
            PinnedLine.Visibility = item.IsPinned ? Visibility.Visible : Visibility.Collapsed;

            if (item.Renoter != null)
            {
                RenoterText.Text = LocalizedStrings.Format("SocialRenotedByFormat", item.RenoterName);
                RenoterButton.Visibility = Visibility.Visible;
            }
            else
            {
                RenoterButton.Visibility = Visibility.Collapsed;
            }

            if (item.IsReply)
            {
                ReplyToText.Text = item.ReplyToUser != null
                                   ? LocalizedStrings.Format("SocialReplyingToFormat", item.ReplyToUser.Handle)
                                   : LocalizedStrings.Get("SocialReplyingToNote");
                ReplyToButton.IsHitTestVisible = item.ReplyToUser != null;
                ReplyToButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReplyToButton.Visibility = Visibility.Collapsed;
            }

            ContextPanel.Visibility = item.IsPinned || item.Renoter != null || item.IsReply
                                      ? Visibility.Visible
                                      : Visibility.Collapsed;
        }

        private void RenderHeader(SocialNoteItem item)
        {
            AvatarPicture.DisplayName = item.AuthorName ?? "";
            AvatarPicture.ProfilePicture = item.Avatar;

            var viewLabel = LocalizedStrings.Format("SocialMenuViewUserFormat", item.AuthorHandle);
            AutomationProperties.SetName(AvatarButton, viewLabel);
            ToolTipService.SetToolTip(AvatarButton, viewLabel);

            MfmInlineBuilder.Fill(NameText, item.NameSegments, false, MfmInlineBuilder.NameEmojiSize);
            var paragraph = NameText.Blocks.Count > 0 ? NameText.Blocks[0] as Paragraph : null;
            if (paragraph != null)
            {
                var handle = new Run { Text = " " + item.AuthorHandle, FontWeight = Windows.UI.Text.FontWeights.Normal };
                BindingOperations.SetBinding(handle, TextElement.ForegroundProperty,
                                             new Binding { Source = TimeTextBlock, Path = new PropertyPath("Foreground") });
                paragraph.Inlines.Add(handle);
            }

            if (string.IsNullOrEmpty(item.VisibilityGlyph))
            {
                VisibilityIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                VisibilityIcon.Glyph = item.VisibilityGlyph;
                ToolTipService.SetToolTip(VisibilityIcon, item.VisibilityName);
                AutomationProperties.SetName(VisibilityIcon, item.VisibilityName ?? "");
                VisibilityIcon.Visibility = Visibility.Visible;
            }

            TimeTextBlock.Text = item.TimeText ?? "";
            ToolTipService.SetToolTip(TimeTextBlock, string.IsNullOrEmpty(item.TimeFull) ? null : item.TimeFull);
            TimeTextBlock.Visibility = IsFocused ? Visibility.Collapsed : Visibility.Visible;

            var edited = item.IsEdited;
            EditedText.Visibility = edited && !IsFocused ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(EditedText, edited ? LocalizedStrings.Format("SocialNoteEditedTooltipFormat", item.EditedTimeFull) : null);

            if (IsFocused)
            {
                if (FocusedDateText == null) FindName("FocusedDateText");
                FocusedDateText.Text = edited
                                       ? LocalizedStrings.Format("SocialNoteFocusedEditedDateFormat", item.TimeFull, item.EditedTimeFull)
                                       : item.TimeFull ?? "";
                FocusedDateText.Visibility = Visibility.Visible;
            }
            else if (FocusedDateText != null)
            {
                FocusedDateText.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyDepth(SocialNoteItem item)
        {
            var depth = IsFocused ? 0 : Math.Min(item.Depth, AppConstants.SocialThreadReplyDepthLimit);
            var indent = AppConstants.SocialThreadIndent;

            RowGrid.Margin = new Thickness(depth * indent, 0, 0, 0);
            if (depth == 0)
            {
                ThreadLines.Visibility = Visibility.Collapsed;
                return;
            }

            while (ThreadLines.Children.Count < depth)
            {
                var line = new Windows.UI.Xaml.Shapes.Rectangle
                {
                    Width = 2,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(ThreadLines.Children.Count * indent + indent / 2 - 1, 0, 0, 0)
                };
                BindingOperations.SetBinding(line, Windows.UI.Xaml.Shapes.Shape.FillProperty,
                                             new Binding { Source = RowGrid, Path = new PropertyPath("BorderBrush") });
                ThreadLines.Children.Add(line);
            }

            for (var i = 0; i < ThreadLines.Children.Count; i++)
            {
                ThreadLines.Children[i].Visibility = i < depth ? Visibility.Visible : Visibility.Collapsed;
            }

            ThreadLines.Width = depth * indent;
            ThreadLines.Visibility = Visibility.Visible;
        }

        private void RenderContinuation(SocialNoteItem item)
        {
            var label = IsFocused || item.IsDeleted
                        ? null
                        : item.ContinueThreadCount > 0
                          ? LocalizedStrings.Format(item.ContinueThreadCount == 1 ? "SocialThreadMoreReplyOne" : "SocialThreadMoreRepliesFormat",
                                                    CountText.Format(item.ContinueThreadCount))
                          : item.ContinuesDeeper ? LocalizedStrings.Get("SocialThreadContinue") : null;

            if (label == null)
            {
                if (ContinueThreadButton != null) ContinueThreadButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (ContinueThreadButton == null) FindName("ContinueThreadButton");
            ContinueThreadButton.Content = label;
            ContinueThreadButton.Visibility = Visibility.Visible;
        }

        private void ApplyDeleted(SocialNoteItem item)
        {
            var deleted = item.IsDeleted;
            DeletedLine.Visibility = deleted ? Visibility.Visible : Visibility.Collapsed;

            if (deleted)
            {
                ContentWarningPanel.Visibility = Visibility.Collapsed;
                ContentPanel.Visibility = Visibility.Collapsed;
                ActionBar.Visibility = Visibility.Collapsed;
                if (ReactionChips != null) ReactionChips.Visibility = Visibility.Collapsed;
                if (ContinueThreadButton != null) ContinueThreadButton.Visibility = Visibility.Collapsed;
                SocialMediaLightbox.CloseFor(this);
                StopVideos();
                return;
            }

            ActionBar.Visibility = Visibility.Visible;
            ContentWarningPanel.Visibility = item.HasContentWarning ? Visibility.Visible : Visibility.Collapsed;
            ApplyContentWarning(item);
        }

        internal static Style StyleOf(string key)
        {
            try
            {
                object value;
                return Application.Current.Resources.TryGetValue(key, out value) ? value as Style : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: style {key} unavailable - {ex.Message}");
                return null;
            }
        }

        private void RenderContent(SocialNoteItem item)
        {
            ContentWarningText.Text = item.ContentWarning ?? "";
            ContentWarningPanel.Visibility = item.HasContentWarning ? Visibility.Visible : Visibility.Collapsed;

            var bodyStyle = StyleOf(IsFocused ? "SocialFocusedBodyRichTextBlockStyle" : "BodyRichTextBlockStyle");
            if (bodyStyle != null && !ReferenceEquals(BodyText.Style, bodyStyle)) BodyText.Style = bodyStyle;

            if (item.BodySegments.Count > 0)
            {
                MfmInlineBuilder.Fill(BodyText, item.BodySegments, true, MfmInlineBuilder.BodyEmojiSize);
                BodyText.Visibility = Visibility.Visible;
            }
            else
            {
                BodyText.Blocks.Clear();
                BodyText.Visibility = Visibility.Collapsed;
            }

            HiddenLine.Visibility = item.IsHidden ? Visibility.Visible : Visibility.Collapsed;

            RenderPoll(item);
            RenderMedia(item);
            RenderLinkCard(item);
            RenderQuote(item);
            ApplyContentWarning(item);
        }

        private void ApplyContentWarning(SocialNoteItem item)
        {
            if (item.IsDeleted) return;

            var revealed = !item.HasContentWarning || item.IsContentRevealed;
            ContentPanel.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;

            if (item.HasContentWarning)
            {
                ContentWarningButton.Content = LocalizedStrings.Get(item.IsContentRevealed ? "SocialHideContent" : "SocialShowContent");
                ContentWarningButton.IsExpanded = item.IsContentRevealed;
            }
        }

        private void RenderMedia(SocialNoteItem item)
        {
            var files = item.IsHidden ? null : item.Media;
            if (files == null || files.Count == 0)
            {
                _mediaShown = 0;
                if (MediaGrid != null)
                {
                    MediaGrid.Visibility = Visibility.Collapsed;
                    foreach (var tile in _tiles) tile.Clear();
                }
                return;
            }

            if (MediaGrid == null) FindName("MediaGrid");
            EnsureTiles();

            _mediaShown = Math.Min(files.Count, AppConstants.SocialMediaGridLimit);
            for (var i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                if (i >= _mediaShown)
                {
                    tile.Clear();
                    tile.Button.Visibility = Visibility.Collapsed;
                    continue;
                }

                tile.Show(files[i], i, _mediaShown, files.Count, item.IsMediaRevealed);
            }

            LayoutTiles(_mediaShown);

            var first = files[0];
            _singleAspect = first.Width.HasValue && first.Height.HasValue
                            ? (double)first.Height.Value / first.Width.Value
                            : 9.0 / 16.0;

            MediaGrid.Visibility = Visibility.Visible;
            UpdateMediaHeight(MediaGrid.ActualWidth);
        }

        private void EnsureTiles()
        {
            if (_tiles.Count > 0) return;

            for (var i = 0; i < AppConstants.SocialMediaGridLimit; i++)
            {
                var tile = new MediaTile(TilePlate);
                tile.Button.Click += MediaTile_Click;
                MediaGrid.Children.Add(tile.Button);
                MediaGrid.Children.Add(tile.PlayerHost);
                _tiles.Add(tile);
            }
        }

        private void StopVideos()
        {
            foreach (var tile in _tiles)
            {
                tile.StopVideo();
            }
        }

        internal UIElement MediaAnchorFor(SocialNoteItem item, int fileIndex)
        {
            if (!_isLoaded || item == null || !ReferenceEquals(item, Item)) return null;
            if (fileIndex < 0 || fileIndex >= _mediaShown || fileIndex >= _tiles.Count) return null;

            return _tiles[fileIndex].AnchorFor(fileIndex);
        }

        internal ImageSource MediaPlaceholderFor(SocialNoteItem item, int fileIndex)
        {
            if (item == null || !ReferenceEquals(item, Item)) return null;
            if (fileIndex < 0 || fileIndex >= _mediaShown || fileIndex >= _tiles.Count) return null;

            return _tiles[fileIndex].ThumbnailFor(fileIndex);
        }

        private Binding TilePlate()
        {
            return new Binding { Source = TilePlateSource, Path = new PropertyPath("Background") };
        }

        private void LayoutTiles(int shown)
        {
            for (var i = 0; i < shown; i++)
            {
                var button = _tiles[i].Button;
                int row = 0, column = 0, rowSpan = 1, columnSpan = 1;

                switch (shown)
                {
                    case 1:
                        rowSpan = 2;
                        columnSpan = 2;
                        break;
                    case 2:
                        column = i;
                        rowSpan = 2;
                        break;
                    case 3:
                        if (i == 0) { row = 0; column = 0; }
                        else if (i == 1) { row = 0; column = 1; rowSpan = 2; }
                        else { row = 1; column = 0; }
                        break;
                    default:
                        row = i / 2;
                        column = i % 2;
                        break;
                }

                Place(button, row, column, rowSpan, columnSpan);
                Place(_tiles[i].PlayerHost, row, column, rowSpan, columnSpan);
            }
        }

        private static void Place(FrameworkElement element, int row, int column, int rowSpan, int columnSpan)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetRowSpan(element, rowSpan);
            Grid.SetColumnSpan(element, columnSpan);
        }

        private void MediaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                UpdateMediaHeight(e.NewSize.Width);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: media layout failed - {ex.Message}");
            }
        }

        private void UpdateMediaHeight(double width)
        {
            if (MediaGrid == null || width <= 0 || _mediaShown == 0) return;

            double height;
            if (_mediaShown == 1)
            {
                height = width * _singleAspect;
                height = Math.Max(width / 2, Math.Min(height, Math.Min(width * 1.25, MaximumSingleHeight)));
            }
            else
            {
                height = width * 9.0 / 16.0;
            }

            height = Math.Round(height);
            if (double.IsNaN(MediaGrid.Height) || Math.Abs(MediaGrid.Height - height) >= 1)
            {
                MediaGrid.Height = height;
            }
        }

        private async void MediaTile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                var button = sender as Control;
                if (item == null || button == null || !(button.Tag is int)) return;

                var index = (int)button.Tag;
                if (index < 0 || index >= item.Media.Count) return;

                var file = item.Media[index];
                if (file.IsSensitive && !item.IsMediaRevealed)
                {
                    item.IsMediaRevealed = true;
                    return;
                }

                switch (file.Kind)
                {
                    case SocialDriveFileKind.Image:
                    case SocialDriveFileKind.Gif:
                        SocialMediaLightbox.Show(this, item, index, button.FocusState);
                        break;
                    case SocialDriveFileKind.Video:
                        if (index < _tiles.Count) _tiles[index].PlayVideo(file, button.FocusState);
                        break;
                    default:
                        await WebLauncher.LaunchAsync(file.Url);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the attachment - {ex.Message}");
            }
        }

        private void RenderLinkCard(SocialNoteItem item)
        {
            var preview = item.IsHidden ? null : item.LinkPreview;
            if (preview == null)
            {
                if (LinkCardButton != null) LinkCardButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (LinkCardButton == null) FindName("LinkCardButton");

            var site = string.IsNullOrWhiteSpace(preview.SiteName) ? HostOf(preview.Url) : preview.SiteName;

            LinkTitleText.Text = preview.Title;
            LinkDescriptionText.Text = preview.Description ?? "";
            LinkDescriptionText.Visibility = string.IsNullOrWhiteSpace(preview.Description) ? Visibility.Collapsed : Visibility.Visible;
            LinkSiteText.Text = site ?? "";

            var thumbnail = preview.IsSensitive ? null : WebLauncher.TryCreateFetchUri(preview.ThumbnailUrl);
            if (thumbnail != null)
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = LinkThumbnailSize;
                bitmap.UriSource = thumbnail;
                LinkThumbImage.Source = bitmap;
                LinkThumbHost.Visibility = Visibility.Visible;
            }
            else
            {
                LinkThumbImage.Source = null;
                LinkThumbHost.Visibility = Visibility.Collapsed;
            }

            AutomationProperties.SetName(LinkCardButton, LocalizedStrings.Format("SocialLinkCardAutomationFormat", preview.Title, site ?? ""));
            ToolTipService.SetToolTip(LinkCardButton, preview.Url);
            LinkCardButton.Visibility = Visibility.Visible;
        }

        private static string HostOf(string url)
        {
            var uri = WebLauncher.TryCreateWebUri(url);
            return uri == null ? null : uri.Host;
        }

        private void RenderQuote(SocialNoteItem item)
        {
            var quote = item.Quote;
            if (quote == null)
            {
                if (QuoteButton != null) QuoteButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (QuoteButton == null) FindName("QuoteButton");

            var name = SocialNoteItem.DisplayNameOf(quote.User);
            QuoteAvatar.DisplayName = name;
            QuoteAvatar.ProfilePicture = SmallAvatar(quote.User);

            MfmInlineBuilder.Fill(QuoteNameText, item.QuoteNameSegments, false, MfmInlineBuilder.NameEmojiSize);
            QuoteTimeText.Text = item.QuoteTimeText ?? "";

            MfmInlineBuilder.Fill(QuoteBodyText, item.QuoteBodySegments, false, MfmInlineBuilder.BodyEmojiSize);
            QuoteBodyText.Visibility = item.QuoteBodySegments != null && item.QuoteBodySegments.Count > 0
                                       ? Visibility.Visible
                                       : Visibility.Collapsed;

            var attachments = quote.Files.Count;
            QuoteAttachmentsText.Text = attachments == 1
                                        ? LocalizedStrings.Get("SocialQuoteAttachmentsOne")
                                        : LocalizedStrings.Format("SocialQuoteAttachmentsFormat", attachments);
            QuoteAttachmentsText.Visibility = attachments > 0 ? Visibility.Visible : Visibility.Collapsed;

            AutomationProperties.SetName(QuoteButton,
                                         LocalizedStrings.Format("SocialQuoteAutomationFormat",
                                                                 quote.User == null ? name : quote.User.Handle,
                                                                 MfmText.PlainText(item.QuoteBodySegments)));
            QuoteButton.Visibility = Visibility.Visible;
        }

        private static ImageSource SmallAvatar(SocialUser user)
        {
            var uri = user == null ? null : WebLauncher.TryCreateFetchUri(user.AvatarUrl);
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = (int)QuoteAvatarSize;
            bitmap.DecodePixelHeight = (int)QuoteAvatarSize;
            bitmap.UriSource = uri;
            return bitmap;
        }

        private async void AvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null) await SocialWindows.ShowUserAsync(item.Author);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the author's profile - {ex.Message}");
            }
        }

        private async void RenoterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null && item.Renoter != null) await SocialWindows.ShowUserAsync(item.Renoter);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the renoter's profile - {ex.Message}");
            }
        }

        private async void ReplyToButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null && item.ReplyToUser != null) await SocialWindows.ShowUserAsync(item.ReplyToUser);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the replied-to profile - {ex.Message}");
            }
        }

        private void ContentWarningButton_Click(object sender, RoutedEventArgs e)
        {
            var item = Item;
            if (item != null) item.IsContentRevealed = !item.IsContentRevealed;
        }

        private async void LinkCardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                var preview = item == null ? null : item.LinkPreview;
                if (preview != null) await WebLauncher.LaunchAsync(preview.Url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the link - {ex.Message}");
            }
        }

        private async void QuoteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item == null || item.Quote == null) return;

                if (!SocialThreads.Open(this, item.Quote, item.Quote.Id, false, SocialThreadTab.Replies))
                {
                    await WebLauncher.LaunchAsync(SocialLinks.NoteUrl(item.Quote.Id));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the quoted note - {ex.Message}");
            }
        }

        private void ContinueThreadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null) SocialThreads.Open(this, item.Note, item.Note.Id, false, SocialThreadTab.Replies);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not continue the thread - {ex.Message}");
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var flyout = SocialMenus.Build(Item, MoreButton);
                SocialMenus.ShowAt(flyout, MoreButton, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteView: could not open the note menu - {ex.Message}");
            }
        }

        private sealed class MediaTile
        {
            private readonly Image _placeholder;

            private readonly Image _thumbnail;

            private readonly Grid _sensitive;

            private readonly Border _gifBadge;

            private readonly Grid _play;

            private readonly StackPanel _fileLabel;

            private readonly TextBlock _fileName;

            private readonly Border _more;

            private readonly TextBlock _moreText;

            private readonly SocialInlineVideo _video;

            public MediaTile(Func<Binding> plate)
            {
                var root = new Grid();
                BindingOperations.SetBinding(root, Panel.BackgroundProperty, plate());

                _placeholder = new Image { Stretch = Stretch.UniformToFill };
                _thumbnail = new Image { Stretch = Stretch.UniformToFill };
                root.Children.Add(_placeholder);
                root.Children.Add(_thumbnail);

                _fileName = new TextBlock
                {
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                _fileName.Style = StyleOf("CaptionTextBlockStyle");
                _fileLabel = new StackPanel
                {
                    Spacing = 6,
                    Margin = new Thickness(8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                _fileLabel.Children.Add(new FontIcon { Glyph = "\uE723", FontSize = 24, IsTextScaleFactorEnabled = false });
                _fileLabel.Children.Add(_fileName);
                root.Children.Add(_fileLabel);

                _gifBadge = new Border
                {
                    Background = new SolidColorBrush(ScrimColor),
                    Padding = new Thickness(6, 1, 6, 2),
                    Margin = new Thickness(6),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Visibility = Visibility.Collapsed,
                    Child = WhiteText(LocalizedStrings.Get("SocialMediaGifBadge"), "CaptionTextBlockStyle")
                };
                root.Children.Add(_gifBadge);

                _play = new Grid
                {
                    Width = 44,
                    Height = 44,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                _play.Children.Add(new Windows.UI.Xaml.Shapes.Ellipse { Fill = new SolidColorBrush(ScrimColor) });
                _play.Children.Add(new FontIcon
                {
                    Glyph = "\uE768",
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Colors.White),
                    IsTextScaleFactorEnabled = false
                });
                root.Children.Add(_play);

                _sensitive = new Grid
                {
                    Background = new SolidColorBrush(ScrimColor),
                    Visibility = Visibility.Collapsed
                };
                var sensitiveLabel = new StackPanel
                {
                    Spacing = 2,
                    Margin = new Thickness(8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sensitiveLabel.Children.Add(new FontIcon
                {
                    Glyph = "\uE7B3",
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Colors.White),
                    IsTextScaleFactorEnabled = false,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                sensitiveLabel.Children.Add(WhiteText(LocalizedStrings.Get("SocialSensitiveMedia"), "BaseTextBlockStyle"));
                sensitiveLabel.Children.Add(WhiteText(LocalizedStrings.Get("SocialSensitiveMediaHint"), "CaptionTextBlockStyle"));
                _sensitive.Children.Add(sensitiveLabel);
                root.Children.Add(_sensitive);

                _moreText = WhiteText("", "SubtitleTextBlockStyle");
                _more = new Border
                {
                    Background = new SolidColorBrush(ScrimColor),
                    Visibility = Visibility.Collapsed,
                    Child = _moreText
                };
                _moreText.HorizontalAlignment = HorizontalAlignment.Center;
                _moreText.VerticalAlignment = VerticalAlignment.Center;
                root.Children.Add(_more);

                Button = new Button
                {
                    Style = StyleOf("SocialBareButtonStyle"),
                    Margin = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Content = root,
                    Visibility = Visibility.Collapsed
                };

                PlayerHost = new Grid
                {
                    Margin = new Thickness(2),
                    Visibility = Visibility.Collapsed
                };
                BindingOperations.SetBinding(PlayerHost, Panel.BackgroundProperty, plate());

                _video = new SocialInlineVideo(PlayerHost, Button);
            }

            public Button Button { get; private set; }

            public Grid PlayerHost { get; private set; }

            public UIElement AnchorFor(int index)
            {
                if (!(Button.Tag is int) || (int)Button.Tag != index || !Button.IsShown()) return null;
                if (_sensitive.IsShown()) return null;

                if (_thumbnail.Source != null) return _thumbnail;
                return _placeholder.Source != null ? _placeholder : null;
            }

            public ImageSource ThumbnailFor(int index)
            {
                if (!(Button.Tag is int) || (int)Button.Tag != index) return null;

                return _thumbnail.Source;
            }

            public void PlayVideo(SocialDriveFile file, FocusState focusState)
            {
                var poster = _thumbnail.Source ?? BlurhashImage.CreateBitmap(file.Blurhash, BlurhashSize, VideoPosterBlurhashHeight);
                _video.Play(file, poster, focusState);
            }

            public void StopVideo()
            {
                _video.Stop();
            }

            public void Clear()
            {
                StopVideo();
                _placeholder.Source = null;
                _thumbnail.Source = null;
                Button.Tag = null;
            }

            public void Show(SocialDriveFile file, int index, int shown, int total, bool revealed)
            {
                if (!ReferenceEquals(_video.File, file)) StopVideo();

                Button.Tag = index;

                var hidden = file.IsSensitive && !revealed;
                var visual = file.IsVisual;

                _placeholder.Source = visual ? BlurhashImage.CreateBitmap(file.Blurhash, BlurhashSize, BlurhashSize) : null;
                _thumbnail.Source = hidden || !visual ? null : Thumbnail(file, shown == 1 ? SingleDecodeWidth : GridDecodeWidth);

                _sensitive.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
                _gifBadge.Visibility = !hidden && file.Kind == SocialDriveFileKind.Gif ? Visibility.Visible : Visibility.Collapsed;
                _play.Visibility = !hidden && file.Kind == SocialDriveFileKind.Video ? Visibility.Visible : Visibility.Collapsed;

                _fileLabel.Visibility = visual ? Visibility.Collapsed : Visibility.Visible;
                _fileName.Text = visual ? "" : (file.Name ?? "");

                var extra = total - shown;
                var isLastSlot = index == shown - 1 && extra > 0;
                _more.Visibility = isLastSlot && !hidden ? Visibility.Visible : Visibility.Collapsed;
                _moreText.Text = isLastSlot ? LocalizedStrings.Format("SocialMediaMoreFormat", extra) : "";

                var kind = LocalizedStrings.Get(KindKey(file.Kind));
                string name;
                if (hidden)
                {
                    name = LocalizedStrings.Format("SocialMediaSensitiveAutomationFormat", kind);
                }
                else if (!string.IsNullOrWhiteSpace(file.Comment))
                {
                    name = LocalizedStrings.Format("SocialMediaTileAltAutomationFormat", kind, index + 1, total, file.Comment);
                }
                else
                {
                    name = LocalizedStrings.Format("SocialMediaTileAutomationFormat", kind, index + 1, total);
                }

                AutomationProperties.SetName(Button, name);
                ToolTipService.SetToolTip(Button, hidden || string.IsNullOrWhiteSpace(file.Comment) ? null : file.Comment);
                if (_video.File == null) Button.Visibility = Visibility.Visible;
            }

            private static ImageSource Thumbnail(SocialDriveFile file, int decodeWidth)
            {
                string url;
                switch (file.Kind)
                {
                    case SocialDriveFileKind.Image:
                    case SocialDriveFileKind.Gif:
                        url = string.IsNullOrEmpty(file.ThumbnailUrl) ? file.Url : file.ThumbnailUrl;
                        break;
                    default:
                        url = file.ThumbnailUrl;
                        break;
                }

                var uri = WebLauncher.TryCreateFetchUri(url);
                if (uri == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.UriSource = uri;
                return bitmap;
            }

            private static string KindKey(SocialDriveFileKind kind)
            {
                switch (kind)
                {
                    case SocialDriveFileKind.Image: return "SocialMediaImageAutomation";
                    case SocialDriveFileKind.Gif: return "SocialMediaGifAutomation";
                    case SocialDriveFileKind.Video: return "SocialMediaVideoAutomation";
                    default: return "SocialMediaFileAutomation";
                }
            }

            private static TextBlock WhiteText(string text, string styleKey)
            {
                var block = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                block.Style = StyleOf(styleKey);
                return block;
            }
        }
    }
}
