using System;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileEditor
    {
        private const double CropperWidth = 456;

        private const double CropperHeight = 320;

        private static readonly string[] s_imageTypes = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

        private SocialProfileImage _pendingAvatar;

        private SocialProfileImage _pendingBanner;

        private bool _picking;

        private void ClearPendingImages()
        {
            _pendingAvatar = null;
            _pendingBanner = null;
        }

        private async void ChangeAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ChangeImageAsync(true);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: could not change the avatar", ex);
            }
        }

        private async void ChangeBannerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ChangeImageAsync(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: could not change the banner", ex);
            }
        }

        private async Task ChangeImageAsync(bool avatar)
        {
            if (_picking || _saving) return;
            _picking = true;

            try
            {
                var file = await PickImageAsync();
                if (file == null) return;

                ulong size = 0;
                try
                {
                    size = (await file.GetBasicPropertiesAsync()).Size;
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialProfileEditor: could not read the image's size", ex);
                }

                if (size > AppConstants.SocialUploadLimitBytes)
                {
                    await DialogService.ShowMessageAsync(this,
                                                         LocalizedStrings.Get("SocialProfileImageTooLargeTitle"),
                                                         LocalizedStrings.Format("SocialProfileImageTooLargeFormat", AppConstants.SocialUploadLimitBytes / (1024 * 1024)),
                                                         LocalizedStrings.Get("DialogOk"));
                    return;
                }

                SocialProfileImage image = null;
                var unreadable = false;
                await DialogService.RunExclusiveAsync(async () =>
                {
                    var crop = await DialogService.ShowConfirmCoreAsync(this,
                                                                        LocalizedStrings.Get("SocialProfileCropAskTitle"),
                                                                        LocalizedStrings.Get(avatar ? "SocialProfileCropAskAvatar" : "SocialProfileCropAskBanner"),
                                                                        LocalizedStrings.Get("SocialProfileCropYes"),
                                                                        LocalizedStrings.Get("SocialProfileCropNo"),
                                                                        ContentDialogButton.Primary);
                    if (!crop)
                    {
                        image = new SocialProfileImage(file.Name, file.ContentType, size, async () => await file.OpenReadAsync());
                        return;
                    }

                    var cropper = new ImageCropper
                    {
                        Width = CropperWidth,
                        Height = CropperHeight,
                        AspectRatio = avatar ? 1.0 : 2.0,
                        CropShape = avatar ? CropShape.Circular : CropShape.Rectangular
                    };

                    try
                    {
                        await cropper.LoadImageFromFile(file);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialProfileEditor: the cropper could not open the image", ex);
                        unreadable = true;
                        return;
                    }

                    var dialog = new ContentDialog
                    {
                        Title = LocalizedStrings.Get(avatar ? "SocialProfileCropAvatarTitle" : "SocialProfileCropBannerTitle"),
                        Content = cropper,
                        PrimaryButtonText = LocalizedStrings.Get("SocialProfileCropDone"),
                        CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                        DefaultButton = ContentDialogButton.Primary
                    };
                    DialogService.AttachToOwner(dialog, this);
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

                    var bytes = await CropAsync(cropper, avatar);
                    if (bytes == null)
                    {
                        unreadable = true;
                        return;
                    }

                    var extension = avatar ? ".png" : ".jpg";
                    image = new SocialProfileImage(SocialProfileEditService.UploadName(file.Name, extension),
                                                   avatar ? "image/png" : "image/jpeg",
                                                   (ulong)bytes.Length,
                                                   () => OpenBytesAsync(bytes));
                });

                if (unreadable)
                {
                    await DialogService.ShowMessageAsync(this,
                                                         LocalizedStrings.Get("SocialProfileImageUnreadableTitle"),
                                                         LocalizedStrings.Get("SocialProfileImageUnreadableBody"),
                                                         LocalizedStrings.Get("DialogOk"));
                    return;
                }

                if (image == null) return;
                await SetPendingAsync(avatar, image);
            }
            finally
            {
                _picking = false;
            }
        }

        private static async Task<StorageFile> PickImageAsync()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var type in s_imageTypes)
            {
                picker.FileTypeFilter.Add(type);
            }

            return await picker.PickSingleFileAsync();
        }

        private static async Task<byte[]> CropAsync(ImageCropper cropper, bool avatar)
        {
            try
            {
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await cropper.SaveAsync(stream, avatar ? BitmapFileFormat.Png : BitmapFileFormat.Jpeg, true);

                    var bytes = new byte[stream.Size];
                    stream.Seek(0);
                    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync((uint)stream.Size);
                        reader.ReadBytes(bytes);
                    }

                    return bytes.Length > 0 ? bytes : null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: the crop could not be saved", ex);
                return null;
            }
        }

        private static async Task<IRandomAccessStream> OpenBytesAsync(byte[] bytes)
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            return stream;
        }

        private async Task SetPendingAsync(bool avatar, SocialProfileImage image)
        {
            var preview = new BitmapImage();
            preview.DecodePixelType = DecodePixelType.Logical;
            if (avatar)
            {
                preview.DecodePixelWidth = AvatarDecodeSize;
                preview.DecodePixelHeight = AvatarDecodeSize;
            }
            else
            {
                preview.DecodePixelWidth = BannerDecodeWidth;
            }

            try
            {
                using (var stream = await image.Open())
                {
                    await preview.SetSourceAsync(stream);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: no preview for the new image", ex);
                preview = null;
            }

            if (avatar)
            {
                _pendingAvatar = image;
                _avatarUrl = null;
                AvatarPicture.ProfilePicture = preview;
            }
            else
            {
                _pendingBanner = image;
                _bannerUrl = null;
                BannerPlaceholder.Fill = null;
                BannerImage.Fill = preview == null ? null : new ImageBrush { ImageSource = preview, Stretch = Stretch.UniformToFill };
            }

            StatusText.Visibility = Visibility.Collapsed;
            UpdateState();
        }
    }
}
