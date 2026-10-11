using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsPersonalizationPage : Page, IShellContentPage, ISettingsSubpage
    {
        private bool _loadingSettings = true;

        private bool _appearanceHandlerAttached = false;

        private string _pendingRevealSection;

        public SettingsPersonalizationPage()
        {
            this.InitializeComponent();

            LoadSettingsControls();

            Loaded += SettingsPersonalizationPage_Loaded;
            Unloaded += SettingsPersonalizationPage_Unloaded;
        }

        public Control ContentRegion
        {
            get { return PageScrollViewer; }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.NavigationMode == NavigationMode.New) _pendingRevealSection = e.Parameter as string;
        }

        private void SettingsPersonalizationPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_appearanceHandlerAttached)
                {
                    AppearanceService.Changed += OnAppearanceChanged;
                    _appearanceHandlerAttached = true;
                }

                RevealPendingSection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsPersonalizationPage: Loaded failed", ex);
            }
        }

        private void SettingsPersonalizationPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_appearanceHandlerAttached)
            {
                AppearanceService.Changed -= OnAppearanceChanged;
                _appearanceHandlerAttached = false;
            }

            AppearanceService.FlushAcrylicPersist();
        }

        private void OnAppearanceChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateTintSelection(AppearanceService.TintTag);
                UpdateAcrylicLegibilityWarnings();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsPersonalizationPage: appearance refresh failed", ex);
            }
        }

        private void LoadSettingsControls()
        {
            _loadingSettings = true;
            try
            {
                switch (AppearanceService.Theme)
                {
                    case AppConstants.ThemeLight: ThemeLightRadio.IsChecked = true; break;
                    case AppConstants.ThemeDark: ThemeDarkRadio.IsChecked = true; break;
                    default: ThemeSystemRadio.IsChecked = true; break;
                }

                TintCustomButton.ClearValue(Control.BackgroundProperty);
                TintCustomIcon.Visibility = Visibility.Visible;

                LoadAcrylicControls();

                UpdateTintSelection(AppearanceService.TintTag);

                var rememberedCustom = AppSettings.ReadString(AppConstants.SettingAppCustomTintColor, null);
                if (TintCustomIcon.IsShown() && !string.IsNullOrEmpty(rememberedCustom))
                {
                    ShowCustomSwatchColor(rememberedCustom);
                }

                LoadInkControls();
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsPersonalizationPage: LoadSettingsControls failed", ex);
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        public void RevealSection(string section)
        {
            _pendingRevealSection = section;
            if (IsLoaded) RevealPendingSection();
        }

        private void RevealPendingSection()
        {
            var section = _pendingRevealSection;
            _pendingRevealSection = null;

            switch (section)
            {
                case AppConstants.SettingsSectionAppearance:
                    SettingsSectionReveal.Reveal(PageScrollViewer, AppearanceSection);
                    break;
                case AppConstants.SettingsSectionTransparency:
                    SettingsSectionReveal.Reveal(PageScrollViewer, TransparencySection);
                    break;
                case AppConstants.SettingsSectionInk:
                    SettingsSectionReveal.Reveal(PageScrollViewer, InkSection);
                    break;
            }
        }
    }
}
