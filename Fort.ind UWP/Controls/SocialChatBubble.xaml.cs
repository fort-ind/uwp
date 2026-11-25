using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatBubble : UserControl
    {
        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register("Item", typeof(SocialChatMessageItem), typeof(SocialChatBubble),
                                        new PropertyMetadata(null, OnItemChanged));

        private const double MediaMaxWidth = 280;

        private const double MediaMaxHeight = 320;

        private const int BlurhashSize = 32;

        private const double GroupGap = 8;

        private const double InGroupGap = 2;

        private static readonly Thickness TextPadding = new Thickness(12, 8, 12, 8);

        private static readonly Thickness MediaPadding = new Thickness(4);

        private readonly List<SocialReactionChip> _chips = new List<SocialReactionChip>();

        private SocialChatMessageItem _subscribed;

        private bool _isLoaded;

        private Button _mediaButton;

        private Image _mediaImage;

        private Grid _mediaRoot;

        private Grid _playerHost;

        private Border _sensitiveVeil;

        private FontIcon _playGlyph;

        private SocialInlineVideo _video;

        private SocialAudioBar _audioBar;

        private Button _fileButton;

        private TextBlock _fileName;

        public SocialChatBubble()
        {
            this.InitializeComponent();

            Loaded += SocialChatBubble_Loaded;
            Unloaded += SocialChatBubble_Unloaded;
        }

        public SocialChatMessageItem Item
        {
            get { return (SocialChatMessageItem)GetValue(ItemProperty); }
            set { SetValue(ItemProperty, value); }
        }

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var bubble = d as SocialChatBubble;
            if (bubble == null) return;

            try
            {
                bubble.Bind(e.NewValue as SocialChatMessageItem);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not show a message", ex);
            }
        }

        private void SocialChatBubble_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            Subscribe(Item);
        }

        private void SocialChatBubble_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            Subscribe(null);

            try
            {
                if (_video != null) _video.Stop();
                SocialMediaLightbox.CloseFor(this);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not stop the media", ex);
            }
        }

        private void Subscribe(SocialChatMessageItem item)
        {
            if (ReferenceEquals(_subscribed, item)) return;

            if (_subscribed != null) _subscribed.PropertyChanged -= Item_PropertyChanged;
            _subscribed = item;
            if (_subscribed != null) _subscribed.PropertyChanged += Item_PropertyChanged;
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (!ReferenceEquals(sender, Item)) return;

                switch (e.PropertyName)
                {
                    case "Layout":
                        PaintLayout(Item);
                        PaintMeta(Item);
                        break;
                    case "State":
                        PaintMeta(Item);
                        PaintReactions(Item);
                        ShowReactButton(false);
                        break;
                    case "Message":
                        Bind(Item);
                        break;
                    case "IsRevealed":
                        PaintAttachment(Item);
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not update a message", ex);
            }
        }

        private void Bind(SocialChatMessageItem item)
        {
            Subscribe(_isLoaded ? item : null);

            if (_video != null && (item == null || !ReferenceEquals(_video.File, item.File))) _video.Stop();
            SocialMediaLightbox.CloseFor(this);

            if (item == null) return;

            VisualStateManager.GoToState(this, item.IsMine ? "MineState" : "TheirsState", false);
            ShowReactButton(false);

            MfmInlineBuilder.Fill(MessageText, item.Segments, true, MfmInlineBuilder.BodyEmojiSize);
            MessageText.Visibility = item.Segments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            PaintAttachment(item);
            PaintReactions(item);
            PaintLayout(item);
            PaintMeta(item);
        }

        private void PaintLayout(SocialChatMessageItem item)
        {
            if (item == null) return;

            DayDivider.Visibility = item.StartsDay ? Visibility.Visible : Visibility.Collapsed;
            DayText.Text = item.StartsDay ? item.DayText : "";

            RootGrid.Margin = new Thickness(0, item.StartsGroup && !item.StartsDay ? GroupGap : InGroupGap, 0, 0);

            var showAvatar = !item.IsMine && item.StartsGroup && item.Sender != null;
            AvatarButton.Visibility = showAvatar ? Visibility.Visible : Visibility.Collapsed;
            if (showAvatar)
            {
                var name = SocialNoteItem.DisplayNameOf(item.Sender);
                AvatarPicture.DisplayName = name ?? "";
                AvatarPicture.ProfilePicture = AvatarFor(item.Sender);
                AutomationProperties.SetName(AvatarButton, LocalizedStrings.Format("SocialMenuViewUserFormat", item.Sender.Handle));
                ToolTipService.SetToolTip(AvatarButton, MfmText.PlainName(name));
            }
        }

        private void PaintMeta(SocialChatMessageItem item)
        {
            if (item == null) return;

            switch (item.State)
            {
                case SocialChatSendState.Sending:
                    MetaText.Text = LocalizedStrings.Get("SocialChatSending");
                    MetaPanel.Visibility = Visibility.Visible;
                    RetryButton.Visibility = Visibility.Collapsed;
                    Bubble.Opacity = 0.7;
                    break;
                case SocialChatSendState.Failed:
                    MetaText.Text = LocalizedStrings.Format("SocialChatNotSentFormat", item.FailureText ?? "");
                    MetaPanel.Visibility = Visibility.Visible;
                    RetryButton.Visibility = Visibility.Visible;
                    Bubble.Opacity = 0.7;
                    break;
                default:
                    MetaText.Text = item.EndsGroup ? item.TimeText : "";
                    MetaPanel.Visibility = item.EndsGroup ? Visibility.Visible : Visibility.Collapsed;
                    RetryButton.Visibility = Visibility.Collapsed;
                    Bubble.Opacity = 1;
                    break;
            }
        }

        private void PaintReactions(SocialChatMessageItem item)
        {
            var reactions = item.Reactions;
            if (reactions.Count == 0)
            {
                ReactionChips.Visibility = Visibility.Collapsed;
                return;
            }

            var emojis = SocialContentService.CachedEmojiMap;
            var mine = item.CanReact;
            for (var i = 0; i < reactions.Count; i++)
            {
                if (i >= _chips.Count)
                {
                    var chip = new SocialReactionChip();
                    chip.SetAccessible(false);
                    chip.Button.Click += ReactionChip_Click;
                    _chips.Add(chip);
                    ReactionChips.Children.Add(chip.Button);
                }

                var key = reactions[i].Key;
                var shown = _chips[i];
                shown.Show(reactions[i], SocialReactions.ImageUri(key, null, emojis), mine);
                shown.Button.IsChecked = mine;
                shown.Button.IsHitTestVisible = mine;
                ToolTipService.SetToolTip(shown.Button, mine
                                                        ? LocalizedStrings.Format("SocialChatRemoveReactionFormat", SocialReactions.SpokenName(key))
                                                        : SocialReactions.SpokenName(key));
            }

            for (var i = reactions.Count; i < _chips.Count; i++)
            {
                _chips[i].Hide();
            }

            ReactionChips.Visibility = Visibility.Visible;
            ReactionChips.InvalidateMeasure();
        }

        private void PaintAttachment(SocialChatMessageItem item)
        {
            var file = item == null ? null : item.File;
            var hasText = item != null && item.Segments.Count > 0;

            if (file == null)
            {
                AttachmentHost.Visibility = Visibility.Collapsed;
                Bubble.Padding = TextPadding;
                return;
            }

            AttachmentHost.Visibility = Visibility.Visible;
            AttachmentHost.Margin = hasText ? new Thickness(0, 0, 0, 8) : new Thickness(0);

            var hidden = file.IsSensitive && !item.IsRevealed;
            switch (file.Kind)
            {
                case SocialDriveFileKind.Image:
                case SocialDriveFileKind.Gif:
                case SocialDriveFileKind.Video:
                    ShowOnly(EnsureMedia());
                    PaintMedia(file, hidden);
                    Bubble.Padding = hasText ? TextPadding : MediaPadding;
                    break;
                case SocialDriveFileKind.Audio:
                    ShowOnly(EnsureAudio());
                    _audioBar.Show(file, hidden);
                    Bubble.Padding = TextPadding;
                    break;
                default:
                    ShowOnly(EnsureFile());
                    _fileName.Text = string.IsNullOrWhiteSpace(file.Name) ? LocalizedStrings.Get("SocialChatAttachmentPreview") : file.Name;
                    AutomationProperties.SetName(_fileButton, LocalizedStrings.Format("SocialChatFileAutomationFormat", _fileName.Text));
                    Bubble.Padding = TextPadding;
                    break;
            }
        }

        private void ShowOnly(UIElement shown)
        {
            foreach (var child in AttachmentHost.Children)
            {
                child.Visibility = ReferenceEquals(child, shown) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private Grid EnsureMedia()
        {
            if (_mediaRoot != null) return _mediaRoot;

            _mediaImage = new Image { Stretch = Stretch.UniformToFill };
            AutomationProperties.SetAccessibilityView(_mediaImage, Windows.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

            _sensitiveVeil = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = LocalizedStrings.Get("SocialChatSensitiveLabel"),
                    Style = SocialNoteView.StyleOf("CaptionTextBlockStyle"),
                    Foreground = new SolidColorBrush(Colors.White),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12)
                }
            };

            _playGlyph = new FontIcon
            {
                Glyph = "",
                FontSize = 32,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsTextScaleFactorEnabled = false
            };

            var content = new Grid();
            content.Children.Add(_mediaImage);
            content.Children.Add(_playGlyph);
            content.Children.Add(_sensitiveVeil);

            _mediaButton = new Button
            {
                Style = SocialNoteView.StyleOf("SocialBareButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Content = content
            };
            _mediaButton.Click += MediaButton_Click;

            _playerHost = new Grid { Visibility = Visibility.Collapsed };

            _mediaRoot = new Grid();
            _mediaRoot.Children.Add(_mediaButton);
            _mediaRoot.Children.Add(_playerHost);
            _video = new SocialInlineVideo(_playerHost, _mediaButton);

            AttachmentHost.Children.Add(_mediaRoot);
            return _mediaRoot;
        }

        private void PaintMedia(SocialDriveFile file, bool hidden)
        {
            var width = MediaMaxWidth;
            var height = MediaMaxWidth * 3 / 4;
            if (file.Width.HasValue && file.Height.HasValue && file.Width.Value > 0)
            {
                width = Math.Min(MediaMaxWidth, file.Width.Value);
                height = width * file.Height.Value / file.Width.Value;
                if (height > MediaMaxHeight)
                {
                    height = MediaMaxHeight;
                    width = height * file.Width.Value / file.Height.Value;
                }
            }

            _mediaRoot.Width = width;
            _mediaRoot.Height = height;
            _mediaRoot.Background = BlurhashImage.CreateBrush(file.Blurhash, BlurhashSize, BlurhashSize, Stretch.UniformToFill);

            _mediaImage.Source = hidden ? null : Thumbnail(file, width);
            _sensitiveVeil.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
            _playGlyph.Visibility = !hidden && file.Kind == SocialDriveFileKind.Video ? Visibility.Visible : Visibility.Collapsed;

            var kind = LocalizedStrings.Get(file.Kind == SocialDriveFileKind.Video ? "SocialChatVideoLabel" : "SocialChatImageLabel");
            var name = hidden ? LocalizedStrings.Format("SocialChatSensitiveAutomationFormat", kind)
                     : !string.IsNullOrWhiteSpace(file.Comment) ? LocalizedStrings.Format("SocialChatMediaAltAutomationFormat", kind, file.Comment)
                     : kind;
            AutomationProperties.SetName(_mediaButton, name);
        }

        private static ImageSource Thumbnail(SocialDriveFile file, double width)
        {
            var url = file.Kind == SocialDriveFileKind.Gif ? file.Url : (file.ThumbnailUrl ?? file.Url);
            var uri = WebLauncher.TryCreateFetchUri(url);
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            if (file.Kind != SocialDriveFileKind.Gif)
            {
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = (int)Math.Ceiling(width);
            }
            RemoteImage.SetUri(bitmap, uri);
            return bitmap;
        }

        private SocialAudioBar EnsureAudio()
        {
            if (_audioBar != null) return _audioBar;

            _audioBar = new SocialAudioBar { MinWidth = 240 };
            _audioBar.RevealRequested += AudioBar_RevealRequested;
            AttachmentHost.Children.Add(_audioBar);
            return _audioBar;
        }

        private Button EnsureFile()
        {
            if (_fileButton != null) return _fileButton;

            _fileName = new TextBlock
            {
                Style = SocialNoteView.StyleOf("BodyTextBlockStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsTextScaleFactorEnabled = false
            });
            content.Children.Add(_fileName);

            _fileButton = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
            _fileButton.Click += FileButton_Click;
            AttachmentHost.Children.Add(_fileButton);
            return _fileButton;
        }

        private static ImageSource AvatarFor(SocialUser user)
        {
            var uri = WebLauncher.TryCreateFetchUri(user == null ? null : user.AvatarUrl);
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = 32;
            bitmap.DecodePixelHeight = 32;
            RemoteImage.SetUri(bitmap, uri);
            return bitmap;
        }

        private void MediaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                var file = item == null ? null : item.File;
                if (file == null) return;

                if (file.IsSensitive && !item.IsRevealed)
                {
                    item.IsRevealed = true;
                    return;
                }

                if (file.Kind == SocialDriveFileKind.Video)
                {
                    var poster = _mediaImage.Source ?? BlurhashImage.CreateBitmap(file.Blurhash, BlurhashSize, BlurhashSize);
                    _video.Play(file, poster, _mediaButton.FocusState);
                    return;
                }

                SocialMediaLightbox.Show(this, new[] { file }, 0, _mediaButton.FocusState,
                                         index => _mediaImage.Source != null ? _mediaImage : null,
                                         index => _mediaImage.Source);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not open the attachment", ex);
            }
        }

        private void AudioBar_RevealRequested(object sender, EventArgs e)
        {
            var item = Item;
            if (item != null) item.IsRevealed = true;
        }

        private async void FileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = Item == null ? null : Item.File;
                if (file != null) await WebLauncher.LaunchAsync(file.Url);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not open the file", ex);
            }
        }

        private async void AvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var person = Item == null ? null : Item.Sender;
                if (person != null) await SocialWindows.ShowUserAsync(person);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not open the profile", ex);
            }
        }

        private void ShowReactButton(bool shown)
        {
            var item = Item;
            shown = shown && item != null && item.CanReact;
            ReactButton.Opacity = shown ? 1 : 0;
            ReactButton.IsHitTestVisible = shown;
        }

        private void Bubble_PointerEntered(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ShowReactButton(true);
        }

        private void Bubble_PointerExited(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ShowReactButton(false);
        }

        private void ReactButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null) item.RequestReact(ReactButton);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not open the reactions", ex);
            }
        }

        private void ReactionChip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                var chip = _chips.FirstOrDefault(entry => ReferenceEquals(entry.Button, sender));
                if (item == null || chip == null) return;

                chip.Button.IsChecked = true;
                item.RequestUnreact(chip.Key);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not remove the reaction", ex);
            }
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = Item;
                if (item != null) item.RequestRetry();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatBubble: could not retry", ex);
            }
        }
    }
}
