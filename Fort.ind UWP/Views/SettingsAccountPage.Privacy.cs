using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsAccountPage
    {
        private bool _loadingPrivacy;

        private bool _authHandlerAttached;

        private readonly HashSet<Control> _privacyBusy = new HashSet<Control>();

        private string PrivacyFieldOf(ToggleSwitch toggle)
        {
            if (toggle == PrivacyLockedToggle) return SocialPrivacySettings.LockedField;
            if (toggle == PrivacyAutoAcceptToggle) return SocialPrivacySettings.AutoAcceptFollowedField;
            if (toggle == PrivacyPublicReactionsToggle) return SocialPrivacySettings.PublicReactionsField;
            if (toggle == PrivacyHideOnlineToggle) return SocialPrivacySettings.HideOnlineStatusField;
            if (toggle == PrivacyExplorableToggle) return SocialPrivacySettings.ExplorableField;
            if (toggle == PrivacyNoCrawleToggle) return SocialPrivacySettings.NoCrawleField;
            if (toggle == PrivacyPreventAiToggle) return SocialPrivacySettings.PreventAiLearningField;
            return null;
        }

        private string PrivacyFieldOf(ComboBox combo)
        {
            if (combo == PrivacyFollowingVisibilityCombo) return SocialPrivacySettings.FollowingVisibilityField;
            if (combo == PrivacyFollowersVisibilityCombo) return SocialPrivacySettings.FollowersVisibilityField;
            if (combo == PrivacyChatScopeCombo) return SocialPrivacySettings.ChatScopeField;
            return null;
        }

        private ToggleSwitch[] PrivacyToggles
        {
            get
            {
                return new[]
                {
                    PrivacyLockedToggle, PrivacyAutoAcceptToggle, PrivacyPublicReactionsToggle, PrivacyHideOnlineToggle,
                    PrivacyExplorableToggle, PrivacyNoCrawleToggle, PrivacyPreventAiToggle
                };
            }
        }

        private ComboBox[] PrivacyCombos
        {
            get { return new[] { PrivacyFollowingVisibilityCombo, PrivacyFollowersVisibilityCombo, PrivacyChatScopeCombo }; }
        }

        private void AttachPrivacy()
        {
            if (!_authHandlerAttached)
            {
                ProfileService.AuthStateChanged += OnAuthStateChanged;
                _authHandlerAttached = true;
            }

            LoadPrivacy();
        }

        private void DetachPrivacy()
        {
            if (!_authHandlerAttached) return;

            ProfileService.AuthStateChanged -= OnAuthStateChanged;
            _authHandlerAttached = false;
        }

        private async void OnAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        LoadPrivacy();
                        LoadModeration();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SettingsAccountPage: could not show the privacy settings", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: auth state change handler failed", ex);
            }
        }

        private void LoadPrivacy()
        {
            var settings = SocialPrivacySettings.FromProfile(ProfileService.CurrentUser);
            PrivacySection.Visibility = settings == null ? Visibility.Collapsed : Visibility.Visible;
            if (settings == null) return;

            _loadingPrivacy = true;
            try
            {
                foreach (var toggle in PrivacyToggles.Where(t => !_privacyBusy.Contains(t)))
                {
                    toggle.IsOn = settings.FlagValue(PrivacyFieldOf(toggle));
                }

                foreach (var combo in PrivacyCombos.Where(c => !_privacyBusy.Contains(c)))
                {
                    SelectTag(combo, settings.ChoiceValue(PrivacyFieldOf(combo)));
                }

                UpdatePrivacyDependents();
            }
            finally
            {
                _loadingPrivacy = false;
            }
        }

        private static void SelectTag(ComboBox combo, string tag)
        {
            combo.SelectedItem = combo.Items
                                      .OfType<ComboBoxItem>()
                                      .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
        }

        private void UpdatePrivacyDependents()
        {
            PrivacyAutoAcceptToggle.IsEnabled = PrivacyLockedToggle.IsOn && !_privacyBusy.Contains(PrivacyAutoAcceptToggle);
        }

        private async void PrivacyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            try
            {
                if (toggle == PrivacyLockedToggle) UpdatePrivacyDependents();
                if (_loadingPrivacy || toggle == null || _privacyBusy.Contains(toggle)) return;

                var field = PrivacyFieldOf(toggle);
                if (field == null) return;

                var value = toggle.IsOn;
                _privacyBusy.Add(toggle);
                toggle.IsEnabled = false;

                var applied = await SocialProfileEditService.SetFlagAsync(this, field, value);
                if (!applied)
                {
                    _loadingPrivacy = true;
                    toggle.IsOn = !value;
                    _loadingPrivacy = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not change a privacy setting", ex);
            }
            finally
            {
                if (toggle != null)
                {
                    _privacyBusy.Remove(toggle);
                    toggle.IsEnabled = true;
                }

                UpdatePrivacyDependents();
            }
        }

        private async void PrivacyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            try
            {
                if (_loadingPrivacy || combo == null || _privacyBusy.Contains(combo)) return;

                var field = PrivacyFieldOf(combo);
                var value = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                if (field == null || value == null) return;

                var previous = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
                _privacyBusy.Add(combo);
                combo.IsEnabled = false;

                var applied = await SocialProfileEditService.SetChoiceAsync(this, field, value);
                if (!applied)
                {
                    _loadingPrivacy = true;
                    combo.SelectedItem = previous;
                    _loadingPrivacy = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not change a privacy choice", ex);
            }
            finally
            {
                if (combo != null)
                {
                    _privacyBusy.Remove(combo);
                    combo.IsEnabled = true;
                }
            }
        }
    }
}
