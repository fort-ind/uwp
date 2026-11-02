using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsNotificationsPage
    {
        private static readonly string[] FilterRules =
        {
            SocialNotificationRules.All, SocialNotificationRules.Following, SocialNotificationRules.Follower,
            SocialNotificationRules.MutualFollow, SocialNotificationRules.FollowingOrFollower, SocialNotificationRules.Never
        };

        private readonly List<ComboBox> _filterCombos = new List<ComboBox>();

        private readonly HashSet<ComboBox> _filterBusy = new HashSet<ComboBox>();

        private SocialAccountSettings _filterSettings;

        private bool _loadingFilters;

        private bool _filtersAttached;

        private void AttachFilters()
        {
            if (!_filtersAttached)
            {
                ProfileService.AuthStateChanged += OnFilterAuthStateChanged;
                _filtersAttached = true;
            }

            LoadFilters();
        }

        private void DetachFilters()
        {
            if (!_filtersAttached) return;

            ProfileService.AuthStateChanged -= OnFilterAuthStateChanged;
            _filtersAttached = false;
        }

        private async void OnFilterAuthStateChanged(object sender, bool isLoggedIn)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        LoadFilters();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SettingsNotificationsPage: could not show the notification filters", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: auth state change handler failed", ex);
            }
        }

        private void BuildFilterRows()
        {
            if (_filterCombos.Count > 0) return;

            foreach (var type in SocialNotificationRules.Types)
            {
                var combo = new ComboBox
                {
                    Header = LocalizedStrings.Get("SocialNotificationFilterType_" + type),
                    Tag = type,
                    MinWidth = 200,
                    Margin = new Thickness(0, 14, 0, 0)
                };
                AutomationProperties.SetName(combo, combo.Header as string);
                combo.SelectionChanged += FilterCombo_SelectionChanged;
                _filterCombos.Add(combo);
                NotificationFilterRows.Children.Add(combo);
            }
        }

        private void LoadFilters()
        {
            var settings = SocialAccountSettings.FromProfile(ProfileService.CurrentUser);
            _filterSettings = settings;
            NotificationFiltersSection.Visibility = settings == null ? Visibility.Collapsed : Visibility.Visible;
            if (settings == null) return;

            BuildFilterRows();
            _loadingFilters = true;
            try
            {
                foreach (var combo in _filterCombos.Where(c => !_filterBusy.Contains(c)))
                {
                    FillFilterCombo(combo, settings.NotificationRule((string)combo.Tag));
                }
            }
            finally
            {
                _loadingFilters = false;
            }
        }

        private static void FillFilterCombo(ComboBox combo, string current)
        {
            combo.Items.Clear();
            foreach (var rule in FilterRules)
            {
                combo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.Get("SocialNotificationRule_" + rule), Tag = rule });
            }

            if (!FilterRules.Contains(current))
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = LocalizedStrings.Get("SocialNotificationRule_" + SocialNotificationRules.List),
                    Tag = current
                });
            }

            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
                                      .FirstOrDefault(item => string.Equals(item.Tag as string, current, StringComparison.Ordinal));
        }

        private async void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            try
            {
                if (_loadingFilters || combo == null || _filterBusy.Contains(combo) || _filterSettings == null) return;

                var rule = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                var type = combo.Tag as string;
                if (rule == null || type == null) return;
                if (string.Equals(rule, _filterSettings.NotificationRule(type), StringComparison.Ordinal)) return;

                var previous = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
                _filterBusy.Add(combo);
                combo.IsEnabled = false;

                var config = _filterSettings.NotificationConfigWith(type, rule);
                var applied = await SocialProfileEditService.SetValueAsync(this, SocialAccountSettings.NotificationConfigField, config);
                if (!applied)
                {
                    _loadingFilters = true;
                    combo.SelectedItem = previous;
                    _loadingFilters = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsNotificationsPage: could not change a notification filter", ex);
            }
            finally
            {
                if (combo != null)
                {
                    _filterBusy.Remove(combo);
                    combo.IsEnabled = true;
                }
            }
        }
    }
}
