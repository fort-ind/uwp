using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class BetasPage : Page, IShellContentPage
    {
        public BetasPage()
        {
            this.InitializeComponent();
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }
    }
}
