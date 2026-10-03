using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public enum SocialAttachmentState
    {
        Uploading,
        Done,
        Failed,
        TooLarge
    }

    public sealed class SocialAttachmentItem : INotifyPropertyChanged
    {
        private const int ThumbnailDecodeSize = 80;

        private SocialAttachmentState _state;

        private double _progress;

        private SocialDriveFile _file;

        private ImageSource _thumbnail;

        private string _errorText;

        private SocialAttachmentItem()
        {
        }

        public string Name { get; private set; }

        public string ContentType { get; private set; }

        public ulong Size { get; private set; }

        public Func<Task<IRandomAccessStream>> OpenAsync { get; private set; }

        public CancellationTokenSource Cancellation { get; set; }

        public SocialAttachmentState State
        {
            get { return _state; }
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged("IsUploading");
                OnPropertyChanged("HasError");
                OnPropertyChanged("AutomationName");
            }
        }

        public bool IsUploading
        {
            get { return _state == SocialAttachmentState.Uploading; }
        }

        public bool HasError
        {
            get { return _state == SocialAttachmentState.Failed || _state == SocialAttachmentState.TooLarge; }
        }

        public double Progress
        {
            get { return _progress; }
            set
            {
                if (Math.Abs(_progress - value) < 0.001) return;
                _progress = value;
                OnPropertyChanged();
            }
        }

        public SocialDriveFile File
        {
            get { return _file; }
            set
            {
                _file = value;
                OnPropertyChanged();
                OnPropertyChanged("HasDescription");
                OnPropertyChanged("AutomationName");
                if (_thumbnail == null && value != null) Thumbnail = RemoteThumbnail(value);
            }
        }

        public bool HasDescription
        {
            get { return _file != null && !string.IsNullOrWhiteSpace(_file.Comment); }
        }

        public bool NeedsDescription
        {
            get
            {
                if (_file == null) return false;
                return (_file.Kind == SocialDriveFileKind.Image || _file.Kind == SocialDriveFileKind.Gif || _file.Kind == SocialDriveFileKind.Video
                        || (_file.Type ?? "").StartsWith("audio/", StringComparison.Ordinal))
                       && !HasDescription;
            }
        }

        public ImageSource Thumbnail
        {
            get { return _thumbnail; }
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public string ErrorText
        {
            get { return _errorText; }
            set
            {
                _errorText = value;
                OnPropertyChanged();
                OnPropertyChanged("AutomationName");
            }
        }

        public string AutomationName
        {
            get
            {
                var name = Name ?? "";
                switch (_state)
                {
                    case SocialAttachmentState.Uploading:
                        return LocalizedStrings.Format("SocialAttachmentUploadingAutomationFormat", name);
                    case SocialAttachmentState.Failed:
                    case SocialAttachmentState.TooLarge:
                        return LocalizedStrings.Format("SocialAttachmentFailedAutomationFormat", name, _errorText ?? "");
                    default:
                        return HasDescription
                               ? LocalizedStrings.Format("SocialAttachmentDescribedAutomationFormat", name, _file.Comment)
                               : LocalizedStrings.Format("SocialAttachmentAutomationFormat", name);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static SocialAttachmentItem ForUpload(string name, string contentType, ulong size, Func<Task<IRandomAccessStream>> open)
        {
            return new SocialAttachmentItem
            {
                Name = string.IsNullOrWhiteSpace(name) ? "upload" : name,
                ContentType = contentType,
                Size = size,
                OpenAsync = open,
                _state = SocialAttachmentState.Uploading
            };
        }

        public static SocialAttachmentItem ForFile(SocialDriveFile file)
        {
            var item = new SocialAttachmentItem
            {
                Name = file.Name,
                ContentType = file.Type,
                _state = SocialAttachmentState.Done,
                _file = file
            };
            item._thumbnail = RemoteThumbnail(file);
            return item;
        }

        public async Task LoadLocalThumbnailAsync(IRandomAccessStream stream)
        {
            if (stream == null) return;

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelHeight = ThumbnailDecodeSize;
                await bitmap.SetSourceAsync(stream);
                Thumbnail = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SocialAttachmentItem: no local preview - {ex.Message}");
            }
        }

        private static ImageSource RemoteThumbnail(SocialDriveFile file)
        {
            if (file == null || !file.IsVisual) return null;

            var url = string.IsNullOrEmpty(file.ThumbnailUrl) && file.Kind != SocialDriveFileKind.Video ? file.Url : file.ThumbnailUrl;
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
