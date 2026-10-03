using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public sealed partial class SocialComposer
    {
        private const uint ThumbnailRequestSize = 160;

        private static readonly string[] s_pickerTypes =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".avif",
            ".mp4", ".mov", ".webm", ".m4v", ".mp3", ".m4a", ".wav", ".ogg", ".opus", ".flac", "*"
        };

        private static readonly Dictionary<string, string> s_uploadErrors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "MAX_FILE_SIZE_EXCEEDED", "SocialAttachmentTooLarge" },
            { "NO_FREE_SPACE", "SocialAttachmentDriveFull" },
            { "INAPPROPRIATE", "SocialAttachmentRefused" },
            { "INVALID_FILE_NAME", "SocialAttachmentBadName" }
        };

        private void Attachments_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_loading) OnEdited();
            else UpdateState();
        }

        private async void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };
                foreach (var type in s_pickerTypes)
                {
                    picker.FileTypeFilter.Add(type);
                }

                var files = await picker.PickMultipleFilesAsync();
                if (files != null && files.Count > 0) await AddItemsAsync(new List<IStorageItem>(files));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not pick files - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public async Task AddItemsAsync(IReadOnlyList<IStorageItem> items)
        {
            if (items == null) return;

            foreach (var file in items.OfType<StorageFile>())
            {
                if (_attachments.Count >= AppConstants.SocialAttachmentLimit)
                {
                    AutomationHelper.AnnounceStatus(this,
                                                    LocalizedStrings.Format("SocialAttachmentLimitFormat", AppConstants.SocialAttachmentLimit),
                                                    "SocialAttachmentLimit");
                    break;
                }

                ulong size = 0;
                try
                {
                    size = (await file.GetBasicPropertiesAsync()).Size;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialComposer: could not read a file's size - {ex.Message}");
                }

                var item = SocialAttachmentItem.ForUpload(file.Name, file.ContentType, size, async () => await file.OpenReadAsync());
                _attachments.Add(item);
                LoadFileThumbnail(item, file);

                if (size > AppConstants.SocialUploadLimitBytes)
                {
                    item.ErrorText = LocalizedStrings.Format("SocialAttachmentTooLargeFormat", AppConstants.SocialUploadLimitBytes / (1024 * 1024));
                    item.State = SocialAttachmentState.TooLarge;
                    UpdateState();
                    continue;
                }

                Upload(item);
            }
        }

        private static async void LoadFileThumbnail(SocialAttachmentItem item, StorageFile file)
        {
            try
            {
                using (var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, ThumbnailRequestSize))
                {
                    if (thumbnail != null && thumbnail.Type == ThumbnailType.Image) await item.LoadLocalThumbnailAsync(thumbnail);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: no thumbnail for {file.Name} - {ex.Message}");
            }
        }

        private async void Upload(SocialAttachmentItem item)
        {
            try
            {
                item.State = SocialAttachmentState.Uploading;
                item.Progress = 0;
                item.ErrorText = null;
                UpdateState();

                if (!await SocialPermissions.EnsureAsync(this, SocialPermissions.WriteDrive, SocialSignInPrompt.Post))
                {
                    Fail(item, "SocialAttachmentNeedsSignIn");
                    return;
                }

                var cancellation = new CancellationTokenSource();
                item.Cancellation = cancellation;

                SocialApiResult<SocialDriveFile> result;
                var token = await MisskeyAuthService.TryGetTokenAsync();
                using (var stream = await item.OpenAsync())
                {
                    var progress = new Progress<HttpProgress>(update =>
                    {
                        if (update.TotalBytesToSend.HasValue && update.TotalBytesToSend.Value > 0)
                        {
                            item.Progress = (double)update.BytesSent / update.TotalBytesToSend.Value;
                        }
                    });

                    result = await SocialApiService.UploadFileAsync(token, stream, item.Name, item.ContentType, false, progress, cancellation.Token);
                }

                if (!_attachments.Contains(item)) return;

                if (result.Status == SocialApiStatus.Ok)
                {
                    item.File = result.Value;
                    item.State = SocialAttachmentState.Done;
                    return;
                }

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SocialPermissions.Forget(SocialPermissions.WriteDrive);
                    Fail(item, "SocialAttachmentNeedsSignIn");
                    return;
                }

                string key;
                if (result.ErrorCode == null || !s_uploadErrors.TryGetValue(result.ErrorCode, out key)) key = "SocialAttachmentFailed";
                Fail(item, key);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialComposer: upload cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: upload failed - {ex.GetType().Name}: {ex.Message}");
                Fail(item, "SocialAttachmentFailed");
            }
            finally
            {
                item.Cancellation = null;
                UpdateState();
                if (!_loading) OnEdited();
            }
        }

        private void Fail(SocialAttachmentItem item, string messageKey)
        {
            item.ErrorText = LocalizedStrings.Get(messageKey);
            item.State = SocialAttachmentState.Failed;
            AutomationHelper.AnnounceStatus(this, item.AutomationName, "SocialAttachmentFailed");
        }

        private void CancelUploads()
        {
            foreach (var item in _attachments)
            {
                try
                {
                    if (item.Cancellation != null) item.Cancellation.Cancel();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialComposer: could not cancel an upload - {ex.Message}");
                }
            }
        }

        public void ReleaseUploads()
        {
            CancelUploads();
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as SocialAttachmentItem;
            if (item == null) return;

            try
            {
                if (item.Cancellation != null) item.Cancellation.Cancel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not cancel the upload - {ex.Message}");
            }

            _attachments.Remove(item);
            AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialAttachmentRemovedFormat", item.Name), "SocialAttachmentRemoved");
        }

        private void AttachmentTile_Click(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var item = element == null ? null : element.DataContext as SocialAttachmentItem;
            if (item == null) return;

            if (item.State == SocialAttachmentState.Failed)
            {
                Upload(item);
                return;
            }

            if (item.State == SocialAttachmentState.Done) ShowDescriptionFlyout(item, element);
        }

        private void OpenDescriptionFor(SocialAttachmentItem item)
        {
            var container = AttachmentList.ContainerFromItem(item) as FrameworkElement;
            if (container != null) ShowDescriptionFlyout(item, container);
        }

        private void ShowDescriptionFlyout(SocialAttachmentItem item, FrameworkElement anchor)
        {
            var file = item.File;
            if (file == null) return;

            var box = new TextBox
            {
                Header = LocalizedStrings.Get("SocialAttachmentDescriptionHeader"),
                PlaceholderText = LocalizedStrings.Get("SocialAttachmentDescriptionPlaceholder"),
                Text = file.Comment ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 96,
                MaxHeight = 200,
                Width = 300,
                MaxLength = AppConstants.SocialAltTextMaxLength,
                IsSpellCheckEnabled = true
            };

            var sensitive = new ToggleSwitch
            {
                Header = LocalizedStrings.Get("SocialAttachmentSensitiveHeader"),
                IsOn = file.IsSensitive
            };

            var save = new Button
            {
                Content = LocalizedStrings.Get("SocialAttachmentSaveButton"),
                Style = StyleOf("AccentButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(box);
            panel.Children.Add(sensitive);
            panel.Children.Add(save);

            var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Bottom };
            save.Tag = new DescriptionEditor(item, box, sensitive, flyout);
            save.Click += DescriptionSave_Click;

            flyout.ShowAt(anchor);
        }

        private async void DescriptionSave_Click(object sender, RoutedEventArgs e)
        {
            var save = sender as Button;
            var editor = save == null ? null : save.Tag as DescriptionEditor;
            if (editor == null) return;

            try
            {
                save.IsEnabled = false;
                if (await SaveDescriptionAsync(editor.Item, editor.Box.Text, editor.Sensitive.IsOn)) editor.Flyout.Hide();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not save the description - {ex.Message}");
            }
            finally
            {
                save.IsEnabled = true;
            }
        }

        private sealed class DescriptionEditor
        {
            public DescriptionEditor(SocialAttachmentItem item, TextBox box, ToggleSwitch sensitive, Flyout flyout)
            {
                Item = item;
                Box = box;
                Sensitive = sensitive;
                Flyout = flyout;
            }

            public SocialAttachmentItem Item { get; private set; }

            public TextBox Box { get; private set; }

            public ToggleSwitch Sensitive { get; private set; }

            public Flyout Flyout { get; private set; }
        }

        private async Task<bool> SaveDescriptionAsync(SocialAttachmentItem item, string description, bool sensitive)
        {
            var file = item.File;
            if (file == null) return true;

            var trimmed = (description ?? "").Trim();
            if (string.Equals(trimmed, (file.Comment ?? "").Trim(), StringComparison.Ordinal) && sensitive == file.IsSensitive) return true;

            if (!await SocialPermissions.EnsureAsync(this, SocialPermissions.WriteDrive, SocialSignInPrompt.Post)) return false;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.UpdateFileAsync(token, file.Id, trimmed, sensitive, CancellationToken.None);
            if (result.Status == SocialApiStatus.Ok)
            {
                item.File = result.Value;
                OnEdited();
                AutomationHelper.AnnounceStatus(this, LocalizedStrings.Get("SocialAttachmentDescriptionSaved"), "SocialAttachmentDescribed");
                return true;
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(this, SocialPermissions.WriteDrive, SocialSignInPrompt.Post);
                return false;
            }

            await DialogService.ShowMessageAsync(this,
                                                 LocalizedStrings.Get("SocialAttachmentDescriptionFailedTitle"),
                                                 LocalizedStrings.Get("SocialActionErrorGeneric"),
                                                 LocalizedStrings.Get("DialogOk"));
            return false;
        }

        private static Style StyleOf(string key)
        {
            return SocialNoteView.StyleOf(key);
        }

        private void Editor_Paste(object sender, TextControlPasteEventArgs e)
        {
            try
            {
                if (_attachments.Count >= AppConstants.SocialAttachmentLimit) return;

                var view = Clipboard.GetContent();
                if (view.Contains(StandardDataFormats.StorageItems))
                {
                    e.Handled = true;
                    PasteItems(view);
                }
                else if (view.Contains(StandardDataFormats.Bitmap))
                {
                    e.Handled = true;
                    PasteBitmap(view);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not read the clipboard - {ex.Message}");
            }
        }

        private async void PasteItems(DataPackageView view)
        {
            try
            {
                var items = await view.GetStorageItemsAsync();
                await AddItemsAsync(items);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not paste the files - {ex.Message}");
            }
        }

        private async void PasteBitmap(DataPackageView view)
        {
            try
            {
                await AddBitmapAsync(await view.GetBitmapAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: could not paste the image - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public async Task AddBitmapAsync(RandomAccessStreamReference reference)
        {
            if (reference == null) return;

            byte[] bytes;
            using (var source = await reference.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(source);
                using (var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                using (var output = new InMemoryRandomAccessStream())
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                    encoder.SetSoftwareBitmap(pixels);
                    await encoder.FlushAsync();

                    bytes = new byte[output.Size];
                    output.Seek(0);
                    await output.ReadAsync(bytes.AsBuffer(), (uint)output.Size, InputStreamOptions.None);
                }
            }

            if (_attachments.Count >= AppConstants.SocialAttachmentLimit) return;

            var name = DateTimeOffset.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) + ".png";
            var item = SocialAttachmentItem.ForUpload(name, "image/png", (ulong)bytes.Length, () => OpenBytesAsync(bytes));
            _attachments.Add(item);

            using (var preview = await OpenBytesAsync(bytes))
            {
                await item.LoadLocalThumbnailAsync(preview);
            }

            if ((ulong)bytes.Length > AppConstants.SocialUploadLimitBytes)
            {
                item.ErrorText = LocalizedStrings.Format("SocialAttachmentTooLargeFormat", AppConstants.SocialUploadLimitBytes / (1024 * 1024));
                item.State = SocialAttachmentState.TooLarge;
                UpdateState();
                return;
            }

            Upload(item);
        }

        private static async Task<IRandomAccessStream> OpenBytesAsync(byte[] bytes)
        {
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            return stream;
        }

        private void Composer_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                if (_posting || _attachments.Count >= AppConstants.SocialAttachmentLimit) return;
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = LocalizedStrings.Get("SocialComposeDropCaption");
                e.DragUIOverride.IsCaptionVisible = true;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: drag over failed - {ex.Message}");
            }
        }

        private async void Composer_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (_posting || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

                e.Handled = true;
                var deferral = e.GetDeferral();
                IReadOnlyList<IStorageItem> items;
                try
                {
                    items = await e.DataView.GetStorageItemsAsync();
                }
                finally
                {
                    deferral.Complete();
                }

                if (_context.Mode == SocialComposerMode.Reply) SetExpanded(true);
                await AddItemsAsync(items);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposer: drop failed - {ex.Message}");
            }
        }
    }
}
