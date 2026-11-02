using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialMuteListEditor : UserControl
    {
        private static readonly Regex s_host = new Regex("^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$", RegexOptions.CultureInvariant);

        private string _field;

        private bool _words;

        private string _baseline = "";

        private bool _busy;

        private bool _loading;

        public SocialMuteListEditor()
        {
            InitializeComponent();
        }

        public void Configure(string field, bool words, string headerKey, string descriptionKey)
        {
            _field = field;
            _words = words;
            HeaderText.Text = LocalizedStrings.Get(headerKey);
            DescriptionText.Text = LocalizedStrings.Get(descriptionKey);
            AutomationProperties.SetName(Box, HeaderText.Text);
        }

        public void Show(string serverText)
        {
            var text = serverText ?? "";
            var wasClean = !IsDirty;
            _baseline = text;
            if (_busy) return;

            if (wasClean)
            {
                _loading = true;
                try
                {
                    Box.Text = text;
                }
                finally
                {
                    _loading = false;
                }

                ErrorText.Visibility = Visibility.Collapsed;
            }

            UpdateButtons();
        }

        private bool IsDirty
        {
            get { return !string.Equals(Canonical(Box.Text), Canonical(_baseline), StringComparison.Ordinal); }
        }

        private string Canonical(string text)
        {
            if (_words)
            {
                string bad;
                var set = SocialMuteWordSet.FromEditorText(text, out bad);
                return set == null ? text : set.ToEditorText();
            }

            string badHost;
            var hosts = ParseHosts(text, out badHost);
            return hosts == null ? text : string.Join("\r", hosts);
        }

        private void UpdateButtons()
        {
            var dirty = IsDirty;
            SaveButton.IsEnabled = dirty && !_busy;
            RevertButton.IsEnabled = dirty && !_busy;
        }

        private void Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            ErrorText.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }

        private void RevertButton_Click(object sender, RoutedEventArgs e)
        {
            Box.Text = _baseline;
            ErrorText.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_busy) return;

                IJsonValue value;
                string canonical;
                if (!TryBuild(out value, out canonical)) return;

                _busy = true;
                SaveButton.IsEnabled = false;
                RevertButton.IsEnabled = false;
                Box.IsReadOnly = true;

                var applied = await SocialProfileEditService.SetValueAsync(this, _field, value);
                if (applied)
                {
                    _baseline = canonical;
                    _loading = true;
                    try
                    {
                        Box.Text = canonical;
                    }
                    finally
                    {
                        _loading = false;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMuteListEditor: could not save " + _field, ex);
            }
            finally
            {
                _busy = false;
                Box.IsReadOnly = false;
                UpdateButtons();
            }
        }

        private bool TryBuild(out IJsonValue value, out string canonical)
        {
            value = null;
            canonical = null;

            if (_words)
            {
                string bad;
                var set = SocialMuteWordSet.FromEditorText(Box.Text, out bad);
                if (set == null)
                {
                    ShowError(LocalizedStrings.Format("MuteEditorInvalidPatternFormat", bad));
                    return false;
                }

                value = set.ToJson();
                canonical = set.ToEditorText();
                return true;
            }

            string badHost;
            var hosts = ParseHosts(Box.Text, out badHost);
            if (hosts == null)
            {
                ShowError(LocalizedStrings.Format("MuteEditorInvalidHostFormat", badHost));
                return false;
            }

            var array = new JsonArray();
            foreach (var host in hosts)
            {
                array.Add(JsonValue.CreateStringValue(host));
            }

            value = array;
            canonical = string.Join("\r", hosts);
            return true;
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.Visibility = Visibility.Visible;
            AutomationHelper.AnnounceLiveRegion(ErrorText);
        }

        public static string HostsToText(IEnumerable<string> hosts)
        {
            return hosts == null ? "" : string.Join("\r", hosts);
        }

        private static IReadOnlyList<string> ParseHosts(string text, out string badHost)
        {
            badHost = null;
            var hosts = new List<string>();
            foreach (var raw in (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = raw.Trim().TrimStart('@').ToLowerInvariant();
                if (line.Length == 0) continue;

                if (!s_host.IsMatch(line))
                {
                    badHost = raw.Trim();
                    return null;
                }

                if (!hosts.Contains(line)) hosts.Add(line);
            }

            return hosts;
        }
    }
}
