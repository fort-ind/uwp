using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Windows.Globalization.DateTimeFormatting;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileDetails : UserControl
    {
        private const int RoleIconSize = 16;

        private readonly AccessibilitySettings _accessibilitySettings = new AccessibilitySettings();

        private readonly List<Border> _roleTags = new List<Border>();

        private SocialUserDetail _detail;

        private bool _tagsHighContrast;

        private bool _highContrastAttached;

        public SocialProfileDetails()
        {
            this.InitializeComponent();
        }

        public event EventHandler<SocialFollowList> FollowListRequested;

        public bool ShowAccountAge { get; set; }

        public bool HasContent
        {
            get
            {
                return TagsPanel.IsShown()
                       || BioText.IsShown()
                       || FieldsGrid.IsShown()
                       || InfoPanel.IsShown()
                       || CountsText.IsShown();
            }
        }

        public void Paint(SocialUserDetail detail, IReadOnlyDictionary<string, Uri> instanceEmojis)
        {
            if (detail == null) return;

            _detail = detail;
            var user = detail.User;
            var localEmojis = IsLocal(user) ? instanceEmojis : null;

            PaintTags(detail);

            var bio = MfmText.Parse(detail.Description, user.Emojis, localEmojis, null);
            if (bio.Count > 0)
            {
                MfmInlineBuilder.Fill(BioText, bio, true, MfmInlineBuilder.BodyEmojiSize);
                BioText.Visibility = Visibility.Visible;
            }
            else
            {
                BioText.Visibility = Visibility.Collapsed;
            }

            PaintFields(detail, localEmojis);
            PaintInfo(detail);
            PaintCounts(detail);
        }

        public void PaintRelation(SocialUserDetail detail)
        {
            if (detail == null) return;

            _detail = detail;
            PaintRelationTags(detail);
            PaintCounts(detail);
        }

        public void Clear()
        {
            _detail = null;
            foreach (var tag in _roleTags)
            {
                TagsPanel.Children.Remove(tag);
            }
            _roleTags.Clear();

            TagsPanel.Visibility = Visibility.Collapsed;
            BioText.Blocks.Clear();
            BioText.Visibility = Visibility.Collapsed;
            FieldsGrid.Children.Clear();
            FieldsGrid.RowDefinitions.Clear();
            FieldsGrid.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Collapsed;
            CountsText.Inlines.Clear();
            CountsText.Visibility = Visibility.Collapsed;
        }

        private static bool IsLocal(SocialUser user)
        {
            return user != null && string.IsNullOrWhiteSpace(user.Host);
        }

        private void Details_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_highContrastAttached)
                {
                    _accessibilitySettings.HighContrastChanged += AccessibilitySettings_HighContrastChanged;
                    _highContrastAttached = true;
                }

                RepaintTagsIfContrastChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileDetails: could not watch high contrast - {ex.Message}");
            }
        }

        private void Details_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_highContrastAttached) return;

            _accessibilitySettings.HighContrastChanged -= AccessibilitySettings_HighContrastChanged;
            _highContrastAttached = false;
        }

        private async void AccessibilitySettings_HighContrastChanged(AccessibilitySettings sender, object args)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        RepaintTagsIfContrastChanged();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialProfileDetails: could not repaint the role tags - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileDetails: high contrast handler failed - {ex.Message}");
            }
        }

        private void RepaintTagsIfContrastChanged()
        {
            if (_detail != null && _tagsHighContrast != _accessibilitySettings.HighContrast) PaintTags(_detail);
        }

        private void PaintTags(SocialUserDetail detail)
        {
            foreach (var tag in _roleTags)
            {
                TagsPanel.Children.Remove(tag);
            }
            _roleTags.Clear();
            _tagsHighContrast = _accessibilitySettings.HighContrast;

            var index = 0;
            foreach (var tag in detail.Roles.Select(BuildRoleTag))
            {
                TagsPanel.Children.Insert(index++, tag);
                _roleTags.Add(tag);
            }

            BotTag.Visibility = detail.User.IsBot ? Visibility.Visible : Visibility.Collapsed;
            LockedTag.Visibility = detail.IsLocked ? Visibility.Visible : Visibility.Collapsed;
            PaintRelationTags(detail);
        }

        private void PaintRelationTags(SocialUserDetail detail)
        {
            FollowsYouTag.Visibility = detail.IsFollowed ? Visibility.Visible : Visibility.Collapsed;
            TagsPanel.Visibility = _roleTags.Count > 0 || detail.IsFollowed || detail.User.IsBot || detail.IsLocked
                                   ? Visibility.Visible
                                   : Visibility.Collapsed;
        }

        private Border BuildRoleTag(SocialRole role)
        {
            var tag = new Border { Style = LocalStyle("ProfileRoleTagStyle") };

            Windows.UI.Color color;
            if (!_tagsHighContrast && ColorHelper.TryHexToColor(role.Color, out color))
            {
                tag.BorderBrush = new SolidColorBrush(color);
            }

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            var iconUri = WebLauncher.TryCreateFetchUri(role.IconUrl);
            if (iconUri != null)
            {
                var bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelHeight = RoleIconSize;
                bitmap.UriSource = iconUri;

                var icon = new Image
                {
                    Source = bitmap,
                    Height = RoleIconSize,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };
                AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
                icon.ImageFailed += RoleIcon_ImageFailed;
                content.Children.Add(icon);
            }

            content.Children.Add(new TextBlock
            {
                Text = role.Name ?? "",
                Style = CaptionStyle(),
                VerticalAlignment = VerticalAlignment.Center
            });

            tag.Child = content;

            if (!string.IsNullOrWhiteSpace(role.Description))
            {
                ToolTipService.SetToolTip(tag, role.Description.Trim());
            }

            return tag;
        }

        private Style CaptionStyle()
        {
            var text = FollowsYouTag.Child as TextBlock;
            return text == null ? null : text.Style;
        }

        private static void RoleIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            var icon = sender as Image;
            if (icon != null) icon.Visibility = Visibility.Collapsed;
        }

        private void PaintFields(SocialUserDetail detail, IReadOnlyDictionary<string, Uri> localEmojis)
        {
            FieldsGrid.Children.Clear();
            FieldsGrid.RowDefinitions.Clear();

            var row = 0;
            foreach (var field in detail.Fields)
            {
                FieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = MfmText.PlainName(field.Name),
                    Style = LocalStyle("ProfileFieldLabelStyle"),
                    TextWrapping = TextWrapping.Wrap
                };
                BindingOperations.SetBinding(label, TextBlock.ForegroundProperty,
                                             new Binding { Source = LocationText, Path = new PropertyPath("Foreground") });
                Grid.SetRow(label, row);
                FieldsGrid.Children.Add(label);

                var value = new RichTextBlock
                {
                    Style = BioText.Style,
                    IsTextSelectionEnabled = false,
                    TextWrapping = TextWrapping.Wrap
                };
                MfmInlineBuilder.Fill(value, MfmText.Parse(field.Value, detail.User.Emojis, localEmojis, null), true, MfmInlineBuilder.BodyEmojiSize);
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                FieldsGrid.Children.Add(value);

                row++;
            }

            FieldsGrid.Visibility = row > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Style LocalStyle(string key)
        {
            object value;
            return Resources.TryGetValue(key, out value) ? value as Style : null;
        }

        private void PaintInfo(SocialUserDetail detail)
        {
            SetInfoLine(LocationLine, LocationText, string.IsNullOrWhiteSpace(detail.Location) ? null : detail.Location.Trim());
            SetInfoLine(BirthdayLine, BirthdayText, FormatBirthday(detail.Birthday));
            SetInfoLine(JoinedLine, JoinedText, FormatJoined(detail.CreatedAt));

            InfoPanel.Visibility = LocationLine.IsShown()
                                   || BirthdayLine.IsShown()
                                   || JoinedLine.IsShown()
                                   ? Visibility.Visible
                                   : Visibility.Collapsed;
        }

        private string FormatJoined(DateTimeOffset createdAt)
        {
            if (createdAt == DateTimeOffset.MinValue) return null;

            var date = RelativeTime.FormatDate(createdAt);
            if (!ShowAccountAge) return LocalizedStrings.Format("SocialProfileJoinedFormat", date);

            return LocalizedStrings.Format("ProfileJoinedWithAgeFormat", date, FormatAccountAge(createdAt, DateTimeOffset.Now));
        }

        private static string FormatAccountAge(DateTimeOffset createdAt, DateTimeOffset now)
        {
            var start = createdAt.ToLocalTime().Date;
            var today = now.ToLocalTime().Date;
            if (today < start) today = start;

            var months = (today.Year - start.Year) * 12 + today.Month - start.Month;
            if (today.Day < start.Day) months--;

            if (months >= 12)
            {
                var years = months / 12;
                return years == 1 ? LocalizedStrings.Get("ProfileAgeYearOne") : LocalizedStrings.Format("ProfileAgeYearsFormat", years);
            }

            if (months >= 1)
            {
                return months == 1 ? LocalizedStrings.Get("ProfileAgeMonthOne") : LocalizedStrings.Format("ProfileAgeMonthsFormat", months);
            }

            var days = Math.Max(1, (int)(today - start).TotalDays);
            return days == 1 ? LocalizedStrings.Get("ProfileAgeDayOne") : LocalizedStrings.Format("ProfileAgeDaysFormat", days);
        }

        private static void SetInfoLine(FrameworkElement line, TextBlock text, string value)
        {
            text.Text = value ?? "";
            line.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string FormatBirthday(string birthday)
        {
            if (string.IsNullOrWhiteSpace(birthday)) return null;

            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParseExact(birthday.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                              DateTimeStyles.AssumeUniversal, out parsed))
            {
                return null;
            }

            try
            {
                var formatter = new DateTimeFormatter("month day");
                return LocalizedStrings.Format("SocialProfileBirthdayFormat",
                                               formatter.Format(new DateTimeOffset(parsed.Year, parsed.Month, parsed.Day, 12, 0, 0, TimeSpan.Zero)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileDetails: could not format a birthday - {ex.Message}");
                return null;
            }
        }

        private void PaintCounts(SocialUserDetail detail)
        {
            CountsText.Inlines.Clear();

            var viewer = SocialContentService.CurrentAccountId();
            var parts = 0;

            if (detail.NotesCount.HasValue)
            {
                CountText.AppendBold(CountsText.Inlines,
                                     detail.NotesCount.Value == 1 ? "SocialProfileNotesCountOneFormat" : "SocialProfileNotesCountFormat",
                                     detail.NotesCount.Value);
                parts++;
            }

            if (detail.ShowsFollowingCount(viewer))
            {
                AppendSeparator(parts);
                CountsText.Inlines.Add(CountLink("ProfileFollowingCountFormat", detail.FollowingCount.GetValueOrDefault(), SocialFollowList.Following));
                parts++;
            }

            if (detail.ShowsFollowersCount(viewer))
            {
                AppendSeparator(parts);
                var followers = detail.FollowersCount.GetValueOrDefault();
                CountsText.Inlines.Add(CountLink(followers == 1 ? "ProfileFollowersCountOneFormat" : "ProfileFollowersCountFormat",
                                                 followers,
                                                 SocialFollowList.Followers));
                parts++;
            }

            CountsText.Visibility = parts > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AppendSeparator(int partsSoFar)
        {
            if (partsSoFar > 0) CountsText.Inlines.Add(new Run { Text = LocalizedStrings.Get("SocialProfileCountSeparator") });
        }

        private Hyperlink CountLink(string formatKey, int count, SocialFollowList list)
        {
            var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
            CountText.AppendBold(link.Inlines, formatKey, count);

            if (list == SocialFollowList.Following)
            {
                link.Click += FollowingLink_Click;
            }
            else
            {
                link.Click += FollowersLink_Click;
            }

            return link;
        }

        private void FollowingLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
        {
            RequestFollowList(SocialFollowList.Following);
        }

        private void FollowersLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
        {
            RequestFollowList(SocialFollowList.Followers);
        }

        private void RequestFollowList(SocialFollowList list)
        {
            var handler = FollowListRequested;
            if (handler != null) handler(this, list);
        }
    }
}
