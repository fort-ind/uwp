using System;
using System.Collections.Generic;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialCustomEmoji
    {
        private SocialCustomEmoji()
        {
        }

        public string Name { get; private set; }

        public IReadOnlyList<string> Aliases { get; private set; }

        public string Category { get; private set; }

        public string Url { get; private set; }

        public bool IsSensitive { get; private set; }

        internal static SocialCustomEmoji FromJson(JsonObject obj)
        {
            var name = SocialJson.String(obj, "name");
            var url = SocialJson.String(obj, "url");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return null;

            return new SocialCustomEmoji
            {
                Name = name,
                Aliases = SocialJson.StringList(obj, "aliases"),
                Category = SocialJson.String(obj, "category"),
                Url = url,
                IsSensitive = SocialJson.Bool(obj, "isSensitive") == true
            };
        }
    }

    public sealed class SocialEmojiEntry
    {
        public SocialEmojiEntry(string key, string text, Uri imageUri, string name, IReadOnlyList<string> aliases,
                                string group, IReadOnlyList<string> tones, bool isSensitive)
        {
            Key = key;
            Text = text;
            ImageUri = imageUri;
            Name = name;
            Aliases = aliases;
            Group = group;
            Tones = tones;
            IsSensitive = isSensitive;
        }

        public string Key { get; private set; }

        public string Text { get; private set; }

        public Uri ImageUri { get; private set; }

        public string Name { get; private set; }

        public IReadOnlyList<string> Aliases { get; private set; }

        public string Group { get; private set; }

        public IReadOnlyList<string> Tones { get; private set; }

        public bool IsSensitive { get; private set; }

        public bool IsCustom
        {
            get { return ImageUri != null; }
        }

        public string KeyWithTone(int tone)
        {
            if (Tones == null || tone < 1 || tone > Tones.Count) return Key;

            return Tones[tone - 1];
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query)) return true;

            if (Name != null && Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (Aliases != null)
            {
                foreach (var alias in Aliases)
                {
                    if (alias != null && alias.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            return false;
        }
    }

    public sealed class SocialEmojiItem
    {
        private const int ImageDecodeSize = 28;

        private ImageSource _image;

        public SocialEmojiItem(SocialEmojiEntry entry, int tone)
        {
            Entry = entry;
            Key = entry.KeyWithTone(tone);
        }

        public SocialEmojiEntry Entry { get; private set; }

        public string Key { get; private set; }

        public string Text
        {
            get { return Entry.IsCustom ? "" : Key; }
        }

        public Visibility TextVisibility
        {
            get { return Entry.IsCustom ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ImageVisibility
        {
            get { return Entry.IsCustom ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string AutomationName
        {
            get { return Entry.IsCustom ? ":" + Entry.Name + ":" : Entry.Name; }
        }

        public ImageSource Image
        {
            get
            {
                if (_image != null || !Entry.IsCustom) return _image;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelHeight = ImageDecodeSize;
                bitmap.UriSource = Entry.ImageUri;
                _image = bitmap;
                return _image;
            }
        }
    }

    public sealed class SocialEmojiGroup : List<SocialEmojiItem>
    {
        public SocialEmojiGroup(string id, string title, IEnumerable<SocialEmojiItem> items)
            : base(items)
        {
            Id = id;
            Title = title;
        }

        public string Id { get; private set; }

        public string Title { get; private set; }
    }
}
