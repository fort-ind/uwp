using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private async Task ShowWelcomeDialogAsync()
        {
            await DialogService.RunExclusiveAsync(ShowWelcomeDialogCoreAsync);
        }

        private async Task ShowWelcomeDialogCoreAsync()
        {
            var contentTemplate = Resources["WelcomeDialogContentTemplate"] as DataTemplate;
            if (contentTemplate == null) return;

            var dialogContent = contentTemplate.LoadContent() as FrameworkElement;
            if (dialogContent == null) return;

            var dontShowCheckBox = dialogContent.FindName("WelcomeDontShowCheckBox") as CheckBox;

            ContentDialog welcomeDialog = new ContentDialog();
            welcomeDialog.Title = LocalizedStrings.Get("WelcomeDialogTitle");
            welcomeDialog.Content = dialogContent;
            welcomeDialog.CloseButtonText = LocalizedStrings.Get("WelcomeDialogDismiss");
            welcomeDialog.DefaultButton = ContentDialogButton.Close;
            DialogService.ApplyXamlRoot(welcomeDialog, this);

            await welcomeDialog.ShowAsync();

            if (dontShowCheckBox != null && dontShowCheckBox.IsChecked.GetValueOrDefault(false))
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingHideWelcomeDialog] = true;
            }
        }

        internal async Task ReplayWelcomeDialogAsync()
        {
            try
            {
                await DialogService.RunExclusiveAsync(async () =>
                {
                    ApplicationData.Current.LocalSettings.Values[AppConstants.SettingHideWelcomeDialog] = false;
                    await ShowWelcomeDialogCoreAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Reset welcome failed - {ex.Message}");
            }
        }
    }
}
