using System;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed class SocialInlineVideo
    {
        private const string TransportControlsStyleKey = "SocialMediaTransportControlsStyle";

        [ThreadStatic]
        private static SocialInlineVideo t_playing;

        private static SocialInlineVideo PlayingInWindow
        {
            get => t_playing;
            set => t_playing = value;
        }

        private readonly Grid _host;

        private readonly UIElement _cover;

        private Grid _frame;

        private MediaPlayerElement _element;

        private MediaPlayer _player;

        private MediaSource _source;

        private DisplayRequest _displayRequest;

        private bool _displayRequestActive;

        private FocusState _focusState = FocusState.Programmatic;

        private double _aspect;

        private long _fullWindowToken;

        public SocialInlineVideo(Grid host, UIElement cover)
        {
            _host = host;
            _cover = cover;
            _host.PointerPressed += Host_PointerPressed;
            _host.SizeChanged += Host_SizeChanged;
        }

        public SocialDriveFile File { get; private set; }

        public static void StopCurrentView()
        {
            var playing = PlayingInWindow;
            if (playing != null) playing.Stop();
        }

        public void Play(SocialDriveFile file, ImageSource poster, FocusState focusState)
        {
            if (file == null) return;

            var playing = PlayingInWindow;
            if (playing != null && !ReferenceEquals(playing, this)) playing.Stop();
            Stop();

            File = file;
            PlayingInWindow = this;
            _focusState = focusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;

            _host.Visibility = Visibility.Visible;
            _cover.IsHitTestVisible = false;

            var uri = WebLauncher.TryCreateFetchUri(file.Url);
            if (uri == null)
            {
                ShowFailure();
                return;
            }

            _aspect = file.Width.HasValue && file.Height.HasValue ? (double)file.Height.Value / file.Width.Value : 0;

            _source = MediaSource.CreateFromUri(uri);
            _player = new MediaPlayer { AutoPlay = true, Source = _source };
            _player.MediaFailed += Player_MediaFailed;
            _player.PlaybackSession.PlaybackStateChanged += Session_PlaybackStateChanged;
            _player.PlaybackSession.NaturalVideoSizeChanged += Session_NaturalVideoSizeChanged;

            _element = new MediaPlayerElement
            {
                AreTransportControlsEnabled = true,
                Stretch = Stretch.Uniform
            };
            var controls = new MediaTransportControls { IsCompact = true, IsZoomButtonVisible = false };
            var style = StyleOf(TransportControlsStyleKey);
            if (style != null) controls.Style = style;
            _element.TransportControls = controls;
            if (poster != null) _element.PosterSource = poster;
            AutomationProperties.SetName(_element, AutomationNameFor(file));

            _element.PreviewKeyDown += Element_PreviewKeyDown;
            _fullWindowToken = _element.RegisterPropertyChangedCallback(MediaPlayerElement.IsFullWindowProperty, Element_IsFullWindowChanged);
            _element.Loaded += Element_Loaded;
            _element.SetMediaPlayer(_player);
            _frame = new Grid();
            _frame.Children.Add(_element);
            _host.Children.Add(_frame);
            Fit();
        }

        public void Stop()
        {
            if (ReferenceEquals(PlayingInWindow, this)) PlayingInWindow = null;
            if (File == null && _host.Children.Count == 0) return;

            File = null;

            ExitFullWindow();
            ReleasePlayer();
            DetachElement();

            _host.Children.Clear();
            _host.Visibility = Visibility.Collapsed;
            _cover.Visibility = Visibility.Visible;
            _cover.IsHitTestVisible = true;
        }

        private void ReleasePlayer()
        {
            try
            {
                if (_player != null)
                {
                    _player.MediaFailed -= Player_MediaFailed;
                    _player.PlaybackSession.PlaybackStateChanged -= Session_PlaybackStateChanged;
                    _player.PlaybackSession.NaturalVideoSizeChanged -= Session_NaturalVideoSizeChanged;
                    _player.Pause();
                }

                if (_element != null) _element.SetMediaPlayer(null);

                if (_source != null)
                {
                    _source.Dispose();
                    _source = null;
                }

                if (_player != null)
                {
                    _player.Dispose();
                    _player = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: could not release the player", ex);
            }

            SetDisplayRequest(false);
        }

        private void Element_Loaded(object sender, RoutedEventArgs e)
        {
            var element = sender as MediaPlayerElement;
            if (element == null) return;

            element.Loaded -= Element_Loaded;

            var ignored = _host.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                try
                {
                    if (!ReferenceEquals(element, _element)) return;

                    var target = FocusManager.FindFirstFocusableElement(element) as Control;
                    if (target != null) target.Focus(_focusState);

                    _cover.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialInlineVideo: could not move focus into the player", ex);
                }
            });
        }

        private void Host_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                Fit();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: player layout failed", ex);
            }
        }

        private void Fit()
        {
            var frame = _frame;
            if (frame == null) return;

            var width = _host.ActualWidth;
            var height = _host.ActualHeight;
            if (_aspect <= 0 || width <= 0 || height <= 0)
            {
                frame.Width = double.NaN;
                frame.Height = double.NaN;
                frame.HorizontalAlignment = HorizontalAlignment.Stretch;
                frame.VerticalAlignment = VerticalAlignment.Stretch;
                return;
            }

            var fittedWidth = Math.Min(width, height / _aspect);
            frame.Width = Math.Max(1, Math.Round(fittedWidth));
            frame.Height = Math.Max(1, Math.Round(fittedWidth * _aspect));
            frame.HorizontalAlignment = HorizontalAlignment.Center;
            frame.VerticalAlignment = VerticalAlignment.Center;
        }

        private void Session_NaturalVideoSizeChanged(MediaPlaybackSession session, object args)
        {
            try
            {
                var player = session.MediaPlayer;
                var width = session.NaturalVideoWidth;
                var height = session.NaturalVideoHeight;
                if (width == 0 || height == 0) return;

                var ignored = _host.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!ReferenceEquals(player, _player)) return;

                        _aspect = (double)height / width;
                        Fit();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialInlineVideo: could not fit the video", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: video size handler failed", ex);
            }
        }

        private void Element_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Escape) return;

            e.Handled = ExitFullWindow();
        }

        private void Element_IsFullWindowChanged(DependencyObject sender, DependencyProperty property)
        {
            var element = sender as MediaPlayerElement;
            if (element == null || !element.IsFullWindow) return;

            var ignored = _host.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                try
                {
                    if (!ReferenceEquals(element, _element) || !element.IsFullWindow || ContainsFocus(element)) return;

                    var target = FocusManager.FindFirstFocusableElement(element) as Control;
                    if (target != null) target.Focus(FocusState.Programmatic);
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialInlineVideo: could not move focus into the full-window player", ex);
                }
            });
        }

        private bool ExitFullWindow()
        {
            try
            {
                if (_element == null || !_element.IsFullWindow) return false;

                _element.IsFullWindow = false;
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: could not leave full window", ex);
                return false;
            }
        }

        private void DetachElement()
        {
            var element = _element;
            _element = null;
            _frame = null;
            if (element == null) return;

            element.Loaded -= Element_Loaded;
            element.PreviewKeyDown -= Element_PreviewKeyDown;
            element.UnregisterPropertyChangedCallback(MediaPlayerElement.IsFullWindowProperty, _fullWindowToken);
        }

        private void Session_PlaybackStateChanged(MediaPlaybackSession session, object args)
        {
            try
            {
                var player = session.MediaPlayer;
                var playing = session.PlaybackState == MediaPlaybackState.Playing;
                var ignored = _host.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (ReferenceEquals(player, _player)) SetDisplayRequest(playing);
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: playback state handler failed", ex);
            }
        }

        private void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            try
            {
                AppLog.Warning($"SocialInlineVideo: playback failed - {args.Error}: {args.ErrorMessage}");
                var ignored = _host.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (ReferenceEquals(sender, _player)) ShowFailure();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialInlineVideo: could not show the playback failure", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: media failure handler failed", ex);
            }
        }

        private void ShowFailure()
        {
            var hadFocus = _element != null && ContainsFocus(_element);

            ExitFullWindow();
            ReleasePlayer();
            DetachElement();
            _host.Children.Clear();

            var panel = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(new FontIcon
            {
                Glyph = "\uE783",
                FontSize = 24,
                IsTextScaleFactorEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var message = new TextBlock
            {
                Text = LocalizedStrings.Get("SocialInlineVideoFailed"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Style = StyleOf("CaptionTextBlockStyle")
            };
            AutomationProperties.SetLiveSetting(message, Windows.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            panel.Children.Add(message);

            var open = new Button
            {
                Content = LocalizedStrings.Get("SocialMediaOpenInBrowser"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            open.Click += OpenButton_Click;
            panel.Children.Add(open);

            _host.Children.Add(panel);
            _cover.Visibility = Visibility.Collapsed;
            AutomationHelper.AnnounceLiveRegion(message);

            if (hadFocus) open.Loaded += OpenButton_Loaded;
        }

        private void OpenButton_Loaded(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            button.Loaded -= OpenButton_Loaded;
            button.Focus(_focusState);
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = File;
                if (file != null) await WebLauncher.LaunchAsync(file.Url);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: could not open the video", ex);
            }
        }

        private void SetDisplayRequest(bool active)
        {
            try
            {
                if (active == _displayRequestActive) return;

                if (_displayRequest == null) _displayRequest = new DisplayRequest();

                if (active)
                {
                    _displayRequest.RequestActive();
                }
                else
                {
                    _displayRequest.RequestRelease();
                }
                _displayRequestActive = active;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialInlineVideo: display request failed", ex);
            }
        }

        private static bool ContainsFocus(DependencyObject scope)
        {
            var current = FocusManager.GetFocusedElement() as DependencyObject;
            while (current != null)
            {
                if (ReferenceEquals(current, scope)) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private static string AutomationNameFor(SocialDriveFile file)
        {
            var kind = LocalizedStrings.Get("SocialMediaVideoAutomation");
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
                AppLog.Error($"SocialInlineVideo: style {key} unavailable", ex);
                return null;
            }
        }
    }
}
