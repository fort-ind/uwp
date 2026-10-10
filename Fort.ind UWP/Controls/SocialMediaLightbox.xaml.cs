using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed partial class SocialMediaLightbox : UserControl
    {
        private const string OpenAnimationKey = "SocialLightboxOpen";

        private const string CloseAnimationKey = "SocialLightboxClose";

        private const float MaximumZoom = 8f;

        private const float ZoomStep = 1.25f;

        private const int BlurhashSize = 32;

        [ThreadStatic]
        private static SocialMediaLightbox t_current;

        private static SocialMediaLightbox ShownInWindow
        {
            get => t_current;
            set => t_current = value;
        }

        private readonly List<LightboxSlot> _slots = new List<LightboxSlot>();

        private readonly Popup _popup;

        private SocialNoteView _owner;

        private SocialNoteItem _item;

        private Control _returnFocus;

        private FocusState _focusState = FocusState.Programmatic;

        private ConnectedAnimation _pendingOpen;

        private bool _selectionReady;

        private bool _closing;

        private bool _finished;

        private SocialMediaLightbox()
        {
            this.InitializeComponent();

            _popup = new Popup { Child = this };

            AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, CloseAccelerator_Invoked);
            AddAccelerator((VirtualKey)187, VirtualKeyModifiers.Control, ZoomInAccelerator_Invoked);
            AddAccelerator(VirtualKey.Add, VirtualKeyModifiers.Control, ZoomInAccelerator_Invoked);
            AddAccelerator((VirtualKey)189, VirtualKeyModifiers.Control, ZoomOutAccelerator_Invoked);
            AddAccelerator(VirtualKey.Subtract, VirtualKeyModifiers.Control, ZoomOutAccelerator_Invoked);
            AddAccelerator(VirtualKey.Number0, VirtualKeyModifiers.Control, ZoomResetAccelerator_Invoked);

            Loaded += SocialMediaLightbox_Loaded;
        }

        public static bool Show(SocialNoteView owner, SocialNoteItem item, int fileIndex, FocusState focusState)
        {
            if (owner == null || item == null || ShownInWindow != null) return false;

            var lightbox = new SocialMediaLightbox();
            return lightbox.Open(owner, item, fileIndex, focusState);
        }

        public static bool CloseCurrent()
        {
            var current = ShownInWindow;
            if (current == null) return false;

            current.Close(true);
            return true;
        }

        public static void CloseFor(SocialNoteView owner)
        {
            var current = ShownInWindow;
            if (current != null && ReferenceEquals(current._owner, owner)) current.Close(false);
        }

        private bool Open(SocialNoteView owner, SocialNoteItem item, int fileIndex, FocusState focusState)
        {
            var files = item.Media;
            var selected = -1;
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file.Kind != SocialDriveFileKind.Image && file.Kind != SocialDriveFileKind.Gif) continue;

                if (i == fileIndex) selected = _slots.Count;

                var slot = new LightboxSlot(this, file, i, owner.MediaPlaceholderFor(item, i));
                _slots.Add(slot);
                MediaFlipView.Items.Add(slot.Root);
            }

            if (selected < 0) return false;

            _owner = owner;
            _item = item;
            RequestedTheme = owner.ActualTheme;
            _focusState = focusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
            _returnFocus = FocusManager.GetFocusedElement() as Control;

            MediaFlipView.SelectedIndex = selected;
            _selectionReady = true;
            ShowSlot(selected, false);

            UpdateBounds();
            Window.Current.SizeChanged += Window_SizeChanged;

            var anchor = owner.MediaAnchorFor(item, fileIndex);
            if (anchor != null)
            {
                _pendingOpen = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(OpenAnimationKey, anchor);
                _pendingOpen.Configuration = new GravityConnectedAnimationConfiguration();
            }
            else
            {
                MediaFlipView.Opacity = 0;
            }

            ShownInWindow = this;
            _popup.IsOpen = true;

            Fade(true, anchor == null ? MediaFlipView : null);
            return true;
        }

        private void Close(bool animate)
        {
            if (_closing) return;
            _closing = true;

            if (ReferenceEquals(ShownInWindow, this)) ShownInWindow = null;

            try
            {
                Window.Current.SizeChanged -= Window_SizeChanged;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: could not detach from the window", ex);
            }

            CancelPendingOpen();

            if (!animate || !_popup.IsOpen)
            {
                Finish();
                return;
            }

            try
            {
                var flew = TryFlyBack();
                if (flew) MediaFlipView.Opacity = 0;

                var storyboard = Fade(false, flew ? null : MediaFlipView);
                storyboard.Completed += CloseStoryboard_Completed;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: close animation failed", ex);
                Finish();
            }
        }

        private bool TryFlyBack()
        {
            var slot = CurrentSlot();
            if (slot == null || slot.IsZoomed || slot.Picture.ActualWidth <= 0) return false;

            var anchor = _owner == null ? null : _owner.MediaAnchorFor(_item, slot.FileIndex);
            if (anchor == null) return false;

            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(CloseAnimationKey, slot.Picture);
            animation.Configuration = new DirectConnectedAnimationConfiguration();
            if (animation.TryStart(anchor)) return true;

            animation.Cancel();
            return false;
        }

        private void CloseStoryboard_Completed(object sender, object e)
        {
            Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;

            try
            {
                _popup.IsOpen = false;

                foreach (var slot in _slots)
                {
                    slot.Release();
                }
                MediaFlipView.Items.Clear();
                _slots.Clear();

                var returnFocus = _returnFocus;
                _returnFocus = null;
                if (returnFocus != null) returnFocus.Focus(_focusState);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: could not close", ex);
            }

            _owner = null;
            _item = null;
        }

        private void CancelPendingOpen()
        {
            var pending = _pendingOpen;
            _pendingOpen = null;
            if (pending == null) return;

            try
            {
                pending.Cancel();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: could not cancel the open animation", ex);
            }
        }

        private void OnSlotFitted(LightboxSlot slot)
        {
            var pending = _pendingOpen;
            if (pending == null || _closing || !ReferenceEquals(slot, CurrentSlot())) return;

            _pendingOpen = null;
            if (pending.TryStart(slot.Picture)) return;

            pending.Cancel();
            Fade(true, MediaFlipView);
        }

        private void SocialMediaLightbox_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                MediaFlipView.Focus(_focusState);

                var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    if (_pendingOpen == null) return;

                    CancelPendingOpen();
                    if (!_closing) Fade(true, MediaFlipView);
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: could not finish opening", ex);
            }
        }

        private Storyboard Fade(bool show, UIElement extra)
        {
            var duration = TimeSpan.FromMilliseconds(show ? AppConstants.SocialLightboxEnterMilliseconds
                                                          : AppConstants.SocialLightboxExitMilliseconds);
            var target = show ? 1.0 : 0.0;

            var storyboard = new Storyboard();
            AddFade(storyboard, Scrim, target, duration, show);
            AddFade(storyboard, TopBar, target, duration, show);
            AddFade(storyboard, CaptionScroller, target, duration, show);
            if (extra != null) AddFade(storyboard, extra, target, duration, show);
            storyboard.Begin();
            return storyboard;
        }

        private static void AddFade(Storyboard storyboard, UIElement element, double to, TimeSpan duration, bool decelerate)
        {
            var spline = decelerate
                         ? new KeySpline { ControlPoint1 = new Point(0.1, 0.9), ControlPoint2 = new Point(0.2, 1.0) }
                         : new KeySpline { ControlPoint1 = new Point(0.7, 0.0), ControlPoint2 = new Point(1.0, 0.5) };

            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(duration),
                Value = to,
                KeySpline = spline
            });

            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            try
            {
                UpdateBounds();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: could not follow the window size", ex);
            }
        }

        private void UpdateBounds()
        {
            var bounds = Window.Current.Bounds;
            Width = bounds.Width;
            Height = bounds.Height;

            var titleBar = CoreApplication.GetCurrentView().TitleBar;
            TitleBarRow.Height = new GridLength(titleBar.ExtendViewIntoTitleBar ? titleBar.Height : 0);
        }

        private void MediaFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_selectionReady || _closing) return;

            try
            {
                var index = MediaFlipView.SelectedIndex;
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (i != index) _slots[i].Leave();
                }

                ShowSlot(index, true);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialMediaLightbox: could not switch images", ex);
            }
        }

        private void ShowSlot(int index, bool announce)
        {
            if (index < 0 || index >= _slots.Count) return;

            _slots[index].Load();
            if (index + 1 < _slots.Count) _slots[index + 1].Load();
            if (index - 1 >= 0) _slots[index - 1].Load();

            var comment = _slots[index].File.Comment;
            CaptionText.Text = comment ?? "";
            CaptionScroller.Visibility = string.IsNullOrWhiteSpace(comment) ? Visibility.Collapsed : Visibility.Visible;
            CaptionScroller.ChangeView(null, 0, null, true);

            var position = LocalizedStrings.Format("SocialLightboxPositionFormat", index + 1, _slots.Count);
            PositionText.Text = _slots.Count > 1 ? position : "";

            if (announce && _slots.Count > 1)
            {
                AutomationHelper.AnnounceStatus(MediaFlipView, position, "SocialMediaPosition");
            }
        }

        private LightboxSlot CurrentSlot()
        {
            var index = MediaFlipView.SelectedIndex;
            return index >= 0 && index < _slots.Count ? _slots[index] : null;
        }

        private void Scrim_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            Close(true);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers,
                                    TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += handler;
            KeyboardAccelerators.Add(accelerator);
        }

        private void CloseAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            Close(true);
        }

        private void ZoomInAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var slot = CurrentSlot();
            args.Handled = slot != null && slot.Zoom(ZoomStep);
        }

        private void ZoomOutAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var slot = CurrentSlot();
            args.Handled = slot != null && slot.Zoom(1 / ZoomStep);
        }

        private void ZoomResetAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var slot = CurrentSlot();
            args.Handled = slot != null && slot.ResetZoom();
        }

        private sealed class LightboxSlot
        {
            private readonly SocialMediaLightbox _lightbox;

            private readonly Grid _content;

            private readonly ScrollViewer _scroller;

            private readonly Grid _canvas;

            private readonly Image _placeholder;

            private readonly Image _image;

            private Image _detail;

            private readonly ProgressRing _ring;

            private StackPanel _failure;

            private double _aspect;

            private double _naturalWidth;

            private double _naturalHeight;

            private bool _requested;

            private bool _usedFallback;

            private int _decodedWidth;

            public LightboxSlot(SocialMediaLightbox lightbox, SocialDriveFile file, int fileIndex, ImageSource placeholder)
            {
                _lightbox = lightbox;
                File = file;
                FileIndex = fileIndex;

                if (file.Width.HasValue && file.Height.HasValue)
                {
                    _naturalWidth = file.Width.Value;
                    _naturalHeight = file.Height.Value;
                    _aspect = _naturalHeight / _naturalWidth;
                }
                else
                {
                    var bitmap = placeholder as BitmapSource;
                    if (bitmap != null && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                    {
                        _aspect = (double)bitmap.PixelHeight / bitmap.PixelWidth;
                    }
                }

                _placeholder = new Image
                {
                    Stretch = Stretch.Fill,
                    Source = placeholder ?? BlurhashImage.CreateBitmap(file.Blurhash, BlurhashSize, BlurhashSize)
                };
                AutomationProperties.SetAccessibilityView(_placeholder, Windows.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

                _image = new Image { Stretch = Stretch.Uniform };
                AutomationProperties.SetName(_image, AutomationNameFor(file));

                Picture = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Picture.Children.Add(_placeholder);
                Picture.Children.Add(_image);

                _canvas = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
                _canvas.Children.Add(Picture);
                _canvas.Tapped += Canvas_Tapped;

                _scroller = new ScrollViewer
                {
                    ZoomMode = ZoomMode.Enabled,
                    MinZoomFactor = 1f,
                    MaxZoomFactor = MaximumZoom,
                    HorizontalScrollMode = ScrollMode.Auto,
                    VerticalScrollMode = ScrollMode.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    IsTabStop = true,
                    Content = _canvas
                };
                _scroller.SizeChanged += Scroller_SizeChanged;
                _scroller.ViewChanged += Scroller_ViewChanged;

                _ring = new ProgressRing
                {
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };

                _content = new Grid();
                _content.Children.Add(_scroller);
                _content.Children.Add(_ring);

                Root = new FlipViewItem { Content = _content };
                AutomationProperties.SetName(Root, AutomationNameFor(file));
            }

            public SocialDriveFile File { get; private set; }

            public int FileIndex { get; private set; }

            public FlipViewItem Root { get; private set; }

            public Grid Picture { get; private set; }

            public bool IsZoomed
            {
                get { return _scroller.ZoomFactor > 1.001f; }
            }

            public void Load()
            {
                if (_requested) return;
                _requested = true;

                var uri = WebLauncher.TryCreateFetchUri(File.Url);
                if (uri == null)
                {
                    ShowFailure();
                    return;
                }

                _ring.IsActive = true;
                _ring.Visibility = Visibility.Visible;
                _image.Source = CreateBitmap(uri, ScreenFitWidth());
            }

            public void Leave()
            {
                ResetZoom();
            }

            public bool Zoom(float factor)
            {
                if (_failure != null) return false;

                var target = Math.Max(1f, Math.Min(MaximumZoom, _scroller.ZoomFactor * factor));
                _scroller.ChangeView(null, null, target);
                return true;
            }

            public bool ResetZoom()
            {
                if (_failure != null) return false;

                _scroller.ChangeView(0, 0, 1f);
                return true;
            }

            public void Release()
            {
                _ring.IsActive = false;
                _image.Source = null;
                _placeholder.Source = null;
                DropDetail();
            }

            private BitmapImage CreateBitmap(Uri uri, int decodeWidth)
            {
                var bitmap = new BitmapImage();
                bitmap.ImageOpened += Bitmap_ImageOpened;
                bitmap.ImageFailed += Bitmap_ImageFailed;
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelType = DecodePixelType.Physical;
                    bitmap.DecodePixelWidth = decodeWidth;
                }
                _decodedWidth = decodeWidth;
                bitmap.UriSource = uri;
                return bitmap;
            }

            private int ScreenFitWidth()
            {
                if (File.Kind != SocialDriveFileKind.Image || _naturalWidth <= 0 || _naturalHeight <= 0) return 0;

                try
                {
                    var display = DisplayInformation.GetForCurrentView();
                    var screenWidth = (double)display.ScreenWidthInRawPixels;
                    var screenHeight = (double)display.ScreenHeightInRawPixels;
                    if (screenWidth <= 0 || screenHeight <= 0) return 0;

                    var scale = Math.Min(screenWidth / _naturalWidth, screenHeight / _naturalHeight);
                    return scale < 1 ? Math.Max(1, (int)Math.Round(_naturalWidth * scale)) : 0;
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialMediaLightbox: could not read the screen size", ex);
                    return 0;
                }
            }

            private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
            {
                if (e.IsIntermediate) return;

                try
                {
                    if (IsZoomed) LoadDetail();
                    else DropDetail();
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialMediaLightbox: could not follow the zoom", ex);
                }
            }

            private void LoadDetail()
            {
                if (_detail != null || _decodedWidth <= 0 || _usedFallback || _failure != null) return;

                var uri = WebLauncher.TryCreateFetchUri(File.Url);
                if (uri == null) return;

                var bitmap = new BitmapImage();
                bitmap.ImageFailed += Detail_ImageFailed;
                _detail = new Image { Stretch = Stretch.Uniform };
                AutomationProperties.SetAccessibilityView(_detail, Windows.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                Picture.Children.Add(_detail);
                _detail.Source = bitmap;
                bitmap.UriSource = uri;
            }

            private void DropDetail()
            {
                var detail = _detail;
                _detail = null;
                if (detail == null) return;

                detail.Source = null;
                Picture.Children.Remove(detail);
            }

            private void Detail_ImageFailed(object sender, ExceptionRoutedEventArgs e)
            {
                AppLog.Warning($"SocialMediaLightbox: full-size image failed - {e.ErrorMessage}");
                DropDetail();
            }

            private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
            {
                try
                {
                    _canvas.Width = e.NewSize.Width;
                    _canvas.Height = e.NewSize.Height;
                    Fit();
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialMediaLightbox: image layout failed", ex);
                }
            }

            private void Fit()
            {
                var width = _canvas.Width;
                var height = _canvas.Height;
                if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return;

                if (_aspect <= 0)
                {
                    Picture.Width = width;
                    Picture.Height = height;
                }
                else
                {
                    var naturalWidth = _naturalWidth > 0 ? _naturalWidth : width;
                    var naturalHeight = _naturalWidth > 0 ? _naturalHeight : width * _aspect;

                    var scale = Math.Min(width / naturalWidth, height / naturalHeight);
                    if (_naturalWidth > 0) scale = Math.Min(scale, 1.0);

                    Picture.Width = Math.Max(1, Math.Round(naturalWidth * scale));
                    Picture.Height = Math.Max(1, Math.Round(naturalHeight * scale));
                }

                _lightbox.OnSlotFitted(this);
            }

            private void Canvas_Tapped(object sender, TappedRoutedEventArgs e)
            {
                if (!ReferenceEquals(e.OriginalSource, _canvas)) return;

                e.Handled = true;
                _lightbox.Close(true);
            }

            private void Bitmap_ImageOpened(object sender, RoutedEventArgs e)
            {
                _ring.IsActive = false;
                _ring.Visibility = Visibility.Collapsed;
                _placeholder.Visibility = Visibility.Collapsed;

                var bitmap = sender as BitmapImage;
                if (_naturalWidth <= 0 && bitmap != null && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                {
                    _naturalWidth = bitmap.PixelWidth;
                    _naturalHeight = bitmap.PixelHeight;
                    _aspect = _naturalHeight / _naturalWidth;
                    Fit();
                }
            }

            private void Bitmap_ImageFailed(object sender, ExceptionRoutedEventArgs e)
            {
                AppLog.Warning($"SocialMediaLightbox: image failed - {e.ErrorMessage}");

                var fallback = _usedFallback ? null : WebLauncher.TryCreateFetchUri(File.ThumbnailUrl);
                if (fallback != null)
                {
                    _usedFallback = true;
                    DropDetail();
                    _image.Source = CreateBitmap(fallback, 0);
                    return;
                }

                ShowFailure();
            }

            private void ShowFailure()
            {
                if (_failure != null) return;

                _failure = new StackPanel
                {
                    Spacing = 12,
                    MaxWidth = 360,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _failure.Children.Add(new FontIcon
                {
                    Glyph = "\uE783",
                    FontSize = 32,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var message = new TextBlock
                {
                    Text = LocalizedStrings.Get("SocialLightboxImageFailed"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Style = StyleOf("BodyTextBlockStyle")
                };
                AutomationProperties.SetLiveSetting(message, Windows.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
                _failure.Children.Add(message);

                var open = new Button
                {
                    Content = LocalizedStrings.Get("SocialMediaOpenInBrowser"),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                open.Click += OpenButton_Click;
                _failure.Children.Add(open);

                _ring.IsActive = false;
                _ring.Visibility = Visibility.Collapsed;
                _scroller.Visibility = Visibility.Collapsed;
                _content.Children.Add(_failure);
                AutomationHelper.AnnounceLiveRegion(message);
            }

            private async void OpenButton_Click(object sender, RoutedEventArgs e)
            {
                try
                {
                    await WebLauncher.LaunchAsync(File.Url);
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialMediaLightbox: could not open the image", ex);
                }
            }

            private static string AutomationNameFor(SocialDriveFile file)
            {
                var kind = LocalizedStrings.Get(file.Kind == SocialDriveFileKind.Gif ? "SocialMediaGifAutomation" : "SocialMediaImageAutomation");

                return string.IsNullOrWhiteSpace(file.Comment)
                       ? kind
                       : LocalizedStrings.Format("SocialMediaItemAutomationFormat", kind, file.Comment);
            }

            private static Style StyleOf(string key)
            {
                try
                {
                    object value;
                    return Application.Current.Resources.TryGetValue(key, out value) ? value as Style : null;
                }
                catch (Exception ex)
                {
                    AppLog.Error($"SocialMediaLightbox: style {key} unavailable", ex);
                    return null;
                }
            }
        }
    }
}
