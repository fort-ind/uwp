using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class BetasPage : Page, IShellContentPage
    {
        private bool _loadingLabs;

        public BetasPage()
        {
            this.InitializeComponent();

            Loaded += BetasPage_Loaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        private void BetasPage_Loaded(object sender, RoutedEventArgs e)
        {
            _loadingLabs = true;
            try
            {
                LabChatToggle.IsOn = LabsService.ChatEnabled;
            }
            catch (Exception ex)
            {
                AppLog.Error("BetasPage: could not load the labs state", ex);
            }
            finally
            {
                _loadingLabs = false;
            }
        }

        private void LabChatToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingLabs) return;

            try
            {
                LabsService.SetChatEnabled(LabChatToggle.IsOn);
            }
            catch (Exception ex)
            {
                AppLog.Error("BetasPage: could not apply the chat lab", ex);
            }
        }
    }
}
