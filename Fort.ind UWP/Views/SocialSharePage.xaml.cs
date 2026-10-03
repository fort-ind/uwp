using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialSharePage : Page
    {
        private ShareOperation _share;

        private Window _window;

        private FrameworkElement _windowRoot;

        private int _viewId;

        private bool _started;

        private bool _finished;

        private bool _released;

        public SocialSharePage()
        {
            this.InitializeComponent();
        }

        private sealed class SharedContent
        {
            public string Text { get; set; }

            public bool CaretAtStart { get; set; }

            public IReadOnlyList<IStorageItem> Items { get; set; }

            public RandomAccessStreamReference Bitmap { get; set; }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                _share = e.Parameter as ShareOperation;
                _window = Window.Current;
                _windowRoot = _window.Content as FrameworkElement;
                _viewId = ApplicationView.GetForCurrentView().Id;

                RootGrid.Background = AppearanceService.AttachWindowSurface(_windowRoot ?? this);
                ActualThemeChanged += OnActualThemeChanged;
                _window.Closed += Window_Closed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not set up the share window - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;

            Start();
        }

        private async void Start()
        {
            try
            {
                if (ProfileService.CurrentUser == null)
                {
                    await LocalStorageService.InitializeAsync();
                    await ProfileService.TryRestoreSessionAsync();
                }

                if (_released) return;

                if (ProfileService.CurrentUser == null)
                {
                    ShowMessage("SocialShareSignedOutTitle", "SocialShareSignedOutBody");
                    return;
                }

                if (!SocialPermissions.Has(SocialPermissions.WriteNotes) || !SocialPermissions.Has(SocialPermissions.WriteDrive))
                {
                    ShowMessage("SocialShareSignInAgainTitle", "SocialShareSignInAgainBody");
                    return;
                }

                var shared = await ReadSharedAsync();
                if (_released) return;

                var draft = SocialComposeDraft.Create(shared.Text, null, SocialComposePage.LastVisibility(), SocialComposePage.LastLocalOnly(),
                                                      null, null, null, null, null, null, null);
                Composer.Load(draft, new SocialComposeContext(SocialComposerMode.Share, null, null, null));
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                Composer.Visibility = Visibility.Visible;

                if (shared.Items != null && shared.Items.Count > 0)
                {
                    await Composer.AddItemsAsync(shared.Items);
                }
                else if (shared.Bitmap != null)
                {
                    await Composer.AddBitmapAsync(shared.Bitmap);
                }

                if (_released) return;
                ReportDataRetrieved();

                Composer.FocusEditorAt(shared.CaretAtStart ? 0 : int.MaxValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not open the share - {ex.GetType().Name}: {ex.Message}");
                ShowMessage("SocialShareFailedTitle", "SocialShareFailedBody");
            }
        }

        private async Task<SharedContent> ReadSharedAsync()
        {
            var shared = new SharedContent();
            if (_share == null) return shared;

            var data = _share.Data;
            string text = null;
            Uri link = null;
            string title = null;

            if (data.Contains(StandardDataFormats.WebLink))
            {
                link = await data.GetWebLinkAsync();
                title = data.Properties.Title;
            }

            if (data.Contains(StandardDataFormats.Text))
            {
                text = await data.GetTextAsync();
            }

            if (data.Contains(StandardDataFormats.StorageItems))
            {
                shared.Items = await data.GetStorageItemsAsync();
            }
            else if (data.Contains(StandardDataFormats.Bitmap))
            {
                shared.Bitmap = await data.GetBitmapAsync();
            }

            bool caretAtStart;
            shared.Text = ComposeText(text, link, title, out caretAtStart);
            shared.CaretAtStart = caretAtStart;
            return shared;
        }

        private static string ComposeText(string text, Uri link, string title, out bool caretAtStart)
        {
            var body = ToEditorLines((text ?? "").Trim());
            var url = link == null || !link.IsAbsoluteUri ? null : link.AbsoluteUri;

            if (url == null)
            {
                caretAtStart = false;
                return body;
            }

            var lines = new List<string>();
            if (body.Length > 0 && !string.Equals(body, url, StringComparison.Ordinal)) lines.Add(body);

            var heading = ToEditorLines((title ?? "").Trim());
            if (heading.Length > 0 && !string.Equals(heading, url, StringComparison.Ordinal)
                && body.IndexOf(heading, StringComparison.Ordinal) < 0)
            {
                lines.Add(heading);
            }

            lines.Add(url);

            caretAtStart = true;
            return "\r\r" + string.Join("\r", lines);
        }

        private static string ToEditorLines(string text)
        {
            return text.Replace("\r\n", "\r").Replace('\n', '\r');
        }

        private void ReportDataRetrieved()
        {
            try
            {
                if (_share != null) _share.ReportDataRetrieved();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not report the data retrieved - {ex.Message}");
            }
        }

        private void ShowMessage(string titleKey, string bodyKey)
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            Composer.Visibility = Visibility.Collapsed;

            MessageTitle.Text = LocalizedStrings.Get(titleKey);
            MessageBody.Text = LocalizedStrings.Get(bodyKey);
            MessagePanel.Visibility = Visibility.Visible;
            CloseButton.Focus(FocusState.Programmatic);
        }

        private void Composer_Posted(object sender, SocialNote note)
        {
            try
            {
                SocialComposePage.RememberVisibility(Composer.GetDraft());
                _finished = true;
                Release();

                if (_share != null) _share.ReportCompleted();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not complete the share - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Composer_Discarded(object sender, EventArgs e)
        {
            Dismiss();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Dismiss();
        }

        private void Dismiss()
        {
            try
            {
                Release();
                if (_share != null) _share.DismissUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not close the share - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                AppearanceService.RepaintWindowSurface(_windowRoot ?? this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not repaint the share window - {ex.Message}");
            }
        }

        private void Window_Closed(object sender, CoreWindowEventArgs e)
        {
            Release();
        }

        private void Release()
        {
            if (_released) return;
            _released = true;

            try
            {
                if (!_finished) Composer.ReleaseUploads();

                ActualThemeChanged -= OnActualThemeChanged;
                if (_window != null) _window.Closed -= Window_Closed;

                AppearanceService.DetachWindowSurface(_windowRoot ?? this);
                SocialEmojiPicker.ForgetCurrentView();
                SocialNoteHub.ForgetView(_viewId);
                DialogService.ForgetView(_viewId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialSharePage: could not let go of the share window - {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
