using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public sealed partial class SocialChatView : UserControl
    {
        private const uint ThumbnailRequestSize = 80;

        private SocialAttachmentItem _attachment;

        private bool AttachmentBlocksSending
        {
            get { return _attachment != null && (_attachment.State != SocialAttachmentState.Done || _attachment.File == null); }
        }

        private bool HasReadyAttachment
        {
            get { return _attachment != null && !AttachmentBlocksSending; }
        }

        private void AttachDropHandlers()
        {
            AddHandler(DragOverEvent, new DragEventHandler(View_DragOver), true);
            AddHandler(DropEvent, new DragEventHandler(View_Drop), true);
        }

        private async void AttachFromPcItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };
                foreach (var type in SocialComposer.s_pickerTypes)
                {
                    picker.FileTypeFilter.Add(type);
                }

                var file = await picker.PickSingleFileAsync();
                if (file != null) await AttachStorageItemsAsync(new List<IStorageItem> { file });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not pick a file", ex);
            }
        }

        private async void AttachFromDriveItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var attached = _attachment != null && _attachment.File != null ? new[] { _attachment.File.Id } : new string[0];
                var files = await SocialComposer.ShowDrivePickerAsync(this, attached, 1);
                if (files == null || files.Count == 0) return;

                SetAttachment(SocialAttachmentItem.ForFile(files[0]));
                ChatInput.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not attach from the drive", ex);
            }
        }

        private void ChatInput_Paste(object sender, TextControlPasteEventArgs e)
        {
            try
            {
                if (!CanWrite) return;

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
                AppLog.Error("SocialChatView: could not read the clipboard", ex);
            }
        }

        private async void PasteItems(DataPackageView view)
        {
            try
            {
                await AttachStorageItemsAsync(await view.GetStorageItemsAsync());
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not paste the file", ex);
            }
        }

        private async void PasteBitmap(DataPackageView view)
        {
            try
            {
                var reference = await view.GetBitmapAsync();
                if (reference == null) return;

                var bytes = await SocialComposer.PngBytesAsync(reference);
                var item = SocialAttachmentItem.ForUpload(SocialComposer.PastedImageName(), "image/png", (ulong)bytes.Length,
                                                          () => SocialComposer.OpenBytesAsync(bytes));
                SetAttachment(item);

                using (var preview = await SocialComposer.OpenBytesAsync(bytes))
                {
                    await item.LoadLocalThumbnailAsync(preview);
                }

                StartUpload(item);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not paste the image", ex);
            }
        }

        private void View_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                if (!CanWrite || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = LocalizedStrings.Get("SocialComposeDropCaption");
                e.DragUIOverride.IsCaptionVisible = true;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: drag over failed", ex);
            }
        }

        private async void View_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!CanWrite || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

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

                await AttachStorageItemsAsync(items);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: drop failed", ex);
            }
        }

        private async Task AttachStorageItemsAsync(IReadOnlyList<IStorageItem> items)
        {
            var files = items == null ? new List<StorageFile>() : items.OfType<StorageFile>().ToList();
            if (files.Count == 0) return;

            if (files.Count > 1) AutomationHelper.AnnounceStatus(this, LocalizedStrings.Get("SocialChatOneFileNotice"), "SocialChatOneFile");

            var file = files[0];
            ulong size = 0;
            try
            {
                size = (await file.GetBasicPropertiesAsync()).Size;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not read a file's size", ex);
            }

            var item = SocialAttachmentItem.ForUpload(file.Name, file.ContentType, size, async () => await file.OpenReadAsync());
            SetAttachment(item);
            LoadFileThumbnail(item, file);
            StartUpload(item);
            ChatInput.Focus(FocusState.Programmatic);
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
                AppLog.Error($"SocialChatView: no thumbnail for {file.Name}", ex);
            }
        }

        private void StartUpload(SocialAttachmentItem item)
        {
            if (item.Size > SocialPolicies.Current.UploadLimitBytes)
            {
                item.ErrorText = LocalizedStrings.Format("SocialAttachmentTooLargeFormat", SocialPolicies.Current.UploadLimitMegabytes);
                item.State = SocialAttachmentState.TooLarge;
                return;
            }

            Upload(item);
        }

        private async void Upload(SocialAttachmentItem item)
        {
            try
            {
                item.State = SocialAttachmentState.Uploading;
                item.Progress = 0;
                item.ErrorText = null;

                if (!await SocialPermissions.EnsureAsync(this, SocialPermissions.WriteDrive, SocialSignInPrompt.Chat))
                {
                    FailAttachment(item, "SocialAttachmentNeedsSignIn");
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

                if (!ReferenceEquals(_attachment, item)) return;

                if (result.Status == SocialApiStatus.Ok)
                {
                    item.File = result.Value;
                    item.State = SocialAttachmentState.Done;
                    AutomationHelper.AnnounceStatus(this, LocalizedStrings.Format("SocialChatAttachmentReadyFormat", item.Name), "SocialChatAttachment");
                    return;
                }

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    SocialPermissions.Forget(SocialPermissions.WriteDrive);
                    FailAttachment(item, "SocialAttachmentNeedsSignIn");
                    return;
                }

                FailAttachment(item, SocialComposer.UploadErrorKeyFor(result.ErrorCode));
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("SocialChatView: upload cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: upload failed", ex);
                if (ReferenceEquals(_attachment, item)) FailAttachment(item, "SocialAttachmentFailed");
            }
            finally
            {
                item.Cancellation = null;
                if (ReferenceEquals(_attachment, item)) UpdateSendButton();
            }
        }

        private void FailAttachment(SocialAttachmentItem item, string messageKey)
        {
            item.ErrorText = LocalizedStrings.Get(messageKey);
            item.State = SocialAttachmentState.Failed;
            AutomationHelper.AnnounceStatus(this, item.AutomationName, "SocialChatAttachmentFailed");
        }

        private void SetAttachment(SocialAttachmentItem item)
        {
            ClearAttachment(true);

            _attachment = item;
            if (item != null) item.PropertyChanged += Attachment_PropertyChanged;
            PaintAttachment();
            UpdateSendButton();
        }

        private void ClearAttachment(bool cancelUpload)
        {
            var previous = _attachment;
            _attachment = null;
            if (previous != null)
            {
                previous.PropertyChanged -= Attachment_PropertyChanged;
                if (cancelUpload)
                {
                    var cancellation = previous.Cancellation;
                    previous.Cancellation = null;
                    if (cancellation != null) cancellation.Cancel();
                }
            }

            PaintAttachment();
            UpdateSendButton();
        }

        private void Attachment_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (!ReferenceEquals(sender, _attachment)) return;

                PaintAttachment();
                if (e.PropertyName == "State" || e.PropertyName == "File") UpdateSendButton();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatView: could not update the attachment", ex);
            }
        }

        private void PaintAttachment()
        {
            var item = _attachment;
            AttachmentPanel.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            if (item == null)
            {
                AttachmentThumbnail.Source = null;
                return;
            }

            AttachmentName.Text = item.Name ?? "";
            AttachmentThumbnail.Source = item.Thumbnail;
            AttachmentGlyph.Visibility = item.Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;

            AttachmentProgress.Visibility = item.IsUploading ? Visibility.Visible : Visibility.Collapsed;
            AttachmentProgress.IsIndeterminate = item.IsUploading && item.Progress <= 0;
            AttachmentProgress.Value = item.Progress;

            AttachmentStatus.Text = item.HasError ? item.ErrorText ?? "" : "";
            AttachmentStatus.Visibility = item.HasError ? Visibility.Visible : Visibility.Collapsed;

            AutomationProperties.SetName(AttachmentPanel, item.AutomationName);
            AutomationProperties.SetName(RemoveAttachmentButton, LocalizedStrings.Format("SocialChatRemoveAttachmentFormat", item.Name ?? ""));
        }

        private void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
        {
            ClearAttachment(true);
            ChatInput.Focus(FocusState.Programmatic);
        }

        private void AttachmentPanel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = _attachment;
            if (item == null || item.State != SocialAttachmentState.Failed || item.OpenAsync == null) return;
            if (e.OriginalSource is FrameworkElement source && VisualTreeSearch.IsDescendantOf(source, RemoveAttachmentButton)) return;

            Upload(item);
        }
    }
}
