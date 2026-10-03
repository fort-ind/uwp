using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    internal sealed class SocialReactionChip
    {
        private const double ImageSize = 18;

        private const double EmojiFontSize = 16;

        private static readonly FontFamily s_emojiFont = new FontFamily("Segoe UI Emoji");

        private readonly TextBlock _emojiText;

        private readonly Image _emojiImage;

        private readonly TextBlock _countText;

        private Uri _imageUri;

        private string _name;

        public SocialReactionChip()
        {
            _emojiText = new TextBlock
            {
                FontFamily = s_emojiFont,
                FontSize = EmojiFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                IsTextScaleFactorEnabled = false
            };

            _emojiImage = new Image
            {
                Width = ImageSize,
                Height = ImageSize,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            _countText = new TextBlock
            {
                Style = SocialNoteView.StyleOf("CaptionTextBlockStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(_emojiText);
            content.Children.Add(_emojiImage);
            content.Children.Add(_countText);

            Button = new ToggleButton
            {
                Style = SocialNoteView.StyleOf("SocialReactionChipStyle"),
                Content = content,
                IsHitTestVisible = false,
                IsTabStop = false
            };
        }

        public ToggleButton Button { get; private set; }

        public string Key { get; private set; }

        public void Show(SocialNoteItem item, SocialReactionCount reaction)
        {
            Key = reaction.Key;

            var uri = item.ReactionImageUri(reaction.Key);
            if (uri != null)
            {
                if (!Equals(uri, _imageUri))
                {
                    _imageUri = uri;
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.DecodePixelType = DecodePixelType.Logical;
                    bitmap.DecodePixelHeight = (int)ImageSize;
                    bitmap.UriSource = uri;
                    _emojiImage.Source = bitmap;
                }

                _emojiImage.Visibility = Visibility.Visible;
                _emojiText.Visibility = Visibility.Collapsed;
            }
            else
            {
                var custom = SocialReactions.IsCustom(reaction.Key);
                _imageUri = null;
                _emojiImage.Source = null;
                _emojiImage.Visibility = Visibility.Collapsed;
                _emojiText.FontFamily = custom ? FontFamily.XamlAutoFontFamily : s_emojiFont;
                _emojiText.FontSize = custom ? _countText.FontSize : EmojiFontSize;
                _emojiText.Text = SocialReactions.SpokenName(reaction.Key);
                _emojiText.Visibility = Visibility.Visible;
            }

            _countText.Text = CountText.Format(reaction.Count);
            _name = SocialReactions.SpokenName(reaction.Key);

            var mine = SocialReactions.AreSame(reaction.Key, item.MyReaction);
            var spoken = LocalizedStrings.Format(mine ? "SocialReactionChipMineAutomationFormat" : "SocialReactionChipAutomationFormat",
                                                 _name, CountText.Format(reaction.Count));
            AutomationProperties.SetName(Button, spoken);
            ToolTipService.SetToolTip(Button, SocialReactions.IsRemoteCustom(reaction.Key)
                                              ? LocalizedStrings.Format("SocialReactionChipRemoteTooltipFormat", _name)
                                              : _name);
            Button.Visibility = Visibility.Visible;
        }

        public void SetAccessible(bool accessible)
        {
            AutomationProperties.SetAccessibilityView(Button, accessible ? AccessibilityView.Content : AccessibilityView.Raw);
        }

        public void Hide()
        {
            Key = null;
            Button.Visibility = Visibility.Collapsed;
        }
    }
}
