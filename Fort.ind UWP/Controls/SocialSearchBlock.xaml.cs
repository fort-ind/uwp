using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialSearchBlock : UserControl
    {
        private string _query;

        public SocialSearchBlock()
        {
            this.InitializeComponent();
        }

        public void Load(MfmSegment segment)
        {
            _query = segment.Query ?? "";
            QueryText.Text = _query;
            AutomationProperties.SetName(SearchButton, LocalizedStrings.Format("SocialMfmSearchButtonNameFormat", _query));
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_query)) return;
                if (SocialThreads.OpenSearch(this, _query)) return;

                await WebLauncher.LaunchAsync(SocialLinks.SearchUrl(_query));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialSearchBlock: running the search failed", ex);
            }
        }
    }
}
