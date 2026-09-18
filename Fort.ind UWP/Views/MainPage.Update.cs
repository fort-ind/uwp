using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage : Page
    {
        private Version _offeredUpdate;

        private async void OfferUpdateIfAvailable()
        {
            try
            {
                var version = await UpdateService.GetUpdateToOfferAsync();
                if (version == null) return;

                _offeredUpdate = version;

                var isMajor = UpdateService.IsMajorUpgrade(version);
                var displayVersion = UpdateService.FormatForDisplay(version);

                UpdateInfoBar.Title = LocalizedStrings.Get(isMajor ? "UpdateMajorTitle" : "UpdateInfoBar/Title");
                UpdateInfoBar.Message = LocalizedStrings.Format(isMajor ? "UpdateMajorMessageFormat" : "UpdateAvailableMessageFormat",
                                                                displayVersion);

                var state = isMajor ? "MajorUpdateState" : "RegularUpdateState";
                if (!VisualStateManager.GoToState(this, state, false))
                {
                    Debug.WriteLine($"MainPage: could not enter {state}");
                }

                UpdateInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: update check failed - {ex.Message}");
            }
        }

        private void UpdateInfoBar_CloseButtonClick(Microsoft.UI.Xaml.Controls.InfoBar sender, object args)
        {
            UpdateService.Dismiss(_offeredUpdate);
        }

        private async void UpdateDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WebLauncher.LaunchAsync(UpdateService.LatestReleasePageUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not open the release page - {ex.Message}");
            }
        }
    }
}
