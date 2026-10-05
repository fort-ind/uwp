using System;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialSuggestionKind
    {
        Mention,
        Hashtag,
        Emoji
    }

    public sealed class SocialSuggestion
    {
        private const int ImageDecodeSize = 24;

        private SocialSuggestion()
        {
        }

        public SocialSuggestionKind Kind { get; private set; }

        public string Title { get; private set; }

        public string Detail { get; private set; }

        public string Glyph { get; private set; }

        public ImageSource Image { get; private set; }

        public string Insert { get; private set; }

        public SocialUser User { get; private set; }

        public bool HasDetail
        {
            get { return !string.IsNullOrEmpty(Detail); }
        }

        public string AutomationName
        {
            get { return HasDetail ? Title + ", " + Detail : Title; }
        }

        public static SocialSuggestion ForUser(SocialUser user)
        {
            var host = string.IsNullOrEmpty(user.Host) ? "" : "@" + user.Host;
            return new SocialSuggestion
            {
                Kind = SocialSuggestionKind.Mention,
                Title = SocialNoteItem.DisplayNameOf(user),
                Detail = user.Handle,
                Insert = "@" + user.Username + host + " ",
                Image = Bitmap(WebLauncher.TryCreateFetchUri(user.AvatarUrl)),
                User = user
            };
        }

        public static SocialSuggestion ForTag(string tag)
        {
            return new SocialSuggestion
            {
                Kind = SocialSuggestionKind.Hashtag,
                Title = "#" + tag,
                Insert = "#" + tag + " ",
                Glyph = "#"
            };
        }

        public static SocialSuggestion ForEmoji(SocialEmojiEntry entry, int tone)
        {
            var key = entry.KeyWithTone(tone);
            return new SocialSuggestion
            {
                Kind = SocialSuggestionKind.Emoji,
                Title = entry.IsCustom ? ":" + entry.Name + ":" : entry.Name,
                Glyph = entry.IsCustom ? "" : key,
                Image = entry.IsCustom ? Bitmap(entry.ImageUri) : null,
                Insert = entry.IsCustom ? ":" + entry.Name + ": " : key
            };
        }

        private static ImageSource Bitmap(Uri uri)
        {
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelHeight = ImageDecodeSize;
            bitmap.UriSource = uri;
            return bitmap;
        }
    }
}
