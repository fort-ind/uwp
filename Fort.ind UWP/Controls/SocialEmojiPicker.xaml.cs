using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialEmojiPickerMode
    {
        Reaction,
        Insert
    }

    public sealed partial class SocialEmojiPicker : UserControl
    {
        private const string LikeOnly = "likeOnly";

        private const string NonSensitiveOnly = "nonSensitiveOnly";

        private const string NonSensitiveOnlyForLocal = "nonSensitiveOnlyForLocalLikeOnlyForRemote";

        private const string RecentGroupId = "recent";

        private const string ResultsGroupId = "results";

        private const string RecentGlyph = "";

        private const int SearchLimit = 200;

        private const double StripButtonSize = 36;

        private const int StripImageSize = 22;

        private static readonly string[] s_toneHands =
        {
            "✋", "✋\U0001F3FB", "✋\U0001F3FC", "✋\U0001F3FD", "✋\U0001F3FE", "✋\U0001F3FF"
        };

        private static readonly Dictionary<string, string> s_groupIcons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "smileys-emotion", "\U0001F600" },
            { "people-body", "\U0001F44B" },
            { "animals-nature", "\U0001F43B" },
            { "food-drink", "\U0001F354" },
            { "travel-places", "\U0001F697" },
            { "activities", "⚽" },
            { "objects", "\U0001F4A1" },
            { "symbols", "❤️" },
            { "flags", "\U0001F3C1" }
        };

        [ThreadStatic]
        private static SocialEmojiPicker t_current;

        private readonly CollectionViewSource _viewSource;

        private readonly Debouncer _searchDebounce = new Debouncer();

        private readonly List<SocialEmojiGroup> _groups = new List<SocialEmojiGroup>();

        private SocialEmojiCatalog _catalog;

        private Task<SocialEmojiCatalog> _loading;

        private Flyout _host;

        private SocialEmojiPickerMode _mode;

        private string _myReaction;

        private string _acceptance;

        private Action<string> _picked;

        private Action _like;

        private Action _remove;

        private int _builtTone = -1;

        private string _builtAcceptance;

        public SocialEmojiPicker()
        {
            this.InitializeComponent();

            _viewSource = (CollectionViewSource)Resources["EmojiViewSource"];
            BuildTonePanel();
        }

        public static SocialEmojiPicker ForCurrentView()
        {
            if (t_current == null) t_current = new SocialEmojiPicker();
            return t_current;
        }

        public static void ForgetCurrentView()
        {
            var picker = t_current;
            t_current = null;
            if (picker == null) return;

            try
            {
                picker.Detach();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: could not let go of the window's picker - {ex.Message}");
            }
        }

        public void AttachTo(Flyout flyout)
        {
            if (flyout == null) return;

            if (_host != null && !ReferenceEquals(_host, flyout)) _host.Content = null;
            _host = flyout;
            if (!ReferenceEquals(flyout.Content, this)) flyout.Content = this;
        }

        public void Detach()
        {
            _picked = null;
            _like = null;
            _remove = null;
            _searchDebounce.Cancel();

            if (_host != null)
            {
                _host.Content = null;
                _host = null;
            }
        }

        public void Configure(SocialEmojiPickerMode mode, string myReaction, string reactionAcceptance,
                              Action<string> picked, Action like, Action remove)
        {
            _mode = mode;
            _myReaction = myReaction;
            _acceptance = reactionAcceptance;
            _picked = picked;
            _like = like;
            _remove = remove;

            ReactionRow.Visibility = mode == SocialEmojiPickerMode.Reaction ? Visibility.Visible : Visibility.Collapsed;
            LikeButton.IsEnabled = myReaction == null || !SocialReactions.IsLike(myReaction);
            RemoveButton.IsEnabled = myReaction != null;

            _searchDebounce.Cancel();
            if (SearchBox.Text.Length > 0) SearchBox.Text = "";

            UpdateToneButton();
            ShowCatalog();
        }

        private Style SquareButtonStyle
        {
            get
            {
                object style;
                return Resources.TryGetValue("PickerSquareButtonStyle", out style) ? style as Style : null;
            }
        }

        private bool IsLikeOnly
        {
            get { return _mode == SocialEmojiPickerMode.Reaction && string.Equals(_acceptance, LikeOnly, StringComparison.Ordinal); }
        }

        private bool HidesSensitive
        {
            get
            {
                return _mode == SocialEmojiPickerMode.Reaction
                       && (string.Equals(_acceptance, NonSensitiveOnly, StringComparison.Ordinal)
                           || string.Equals(_acceptance, NonSensitiveOnlyForLocal, StringComparison.Ordinal));
            }
        }

        private async void ShowCatalog()
        {
            try
            {
                if (IsLikeOnly)
                {
                    ShowMessage(LocalizedStrings.Get("SocialEmojiPickerLikeOnly"));
                    return;
                }

                if (_catalog == null)
                {
                    ShowLoading();
                    if (_loading == null) _loading = SocialEmojiService.GetCatalogAsync();

                    var catalog = await _loading;
                    _loading = null;
                    if (catalog == null || (catalog.Custom.Count == 0 && catalog.Unicode.Count == 0))
                    {
                        ShowMessage(LocalizedStrings.Get("SocialEmojiPickerFailed"));
                        return;
                    }

                    _catalog = catalog;
                    _builtTone = -1;
                }

                var tone = SocialEmojiService.SkinTone;
                if (tone != _builtTone || !string.Equals(_builtAcceptance, _acceptance, StringComparison.Ordinal) || _groups.Count == 0)
                {
                    BuildGroups(tone);
                }
                else
                {
                    RefreshRecents(tone);
                }

                ShowGroups(_groups, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: could not show the emoji - {ex.GetType().Name}: {ex.Message}");
                _loading = null;
                ShowMessage(LocalizedStrings.Get("SocialEmojiPickerFailed"));
            }
        }

        private void BuildGroups(int tone)
        {
            _builtTone = tone;
            _builtAcceptance = _acceptance;
            _groups.Clear();

            var recent = RecentGroup(tone);
            if (recent != null) _groups.Add(recent);

            string current = null;
            var items = new List<SocialEmojiItem>();
            foreach (var entry in _catalog.Custom.Where(Allows))
            {
                if (current != null && !string.Equals(current, entry.Group, StringComparison.Ordinal))
                {
                    _groups.Add(new SocialEmojiGroup(current, CustomGroupTitle(current), items));
                    items = new List<SocialEmojiItem>();
                }

                current = entry.Group;
                items.Add(new SocialEmojiItem(entry, tone));
            }

            if (current != null && items.Count > 0) _groups.Add(new SocialEmojiGroup(current, CustomGroupTitle(current), items));

            foreach (var group in SocialEmojiService.UnicodeGroups)
            {
                var unicode = _catalog.Unicode.Where(entry => string.Equals(entry.Group, group, StringComparison.Ordinal))
                                              .Select(entry => new SocialEmojiItem(entry, tone))
                                              .ToList();

                if (unicode.Count > 0) _groups.Add(new SocialEmojiGroup(group, UnicodeGroupTitle(group), unicode));
            }

            BuildStrip();
        }

        private void RefreshRecents(int tone)
        {
            var recent = RecentGroup(tone);
            var hadRecent = _groups.Count > 0 && _groups[0].Id == RecentGroupId;

            if (hadRecent) _groups.RemoveAt(0);
            if (recent != null) _groups.Insert(0, recent);

            if (hadRecent != (recent != null)) BuildStrip();
        }

        private SocialEmojiGroup RecentGroup(int tone)
        {
            var items = new List<SocialEmojiItem>();
            foreach (var key in SocialEmojiService.Recents)
            {
                string shown;
                var entry = _catalog.Find(key, out shown);
                if (entry == null || !Allows(entry)) continue;

                items.Add(new SocialEmojiItem(entry, entry.IsCustom ? 0 : ToneOf(entry, shown)));
            }

            return items.Count == 0 ? null : new SocialEmojiGroup(RecentGroupId, LocalizedStrings.Get("SocialEmojiGroupRecent"), items);
        }

        private static int ToneOf(SocialEmojiEntry entry, string shown)
        {
            if (entry.Tones == null) return 0;

            for (var i = 0; i < entry.Tones.Count; i++)
            {
                if (string.Equals(entry.Tones[i], shown, StringComparison.Ordinal)) return i + 1;
            }

            return 0;
        }

        private bool Allows(SocialEmojiEntry entry)
        {
            return !(entry.IsSensitive && HidesSensitive);
        }

        private static string CustomGroupTitle(string group)
        {
            var category = group.Substring(SocialEmojiService.CustomGroupPrefix.Length);
            return category.Length == 0 ? LocalizedStrings.Get("SocialEmojiGroupOther") : category;
        }

        private static string UnicodeGroupTitle(string group)
        {
            var key = new System.Text.StringBuilder("SocialEmojiGroup");
            foreach (var part in group.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                key.Append(char.ToUpperInvariant(part[0]));
                key.Append(part.Substring(1));
            }

            return LocalizedStrings.Get(key.ToString());
        }

        private void ShowGroups(IReadOnlyList<SocialEmojiGroup> groups, bool showStrip)
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;

            _viewSource.Source = new List<SocialEmojiGroup>(groups);
            var view = _viewSource.View;
            if (view != null && !ReferenceEquals(EmojiGrid.ItemsSource, view)) EmojiGrid.ItemsSource = view;

            var empty = groups.Count == 0;
            EmojiGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            MessageText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (empty) MessageText.Text = LocalizedStrings.Get("SocialEmojiPickerNoResults");

            SearchRow.Visibility = Visibility.Visible;
            StripScroller.Visibility = showStrip && !empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowLoading()
        {
            EmojiGrid.Visibility = Visibility.Collapsed;
            MessageText.Visibility = Visibility.Collapsed;
            StripScroller.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
        }

        private void ShowMessage(string message)
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            EmojiGrid.Visibility = Visibility.Collapsed;
            StripScroller.Visibility = Visibility.Collapsed;
            SearchRow.Visibility = IsLikeOnly ? Visibility.Collapsed : Visibility.Visible;
            MessageText.Text = message;
            MessageText.Visibility = Visibility.Visible;
        }

        private void BuildStrip()
        {
            CategoryStrip.Children.Clear();

            foreach (var group in _groups)
            {
                var button = new Button
                {
                    Style = SquareButtonStyle,
                    Width = StripButtonSize,
                    Height = StripButtonSize,
                    Tag = group.Id,
                    Content = StripIcon(group)
                };
                AutomationProperties.SetName(button, group.Title);
                ToolTipService.SetToolTip(button, group.Title);
                button.Click += StripButton_Click;
                CategoryStrip.Children.Add(button);
            }
        }

        private static UIElement StripIcon(SocialEmojiGroup group)
        {
            if (group.Id == RecentGroupId)
            {
                return new FontIcon { Glyph = RecentGlyph, FontSize = 16, IsTextScaleFactorEnabled = false };
            }

            string emoji;
            if (s_groupIcons.TryGetValue(group.Id, out emoji))
            {
                return new TextBlock
                {
                    Text = emoji,
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = 18,
                    IsTextScaleFactorEnabled = false,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var first = group.Count > 0 ? group[0].Entry : null;
            if (first == null || first.ImageUri == null) return new TextBlock { Text = group.Title };

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelHeight = StripImageSize;
            bitmap.UriSource = first.ImageUri;
            return new Image { Source = bitmap, Width = StripImageSize, Height = StripImageSize, Stretch = Stretch.Uniform };
        }

        private void StripButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var id = (sender as FrameworkElement)?.Tag as string;
                var target = _groups.FirstOrDefault(group => group.Id == id && group.Count > 0);
                if (target != null) EmojiGrid.ScrollIntoView(target[0], ScrollIntoViewAlignment.Leading);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: could not jump to a category - {ex.Message}");
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Search(SearchBox.Text, _searchDebounce.Restart());
        }

        private async void Search(string query, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SearchDebounceMilliseconds);
                if (cancellationToken.IsCancellationRequested || _catalog == null || IsLikeOnly) return;

                var trimmed = (query ?? "").Trim().Trim(':');
                if (trimmed.Length == 0)
                {
                    ShowGroups(_groups, true);
                    return;
                }

                var tone = SocialEmojiService.SkinTone;
                var results = new List<SocialEmojiItem>();
                foreach (var entry in _catalog.Custom)
                {
                    if (results.Count >= SearchLimit) break;
                    if (Allows(entry) && entry.Matches(trimmed)) results.Add(new SocialEmojiItem(entry, tone));
                }

                foreach (var entry in _catalog.Unicode)
                {
                    if (results.Count >= SearchLimit) break;
                    if (entry.Matches(trimmed)) results.Add(new SocialEmojiItem(entry, tone));
                }

                var groups = results.Count == 0
                             ? new SocialEmojiGroup[0]
                             : new[] { new SocialEmojiGroup(ResultsGroupId, LocalizedStrings.Get("SocialEmojiGroupResults"), results) };
                ShowGroups(groups, false);
                AutomationHelper.AnnounceStatus(SearchBox,
                                                results.Count == 0
                                                ? LocalizedStrings.Get("SocialEmojiPickerNoResults")
                                                : LocalizedStrings.Format("SocialEmojiPickerResultsFormat", CountText.Format(results.Count)),
                                                "SocialEmojiSearch");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: search failed - {ex.Message}");
            }
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (e.Key != VirtualKey.Down && e.Key != VirtualKey.Enter) return;
                if (!EmojiGrid.IsShown() || EmojiGrid.Items.Count == 0) return;

                if (e.Key == VirtualKey.Enter)
                {
                    var first = EmojiGrid.Items[0] as SocialEmojiItem;
                    if (first != null) Pick(first);
                }
                else
                {
                    var container = EmojiGrid.ContainerFromIndex(0) as Control;
                    if (container != null) container.Focus(FocusState.Keyboard);
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: search key failed - {ex.Message}");
            }
        }

        private void EmojiGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as SocialEmojiItem;
            if (item != null) Pick(item);
        }

        private void Pick(SocialEmojiItem item)
        {
            try
            {
                SocialEmojiService.RememberRecent(item.Key);

                var picked = _picked;
                if (_host != null) _host.Hide();
                if (picked != null) picked(item.Key);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: could not use the emoji - {ex.Message}");
            }
        }

        private void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var like = _like;
                if (_host != null) _host.Hide();
                if (like != null) like();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: like failed - {ex.Message}");
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var remove = _remove;
                if (_host != null) _host.Hide();
                if (remove != null) remove();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: remove failed - {ex.Message}");
            }
        }

        private void BuildTonePanel()
        {
            for (var tone = 0; tone < s_toneHands.Length; tone++)
            {
                var button = new Button
                {
                    Style = SquareButtonStyle,
                    Width = 40,
                    Height = 40,
                    Tag = tone,
                    Content = new TextBlock
                    {
                        Text = s_toneHands[tone],
                        FontFamily = new FontFamily("Segoe UI Emoji"),
                        FontSize = 20,
                        IsTextScaleFactorEnabled = false,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                var name = LocalizedStrings.Get("SocialEmojiSkinTone" + tone);
                AutomationProperties.SetName(button, name);
                ToolTipService.SetToolTip(button, name);
                button.Click += ToneOption_Click;
                TonePanel.Children.Add(button);
            }
        }

        private void UpdateToneButton()
        {
            var tone = SocialEmojiService.SkinTone;
            ToneText.Text = s_toneHands[tone];
            AutomationProperties.SetName(ToneButton, LocalizedStrings.Format("SocialEmojiPickerSkinToneFormat",
                                                                             LocalizedStrings.Get("SocialEmojiSkinTone" + tone)));
        }

        private void ToneOption_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var element = sender as FrameworkElement;
                if (element == null || !(element.Tag is int)) return;

                ToneFlyout.Hide();
                SocialEmojiService.SkinTone = (int)element.Tag;
                UpdateToneButton();

                if (_catalog == null) return;

                BuildGroups(SocialEmojiService.SkinTone);
                if (SearchBox.Text.Trim().Length > 0)
                {
                    Search(SearchBox.Text, _searchDebounce.Restart());
                }
                else
                {
                    ShowGroups(_groups, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialEmojiPicker: could not change the skin tone - {ex.Message}");
            }
        }
    }
}
