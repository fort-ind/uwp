using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class SettingsAccountPage
    {
        private static readonly string[] LanguageCodes =
        {
            "af", "ak", "ar", "az", "bg", "bn", "br", "ca", "cs", "cy", "da", "de", "dsb", "el", "en", "eo", "es", "et", "eu",
            "fa", "ff", "fi", "fil", "fo", "fr", "ga", "gd", "gl", "gv", "he", "hi", "hr", "hsb", "ht", "hu", "hy", "id", "is",
            "it", "ja", "kab", "kl", "km", "kn", "ko", "kw", "la", "lb", "lt", "lv", "mai", "mk", "ml", "mr", "ms", "mt", "my",
            "nb", "ne", "nl", "no", "oc", "pa", "pl", "pt", "ro", "ru", "sh", "sk", "sl", "sq", "sr", "su", "sv", "sw", "ta",
            "te", "tg", "th", "tr", "uk", "ur", "uz", "vi", "yi", "zh"
        };

        private static readonly int[] OlderNotesSeconds = { -3600, -86400, -259200, -604800, -2592000, -7776000, -31104000 };

        private static readonly string[] OlderNotesKeys =
        {
            "SocialOlderNotesOneHour", "SocialOlderNotesOneDay", "SocialOlderNotesThreeDays", "SocialOlderNotesOneWeek",
            "SocialOlderNotesOneMonth", "SocialOlderNotesThreeMonths", "SocialOlderNotesOneYear"
        };

        private const string NoneTag = "none";

        private SocialAccountSettings _account;

        private bool _extrasConfigured;

        private ToggleSwitch[] AccountToggles
        {
            get
            {
                return new[]
                {
                    PrivacyCarefulBotToggle, PrivacyNoIndexToggle, PrivacyRssToggle, PrivacyRequireSigninToggle,
                    PostingDefaultSensitiveToggle, PostingAlwaysNsfwToggle, PostingAutoSensitiveToggle
                };
            }
        }

        private string AccountFieldOf(ToggleSwitch toggle)
        {
            if (toggle == PrivacyCarefulBotToggle) return SocialAccountSettings.CarefulBotField;
            if (toggle == PrivacyNoIndexToggle) return SocialAccountSettings.NoIndexField;
            if (toggle == PrivacyRssToggle) return SocialAccountSettings.EnableRssField;
            if (toggle == PrivacyRequireSigninToggle) return SocialAccountSettings.RequireSigninField;
            if (toggle == PostingDefaultSensitiveToggle) return SocialAccountSettings.DefaultSensitiveField;
            if (toggle == PostingAlwaysNsfwToggle) return SocialAccountSettings.AlwaysMarkNsfwField;
            if (toggle == PostingAutoSensitiveToggle) return SocialAccountSettings.AutoSensitiveField;
            return null;
        }

        private string AccountFieldOf(TextBox box)
        {
            return box == PrivacyFollowedMessageBox ? SocialAccountSettings.FollowedMessageField : SocialAccountSettings.DefaultCwField;
        }

        private string StoredTextOf(TextBox box)
        {
            if (_account == null) return "";

            return (box == PrivacyFollowedMessageBox ? _account.FollowedMessage : _account.DefaultCw) ?? "";
        }

        private void ConfigureExtras()
        {
            if (_extrasConfigured) return;
            _extrasConfigured = true;

            PostingDefaultCwBox.MaxLength = AppConstants.SocialWarningMaxLength;

            MutedWordsEditor.Configure(SocialAccountSettings.MutedWordsField, true, "SocialMuteSoftHeading", "SocialMuteSoftDescription");
            HardMutedWordsEditor.Configure(SocialAccountSettings.HardMutedWordsField, true, "SocialMuteHardHeading", "SocialMuteHardDescription");
            MutedInstancesEditor.Configure(SocialAccountSettings.MutedInstancesField, false, "SocialMuteInstancesHeading", "SocialMuteInstancesDescription");

            _loadingPrivacy = true;
            try
            {
                FillLanguageCombo(null);
                FillOlderNotesCombo(PrivacyNotesFollowersOnlyCombo, null);
                FillOlderNotesCombo(PrivacyNotesHiddenCombo, null);
            }
            finally
            {
                _loadingPrivacy = false;
            }
        }

        private void LoadAccountExtras(SocialAccountSettings settings)
        {
            ConfigureExtras();
            _account = settings;

            PostingSection.Visibility = settings == null ? Visibility.Collapsed : Visibility.Visible;
            if (settings == null) return;

            _loadingPrivacy = true;
            try
            {
                foreach (var toggle in AccountToggles.Where(t => !_privacyBusy.Contains(t)))
                {
                    toggle.IsOn = settings.FlagValue(AccountFieldOf(toggle));
                }

                ShowText(PrivacyFollowedMessageBox, settings.FollowedMessage);
                ShowText(PostingDefaultCwBox, settings.DefaultCw);

                if (!_privacyBusy.Contains(PostingLanguageCombo)) FillLanguageCombo(settings.Lang);
                if (!_privacyBusy.Contains(PrivacyNotesFollowersOnlyCombo)) FillOlderNotesCombo(PrivacyNotesFollowersOnlyCombo, settings.NotesFollowersOnlyBefore);
                if (!_privacyBusy.Contains(PrivacyNotesHiddenCombo)) FillOlderNotesCombo(PrivacyNotesHiddenCombo, settings.NotesHiddenBefore);
            }
            finally
            {
                _loadingPrivacy = false;
            }

            MutedWordsEditor.Show(settings.MutedWords.ToEditorText());
            HardMutedWordsEditor.Show(settings.HardMutedWords.ToEditorText());
            MutedInstancesEditor.Show(SocialMuteListEditor.HostsToText(settings.MutedInstances));
        }

        private void ShowText(TextBox box, string value)
        {
            if (_privacyBusy.Contains(box) || box.FocusState != FocusState.Unfocused) return;

            box.Text = value ?? "";
        }

        private void FillLanguageCombo(string current)
        {
            var entries = LanguageCodes.Select(code => new KeyValuePair<string, string>(code, LanguageName(code)))
                                       .OrderBy(entry => entry.Value, StringComparer.CurrentCultureIgnoreCase)
                                       .ToList();
            if (!string.IsNullOrEmpty(current) && entries.All(entry => entry.Key != current))
            {
                entries.Add(new KeyValuePair<string, string>(current, current));
            }

            PostingLanguageCombo.Items.Clear();
            PostingLanguageCombo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.Get("PostingLanguageNone"), Tag = NoneTag });
            foreach (var entry in entries)
            {
                PostingLanguageCombo.Items.Add(new ComboBoxItem { Content = entry.Value, Tag = entry.Key });
            }

            SelectTag(PostingLanguageCombo, string.IsNullOrEmpty(current) ? NoneTag : current);
        }

        private static string LanguageName(string code)
        {
            try
            {
                var name = CultureInfo.GetCultureInfo(code).NativeName;
                return string.IsNullOrWhiteSpace(name) ? code : name;
            }
            catch (CultureNotFoundException)
            {
                return code;
            }
        }

        private static void FillOlderNotesCombo(ComboBox combo, int? current)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.Get("SocialOlderNotesNever"), Tag = NoneTag });
            for (var i = 0; i < OlderNotesSeconds.Length; i++)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = LocalizedStrings.Get(OlderNotesKeys[i]),
                    Tag = OlderNotesSeconds[i].ToString(CultureInfo.InvariantCulture)
                });
            }

            string tag = NoneTag;
            if (current.HasValue)
            {
                tag = current.Value.ToString(CultureInfo.InvariantCulture);
                if (!OlderNotesSeconds.Contains(current.Value))
                {
                    combo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.Get("SocialOlderNotesCustom"), Tag = tag });
                }
            }

            SelectTag(combo, tag);
        }

        private async void AccountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            try
            {
                if (_loadingPrivacy || toggle == null || _privacyBusy.Contains(toggle)) return;

                var field = AccountFieldOf(toggle);
                if (field == null) return;

                var value = toggle.IsOn;
                _privacyBusy.Add(toggle);
                toggle.IsEnabled = false;

                var applied = false;
                if (!value || toggle != PrivacyRequireSigninToggle || await ConfirmRequireSigninAsync())
                {
                    applied = await SocialProfileEditService.SetFlagAsync(this, field, value);
                }

                if (!applied)
                {
                    _loadingPrivacy = true;
                    toggle.IsOn = !value;
                    _loadingPrivacy = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not change an account setting", ex);
            }
            finally
            {
                if (toggle != null)
                {
                    _privacyBusy.Remove(toggle);
                    toggle.IsEnabled = true;
                }
            }
        }

        private Task<bool> ConfirmRequireSigninAsync()
        {
            return DialogService.ShowConfirmAsync(
                this,
                LocalizedStrings.Get("PrivacyRequireSigninConfirmTitle"),
                LocalizedStrings.Get("PrivacyRequireSigninConfirmBody"),
                LocalizedStrings.Get("PrivacyRequireSigninConfirmPrimary"),
                LocalizedStrings.Get("DialogCancel"),
                ContentDialogButton.Close);
        }

        private async void AccountText_LostFocus(object sender, RoutedEventArgs e)
        {
            await CommitTextAsync(sender as TextBox);
        }

        private async void AccountText_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;

            e.Handled = true;
            await CommitTextAsync(sender as TextBox);
        }

        private async Task CommitTextAsync(TextBox box)
        {
            try
            {
                if (box == null || _loadingPrivacy || _privacyBusy.Contains(box)) return;

                var text = SocialPostService.NormalizeText(box.Text).Trim();
                var stored = StoredTextOf(box);
                if (string.Equals(text, stored, StringComparison.Ordinal)) return;

                _privacyBusy.Add(box);
                box.IsEnabled = false;

                var applied = await SocialProfileEditService.SetTextAsync(this, AccountFieldOf(box), text);
                if (!applied)
                {
                    box.Text = stored;
                }
                else
                {
                    box.Text = text;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not change an account text setting", ex);
            }
            finally
            {
                if (box != null)
                {
                    _privacyBusy.Remove(box);
                    box.IsEnabled = true;
                }
            }
        }

        private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = PostingLanguageCombo;
            try
            {
                if (_loadingPrivacy || _privacyBusy.Contains(combo)) return;

                var tag = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                if (tag == null) return;

                var previous = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
                _privacyBusy.Add(combo);
                combo.IsEnabled = false;

                var value = tag == NoneTag ? JsonValue.CreateNullValue() : (IJsonValue)JsonValue.CreateStringValue(tag);
                var applied = await SocialProfileEditService.SetValueAsync(this, SocialAccountSettings.LangField, value);
                if (!applied)
                {
                    _loadingPrivacy = true;
                    combo.SelectedItem = previous;
                    _loadingPrivacy = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not change the language", ex);
            }
            finally
            {
                _privacyBusy.Remove(combo);
                combo.IsEnabled = true;
            }
        }

        private async void OlderNotesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            try
            {
                if (_loadingPrivacy || combo == null || _privacyBusy.Contains(combo)) return;

                var tag = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                if (tag == null) return;

                var field = combo == PrivacyNotesFollowersOnlyCombo
                            ? SocialAccountSettings.NotesFollowersOnlyBeforeField
                            : SocialAccountSettings.NotesHiddenBeforeField;
                var stored = combo == PrivacyNotesFollowersOnlyCombo ? _account?.NotesFollowersOnlyBefore : _account?.NotesHiddenBefore;
                var storedTag = stored.HasValue ? stored.Value.ToString(CultureInfo.InvariantCulture) : NoneTag;
                if (string.Equals(tag, storedTag, StringComparison.Ordinal)) return;

                var previous = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
                _privacyBusy.Add(combo);
                combo.IsEnabled = false;

                IJsonValue value = tag == NoneTag
                                   ? JsonValue.CreateNullValue()
                                   : JsonValue.CreateNumberValue(int.Parse(tag, CultureInfo.InvariantCulture));
                var applied = await SocialProfileEditService.SetValueAsync(this, field, value);
                if (!applied)
                {
                    _loadingPrivacy = true;
                    combo.SelectedItem = previous;
                    _loadingPrivacy = false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SettingsAccountPage: could not change an older-notes setting", ex);
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
