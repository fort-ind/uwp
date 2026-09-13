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

        /// <summary>
        /// One style for every queued tile's title. Adaptive tile content has no animation hint to
        /// set, so the old per-"animation" mapping only varied the title's text style as the queue
        /// rotated - the first tile's title came out in CaptionSubtle, smaller than its own body.
        /// </summary>
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

        /// <summary>
        /// Whether the user cleared the tile from Settings. Persisted so the choice survives the
        /// next launch - MainPage pushes the tile on every startup, which used to repaint a tile the
        /// user had just cleared. Refresh in Settings resets it.
        /// </summary>
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

                var clampedCount = Math.Min(count, 99);
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

        /// <summary>
        /// Initials for the small tile, which has room for about two characters.
        /// </summary>
        /// <remarks>
        /// The fallback is the app's own initialism, like PublisherDisplayName in the manifest, so
        /// it stays literal rather than moving to the resw - a resource lookup here can run off the
        /// UI thread, where LocalizedStrings degrades to returning the key, and a 20-character key
        /// is a far worse monogram than two wrong letters. The caller used to pass the *branding*
        /// argument as the fallback text, so an empty title rendered the small tile as "NA", the
        /// first two letters of "name".
        /// </remarks>
        private static string GetTileMonogram(string primaryText)
        {
            if (string.IsNullOrWhiteSpace(primaryText)) return DefaultMonogram;

            var trimmed = primaryText.Trim();

            // Whole text elements, not chars: a title starting with an emoji is a surrogate pair
            // and Substring(0, 2) would hand the tile half of one.
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
