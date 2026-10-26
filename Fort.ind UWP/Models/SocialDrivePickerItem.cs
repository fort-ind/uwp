using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialDrivePickerItem
    {
        private const int ThumbnailDecodeSize = 88;

        private const int BlurhashSize = 32;

        private ImageSource _thumbnail;

        private bool _thumbnailMade;

        public SocialDrivePickerItem(SocialDriveFile file, bool isAttached)
        {
            File = file;
            IsAttached = isAttached;
        }

        public SocialDriveFile File { get; private set; }

        public bool IsAttached { get; private set; }

        public string Name
        {
            get { return File.Name ?? ""; }
        }

        public bool IsSensitive
        {
            get { return File.IsSensitive; }
        }

        public bool IsVideo
        {
            get { return File.Kind == SocialDriveFileKind.Video; }
        }

        public bool ShowsName
        {
            get { return !File.IsVisual; }
        }

        public string KindGlyph
        {
            get
            {
                switch (File.Kind)
                {
                    case SocialDriveFileKind.Audio:
                        return "\uE8D6";
                    case SocialDriveFileKind.Video:
                        return "\uE714";
                    case SocialDriveFileKind.Image:
                    case SocialDriveFileKind.Gif:
                        return "\uEB9F";
                    default:
                        return "\uE7C3";
                }
            }
        }

        public ImageSource Thumbnail
        {
            get
            {
                if (!_thumbnailMade)
                {
                    _thumbnailMade = true;
                    _thumbnail = MakeThumbnail();
                }

                return _thumbnail;
            }
        }

        public string AutomationName
        {
            get
            {
                if (IsAttached) return LocalizedStrings.Format("SocialDriveFileAttachedAutomationFormat", Name);
                return File.IsSensitive
                       ? LocalizedStrings.Format("SocialDriveFileSensitiveAutomationFormat", Name)
                       : Name;
            }
        }

        private ImageSource MakeThumbnail()
        {
            if (!File.IsVisual) return null;
            if (File.IsSensitive || File.Kind == SocialDriveFileKind.Video && string.IsNullOrEmpty(File.ThumbnailUrl))
            {
                return BlurhashImage.CreateBitmap(File.Blurhash, BlurhashSize, BlurhashSize);
            }

            var url = string.IsNullOrEmpty(File.ThumbnailUrl) ? File.Url : File.ThumbnailUrl;
            var uri = WebLauncher.TryCreateFetchUri(url);
            if (uri == null) return null;

            BitmapImage bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelHeight = ThumbnailDecodeSize;
            bitmap.UriSource = uri;
            return bitmap;
        }
    }
}
