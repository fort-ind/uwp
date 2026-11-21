using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialQuoteBlock : UserControl
    {
        public SocialQuoteBlock()
        {
            this.InitializeComponent();
        }

        public void Load(IReadOnlyList<MfmSegment> children, RichTextBlock owner, bool interactive, double emojiSize)
        {
            Body.FontSize = owner.FontSize;
            Body.FontFamily = owner.FontFamily;
            MfmInlineBuilder.Fill(Body, children, interactive, emojiSize);
        }
    }
}
