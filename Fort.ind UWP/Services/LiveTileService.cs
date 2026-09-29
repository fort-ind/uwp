using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Fort.ind_UWP
{
    public class LiveTileService
    {
        private const string DefaultMonogram = "FI";

        public const string NewContentBadgeGlyph = "newMessage";

        public static void UpdateTileWithNews(string title, string message, string branding = "name")
        {
            try
            {
                var tileXml = CreateTileXml(title, message, branding);

                TileNotification tileNotification = new TileNotification(tileXml);
                TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateTileWithNews failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void UpdateTileWithMultipleNews(List<NewsItem> newsItems)
        {
            if (newsItems == null || newsItems.Count == 0) return;

            try
            {
                var tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication();
                tileUpdater.EnableNotificationQueue(true);

                tileUpdater.Clear();

                for (int i = 0; i <= Math.Min(newsItems.Count - 1, 4); i++)
                {
                    var item = newsItems[i];
                    if (item == null) continue;

                    var tileXml = CreateTileXml(item.Title, item.Message, "name");
                    TileNotification tileNotification = new TileNotification(tileXml);
                    tileNotification.Tag = string.IsNullOrWhiteSpace(item.Tag) ? $"news{i}" : item.Tag;
                    tileUpdater.Update(tileNotification);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateTileWithMultipleNews failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void ShowNewsTile()
        {
            UpdateTileWithMultipleNews(new List<NewsItem>
            {
                new NewsItem(LocalizedStrings.Get("TileNewsWhatsNewTitle"),
                             LocalizedStrings.Get("TileNewsWhatsNewBody"),
                             "welcome"),
                new NewsItem(LocalizedStrings.Get("TileNewsGetStartedTitle"),
                             LocalizedStrings.Get("TileNewsGetStartedBody"),
                             "features")
            });
        }

        public static bool ShowPreviewTiles(IReadOnlyList<TilePreview> previews, IReadOnlyList<string> lockLines)
        {
            if (previews == null || previews.Count == 0) return false;

            try
            {
                var tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication();
                tileUpdater.EnableNotificationQueueForSquare150x150(true);
                tileUpdater.EnableNotificationQueueForWide310x150(true);
                tileUpdater.EnableNotificationQueueForSquare310x310(false);

                tileUpdater.Clear();

                for (int i = previews.Count - 1; i >= 0; i--)
                {
                    TileNotification tileNotification = new TileNotification(CreatePreviewTileXml(previews[i], previews, lockLines));
                    tileNotification.Tag = PreviewTagPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    tileUpdater.Update(tileNotification);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: ShowPreviewTiles failed – {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private const string PreviewTagPrefix = "preview";

        private const int PreviewTitleMaxLines = 2;

        private const int PreviewTitleOnlyMaxLines = 4;

        private const int PreviewBodyMaxLines = 3;

        private const int PreviewListBodyMaxLines = 2;

        private static XmlDocument CreatePreviewTileXml(TilePreview preview, IReadOnlyList<TilePreview> all, IReadOnlyList<string> lockLines)
        {
            TileContent content = new TileContent();
            content.Visual = new TileVisual();
            content.Visual.DisplayName = LocalizedStrings.Get("TileDisplayName");
            content.Visual.Branding = TileBranding.Name;

            if (lockLines != null)
            {
                if (lockLines.Count > 0) content.Visual.LockDetailedStatus1 = SanitizeText(lockLines[0]);
                if (lockLines.Count > 1) content.Visual.LockDetailedStatus2 = SanitizeText(lockLines[1]);
                if (lockLines.Count > 2) content.Visual.LockDetailedStatus3 = SanitizeText(lockLines[2]);
            }

            content.Visual.TileMedium = new TileBinding();
            content.Visual.TileMedium.Content = BuildPreviewContent(preview, true);

            content.Visual.TileWide = new TileBinding();
            content.Visual.TileWide.Content = BuildPreviewContent(preview, false);

            content.Visual.TileLarge = new TileBinding();
            content.Visual.TileLarge.Content = BuildPreviewList(all);

            return content.GetXml();
        }

        private static TileBindingContentAdaptive BuildPreviewContent(TilePreview preview, bool wrapTitle)
        {
            var body = SanitizeText(preview.Body);
            var hasBody = !string.IsNullOrWhiteSpace(body);

            AdaptiveText titleText = new AdaptiveText();
            titleText.Text = SanitizeText(preview.Title);
            titleText.HintStyle = TitleTextStyle;
            titleText.HintWrap = wrapTitle || !hasBody;
            titleText.HintMaxLines = hasBody ? PreviewTitleMaxLines : PreviewTitleOnlyMaxLines;

            TileBindingContentAdaptive result = new TileBindingContentAdaptive();
            result.Children.Add(titleText);

            if (hasBody)
            {
                AdaptiveText bodyText = new AdaptiveText();
                bodyText.Text = body;
                bodyText.HintStyle = AdaptiveTextStyle.CaptionSubtle;
                bodyText.HintWrap = true;
                bodyText.HintMaxLines = PreviewBodyMaxLines;
                result.Children.Add(bodyText);
            }

            if (preview.Avatar != null)
            {
                TilePeekImage peek = new TilePeekImage();
                peek.Source = preview.Avatar.ToString();
                peek.HintCrop = TilePeekImageCrop.Circle;
                var alternateText = SanitizeText(preview.ActorName);
                peek.AlternateText = string.IsNullOrEmpty(alternateText) ? null : alternateText;
                result.PeekImage = peek;
            }

            return result;
        }

        private static TileBindingContentAdaptive BuildPreviewList(IReadOnlyList<TilePreview> previews)
        {
            TileBindingContentAdaptive result = new TileBindingContentAdaptive();

            var count = Math.Min(previews.Count, AppConstants.SocialTileListLimit);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    result.Children.Add(new AdaptiveText());
                }

                AdaptiveText titleText = new AdaptiveText();
                titleText.Text = SanitizeText(previews[i].Title);
                titleText.HintStyle = TitleTextStyle;

                AdaptiveSubgroup subgroup = new AdaptiveSubgroup();
                subgroup.Children.Add(titleText);

                var body = SanitizeText(previews[i].Body);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    AdaptiveText bodyText = new AdaptiveText();
                    bodyText.Text = body;
                    bodyText.HintStyle = AdaptiveTextStyle.CaptionSubtle;
                    bodyText.HintWrap = true;
                    bodyText.HintMaxLines = PreviewListBodyMaxLines;
                    subgroup.Children.Add(bodyText);
                }

                AdaptiveGroup group = new AdaptiveGroup();
                group.Children.Add(subgroup);
                result.Children.Add(group);
            }

            return result;
        }

        private const AdaptiveTextStyle TitleTextStyle = AdaptiveTextStyle.Base;

        private static XmlDocument CreateTileXml(string title, string message, string branding)
        {
            var titleStyle = TitleTextStyle;
            var safeBranding = ParseBranding(branding);
            var safeTitle = SanitizeText(title);
            var safeMessage = SanitizeText(message);
            var smallTileText = SanitizeText(GetTileMonogram(title));

            TileContent content = new TileContent();
            content.Visual = new TileVisual();
            content.Visual.DisplayName = LocalizedStrings.Get("TileDisplayName");
            content.Visual.Branding = safeBranding;

            AdaptiveText smallText = new AdaptiveText();
            smallText.Text = smallTileText;
            smallText.HintStyle = AdaptiveTextStyle.Caption;
            smallText.HintAlign = AdaptiveTextAlign.Center;

            TileBindingContentAdaptive smallContent = new TileBindingContentAdaptive();
            smallContent.TextStacking = TileTextStacking.Center;
            smallContent.Children.Add(smallText);

            content.Visual.TileSmall = new TileBinding();
            content.Visual.TileSmall.Content = smallContent;

            content.Visual.TileMedium = new TileBinding();
            content.Visual.TileMedium.Content = BuildTitleMessageGroup(safeTitle, titleStyle, true, safeMessage, AdaptiveTextStyle.CaptionSubtle, 3);

            content.Visual.TileWide = new TileBinding();
            content.Visual.TileWide.Content = BuildTitleMessageGroup(safeTitle, titleStyle, false, safeMessage, AdaptiveTextStyle.Body, 2);

            TileBindingContentAdaptive largeContent = new TileBindingContentAdaptive();
            largeContent.TextStacking = TileTextStacking.Center;

            AdaptiveText largeTitleText = new AdaptiveText();
            largeTitleText.Text = safeTitle;
            largeTitleText.HintStyle = titleStyle;
            largeTitleText.HintAlign = AdaptiveTextAlign.Center;

            AdaptiveSubgroup largeSubgroup = new AdaptiveSubgroup();
            largeSubgroup.Children.Add(largeTitleText);

            AdaptiveGroup largeGroup = new AdaptiveGroup();
            largeGroup.Children.Add(largeSubgroup);

            AdaptiveText largeMessageText = new AdaptiveText();
            largeMessageText.Text = safeMessage;
            largeMessageText.HintStyle = AdaptiveTextStyle.Body;
            largeMessageText.HintWrap = true;
            largeMessageText.HintMaxLines = 6;
            largeMessageText.HintAlign = AdaptiveTextAlign.Center;

            AdaptiveText largeBrandingText = new AdaptiveText();
            largeBrandingText.Text = LocalizedStrings.Get("TileLargeBranding");
            largeBrandingText.HintStyle = AdaptiveTextStyle.CaptionSubtle;
            largeBrandingText.HintAlign = AdaptiveTextAlign.Center;

            largeContent.Children.Add(largeGroup);
            largeContent.Children.Add(largeMessageText);
            largeContent.Children.Add(largeBrandingText);

            content.Visual.TileLarge = new TileBinding();
            content.Visual.TileLarge.Content = largeContent;

            return content.GetXml();
        }

        private static TileBindingContentAdaptive BuildTitleMessageGroup(string title, AdaptiveTextStyle titleStyle, bool wrapTitle, string message, AdaptiveTextStyle messageStyle, int messageMaxLines)
        {
            AdaptiveText titleText = new AdaptiveText();
            titleText.Text = title;
            titleText.HintStyle = titleStyle;
            titleText.HintWrap = wrapTitle;

            AdaptiveText messageText = new AdaptiveText();
            messageText.Text = message;
            messageText.HintStyle = messageStyle;
            messageText.HintWrap = true;
            messageText.HintMaxLines = messageMaxLines;

            AdaptiveSubgroup subgroup = new AdaptiveSubgroup();
            subgroup.Children.Add(titleText);
            subgroup.Children.Add(messageText);

            AdaptiveGroup group = new AdaptiveGroup();
            group.Children.Add(subgroup);

            TileBindingContentAdaptive result = new TileBindingContentAdaptive();
            result.Children.Add(group);
            return result;
        }

        private static TileBranding ParseBranding(string branding)
        {
            switch ((branding ?? "").Trim().ToLowerInvariant())
            {
                case "none":
                    return TileBranding.None;
                case "logo":
                    return TileBranding.Logo;
                case "nameandlogo":
                    return TileBranding.NameAndLogo;
                default:
                    return TileBranding.Name;
            }
        }

        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            StringBuilder sanitized = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];

                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sanitized.Append(ch);
                    sanitized.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if ((ch == '\t') || (ch == '\n') || (ch == '\r') ||
                    (ch >= '\u0020' && ch <= '\uD7FF') ||
                    (ch >= '\uE000' && ch <= '\uFFFD'))
                {
                    sanitized.Append(ch);
                }

                i += 1;
            }

            return sanitized.ToString();
        }

        public static bool BadgeEnabled
        {
            get
            {
                try
                {
                    var stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingShowTileBadge];
                    if (stored == null) return true;
                    return Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LiveTileService: BadgeEnabled read failed – {ex.GetType().Name}: {ex.Message}");
                    return true;
                }
            }
            set
            {
                try
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingShowTileBadge] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LiveTileService: BadgeEnabled write failed – {ex.GetType().Name}: {ex.Message}");
                }

                if (!value) ClearBadge();
            }
        }

        public static bool TileCleared
        {
            get
            {
                try
                {
                    return Convert.ToBoolean(Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLiveTileCleared]);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LiveTileService: TileCleared read failed – {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
            set
            {
                try
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingLiveTileCleared] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LiveTileService: TileCleared write failed – {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        public static void UpdateBadge(int count)
        {
            try
            {
                if (!BadgeEnabled) return;

                if (count <= 0)
                {
                    ClearBadge();
                    return;
                }

                var clampedCount = Math.Min(count, 100);
                var badgeXml = $"<badge value=\"{clampedCount}\"/>";
                XmlDocument badgeDoc = new XmlDocument();
                badgeDoc.LoadXml(badgeXml);

                BadgeNotification badgeNotification = new BadgeNotification(badgeDoc);
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateBadge failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void UpdateBadgeGlyph(string glyph)
        {
            if (string.IsNullOrWhiteSpace(glyph)) return;

            try
            {
                if (!BadgeEnabled) return;

                var normalizedGlyph = glyph.Trim();
                if (!IsSupportedBadgeGlyph(normalizedGlyph))
                {
                    Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph skipped unsupported glyph '{normalizedGlyph}'.");
                    return;
                }

                var badgeXml = $"<badge value=\"{normalizedGlyph}\"/>";
                XmlDocument badgeDoc = new XmlDocument();
                badgeDoc.LoadXml(badgeXml);

                BadgeNotification badgeNotification = new BadgeNotification(badgeDoc);
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static bool SendToast(string title, string message)
        {
            try
            {
                var notifier = ToastNotificationManager.CreateToastNotifier();
                if (notifier.Setting != NotificationSetting.Enabled)
                {
                    Debug.WriteLine($"LiveTileService: Toast suppressed – NotificationSetting is {notifier.Setting}. " +
                                    "Enable notifications for this app in Windows Settings > System > Notifications.");
                    return false;
                }

                var toastContent = new ToastContentBuilder()
                    .AddText(SanitizeText(title))
                    .AddText(SanitizeText(message))
                    .GetToastContent();

                ToastNotification toast = new ToastNotification(toastContent.GetXml());
                notifier.Show(toast);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: SendToast failed – {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static bool ShowGroupedToast(GroupedToast toast)
        {
            if (toast == null) return false;

            try
            {
                var notifier = ToastNotificationManager.CreateToastNotifier();
                if (notifier.Setting != NotificationSetting.Enabled)
                {
                    Debug.WriteLine($"LiveTileService: Grouped toast suppressed – NotificationSetting is {notifier.Setting}.");
                    return false;
                }

                var builder = new ToastContentBuilder()
                    .AddArgument(toast.ArgumentKey, toast.ArgumentValue)
                    .AddText(SanitizeText(toast.Title));

                if (!string.IsNullOrWhiteSpace(toast.Body))
                {
                    builder.AddText(SanitizeText(toast.Body));
                }

                if (!string.IsNullOrWhiteSpace(toast.Attribution))
                {
                    builder.AddAttributionText(SanitizeText(toast.Attribution));
                }

                if (toast.AppLogo != null)
                {
                    builder.AddAppLogoOverride(toast.AppLogo, ToastGenericAppLogoCrop.Circle);
                }

                if (toast.Timestamp != DateTimeOffset.MinValue)
                {
                    builder.AddCustomTimeStamp(toast.Timestamp.UtcDateTime);
                }

                ToastNotification notification = new ToastNotification(builder.GetToastContent().GetXml());
                notification.Group = toast.Group;
                notification.Tag = toast.Tag;
                notifier.Show(notification);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: ShowGroupedToast failed – {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static void RemoveToastGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return;

            try
            {
                ToastNotificationManager.History.RemoveGroup(group);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: RemoveToastGroup failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void ClearTile()
        {
            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: ClearTile failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void ClearBadge()
        {
            try
            {
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: ClearBadge failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string GetTileMonogram(string primaryText)
        {
            if (string.IsNullOrWhiteSpace(primaryText)) return DefaultMonogram;

            var trimmed = primaryText.Trim();

            var monogram = TextHelper.FirstTextElements(trimmed, 2).ToUpperInvariant();
            return string.IsNullOrEmpty(monogram) ? DefaultMonogram : monogram;
        }

        private static bool IsSupportedBadgeGlyph(string glyph)
        {
            switch (glyph.ToLowerInvariant())
            {
                case "none":
                case "activity":
                case "alarm":
                case "alert":
                case "attention":
                case "available":
                case "away":
                case "busy":
                case "error":
                case "newmessage":
                case "paused":
                case "playing":
                case "unavailable":
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class GroupedToast
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public string Attribution { get; set; }
        public Uri AppLogo { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;
        public string Group { get; set; }
        public string Tag { get; set; }
        public string ArgumentKey { get; set; }
        public string ArgumentValue { get; set; }
    }

    public sealed class TilePreview
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public string ActorName { get; set; }
        public Uri Avatar { get; set; }
    }

    public class NewsItem
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string Tag { get; set; }
        public DateTime Timestamp { get; set; }

        public NewsItem(string title, string message, string tag = null)
        {
            this.Title = title;
            this.Message = message;
            this.Tag = tag;
            this.Timestamp = DateTime.Now;
        }
    }
}
