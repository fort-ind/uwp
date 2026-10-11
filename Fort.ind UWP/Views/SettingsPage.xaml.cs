using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsPage : Page, IShellContentPage
    {
        public SettingsPage()
        {
            this.InitializeComponent();

            CategoryList.ItemsSource = SettingsCategory.All;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var category = (sender as FrameworkElement)?.DataContext as SettingsCategory;
                if (category == null || Frame == null) return;

                Frame.Navigate(category.PageType);
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsPage: could not open a settings category", ex);
            }
        }
    }
}
