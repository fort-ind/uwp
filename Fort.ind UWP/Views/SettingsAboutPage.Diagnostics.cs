using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsPage
    {
        private const int DiagnosticsTapCount = 7;

        private static readonly TimeSpan DiagnosticsTapGap = TimeSpan.FromSeconds(2);

        private int _versionTaps;

        private DateTime _lastVersionTapUtc;

        private bool _copyingDiagnostics;

        private static bool DiagnosticsUnlocked { get; set; }

        private void AboutVersionText_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                if (DiagnosticsUnlocked) return;

                var now = DateTime.UtcNow;
                _versionTaps = now - _lastVersionTapUtc > DiagnosticsTapGap ? 1 : _versionTaps + 1;
                _lastVersionTapUtc = now;
                if (_versionTaps < DiagnosticsTapCount) return;

                _versionTaps = 0;
                DiagnosticsUnlocked = true;
                if (!LoadDiagnosticsSection()) return;

                DiagnosticsSection.StartBringIntoView();
                AutomationHelper.AnnounceStatus(this, LocalizedStrings.Get("DiagnosticsUnlockedAnnouncement"), "SettingsDiagnostics");
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsPage: could not show the diagnostics", ex);
            }
        }

        private void RestoreDiagnosticsSection()
        {
            if (DiagnosticsUnlocked) LoadDiagnosticsSection();
        }

        private bool LoadDiagnosticsSection()
        {
            if (DiagnosticsSection == null) FindName("DiagnosticsSection");
            return DiagnosticsSection != null;
        }

        private async void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_copyingDiagnostics) return;
            _copyingDiagnostics = true;

            try
            {
                var text = await AppLog.ReadDiagnosticsAsync();
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                ShowDiagnosticsStatus(LocalizedStrings.Get("DiagnosticsCopied"));
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsPage: could not copy the diagnostics", ex);
                ShowDiagnosticsStatus(LocalizedStrings.Get("DiagnosticsCopyFailed"));
            }
            finally
            {
                _copyingDiagnostics = false;
            }
        }

        private void ShowDiagnosticsStatus(string text)
        {
            DiagnosticsStatusText.Text = text;
            DiagnosticsStatusText.Visibility = Visibility.Visible;
            AutomationHelper.AnnounceLiveRegion(DiagnosticsStatusText);
        }
    }
}
