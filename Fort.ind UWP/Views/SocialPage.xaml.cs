using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage : Page, IShellContentPage
    {
        public SocialPage()
        {
            this.InitializeComponent();
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }
    }
}
