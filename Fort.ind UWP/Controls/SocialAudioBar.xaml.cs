using System;
using System.Globalization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class SocialAudioBar : UserControl
    {
        private const int PositionTickMilliseconds = 250;

        private readonly Action<AudioPlaybackSnapshot> _handler;

        private readonly DispatcherTimer _timer;

        private SocialDriveFile _file;

        private SocialDriveFile _shown;

        private bool _hidden;

        private bool _isMini;

        private bool _subscribed;

        private bool _updating;

        private bool _dragging;

        private bool _volumeDragging;

        private double? _pendingSeek;

        public SocialAudioBar()
        {
            this.InitializeComponent();

            _handler = OnPlayerChanged;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PositionTickMilliseconds) };
            _timer.Tick += Timer_Tick;

            AddDragHandlers(SeekSlider);
            AddDragHandlers(VolumeSlider);

            Label(PlayButton, "SocialAudioPlay");
            Label(VolumeButton, "SocialAudioVolume");
            Label(MuteToggle, "SocialAudioMute");
            Label(CloseButton, "SocialAudioClose");
            AutomationProperties.SetName(SeekSlider, LocalizedStrings.Get("SocialAudioSeek"));
            AutomationProperties.SetName(VolumeSlider, LocalizedStrings.Get("SocialAudioVolume"));

            FailedText.Text = LocalizedStrings.Get("SocialAudioFailed");
            OpenButton.Content = LocalizedStrings.Get("SocialMediaOpenInBrowser");
            HiddenTitle.Text = LocalizedStrings.Get("SocialSensitiveMedia");
            HiddenHint.Text = LocalizedStrings.Get("SocialSensitiveMediaHint");
            AutomationProperties.SetName(HiddenButton,
                                         LocalizedStrings.Format("SocialMediaSensitiveAutomationFormat", LocalizedStrings.Get("SocialMediaAudioAutomation")));

            Loaded += SocialAudioBar_Loaded;
            Unloaded += SocialAudioBar_Unloaded;
        }

        public event EventHandler RevealRequested;

        public bool IsMini
        {
            get { return _isMini; }
            set
            {
                _isMini = value;
                CloseButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                Root.BorderThickness = value ? new Thickness(0, 1, 0, 0) : new Thickness(0);
            }
        }

        public void Show(SocialDriveFile file, bool hidden)
        {
            _file = file;
            _hidden = hidden;
            Apply(AudioPlayerService.Current);
        }

        public void Clear()
        {
            _file = null;
            _shown = null;
            _hidden = false;
            _timer.Stop();
        }

        private static void Label(FrameworkElement element, string key)
        {
            var text = LocalizedStrings.Get(key);
            AutomationProperties.SetName(element, text);
            ToolTipService.SetToolTip(element, text);
        }

        private void AddDragHandlers(Slider slider)
        {
            slider.AddHandler(PointerPressedEvent, new PointerEventHandler(Slider_PointerPressed), true);
            slider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Slider_PointerEnded), true);
            slider.AddHandler(PointerCanceledEvent, new PointerEventHandler(Slider_PointerEnded), true);
            slider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Slider_PointerEnded), true);
        }

        private void SocialAudioBar_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_subscribed)
                {
                    AudioPlayerService.Subscribe(_handler);
                    _subscribed = true;
                }

                Apply(AudioPlayerService.Current);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialAudioBar: could not start listening", ex);
            }
        }

        private void SocialAudioBar_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_subscribed)
            {
                AudioPlayerService.Unsubscribe(_handler);
                _subscribed = false;
            }

            _timer.Stop();
        }

        private void OnPlayerChanged(AudioPlaybackSnapshot snapshot)
        {
            Apply(snapshot);
        }

        private void Apply(AudioPlaybackSnapshot snapshot)
        {
            var file = _isMini ? snapshot.File : _file;
            var current = file != null && snapshot.IsFor(file);
            var state = current ? snapshot.State : AudioPlaybackState.None;
            _shown = file;

            var showHidden = !_isMini && _hidden && _file != null;
            HiddenButton.Visibility = showHidden ? Visibility.Visible : Visibility.Collapsed;
            PlayerPanel.Visibility = showHidden ? Visibility.Collapsed : Visibility.Visible;
            if (showHidden)
            {
                _timer.Stop();
                return;
            }

            var failed = state == AudioPlaybackState.Failed;
            NormalPanel.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
            FailedPanel.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;

            TitleText.Text = file == null ? "" : (file.Name ?? "");
            ToolTipService.SetToolTip(TitleText, file == null || string.IsNullOrWhiteSpace(file.Comment) ? null : file.Comment);
            AutomationProperties.SetName(Root, file == null ? "" : LocalizedStrings.Format("SocialAudioAutomationFormat", file.Name ?? ""));

            var wantsPlaying = snapshot.IsWantedPlaying && current;
            PlayGlyph.Glyph = wantsPlaying ? "" : "";
            Label(PlayButton, wantsPlaying ? "SocialAudioPause" : "SocialAudioPlay");
            PlayButton.IsEnabled = file != null;

            ApplyVolume(snapshot);
            ApplyDuration(current ? snapshot : null);
            UpdatePosition(current ? snapshot : null);

            if (state == AudioPlaybackState.Playing) _timer.Start();
            else _timer.Stop();
        }

        private void ApplyVolume(AudioPlaybackSnapshot snapshot)
        {
            _updating = true;
            try
            {
                if (!_volumeDragging) VolumeSlider.Value = snapshot.Volume * 100;
                MuteToggle.IsChecked = snapshot.IsMuted;
            }
            finally
            {
                _updating = false;
            }

            VolumeGlyph.Glyph = snapshot.IsMuted || snapshot.Volume <= 0 ? "" : "";
        }

        private void ApplyDuration(AudioPlaybackSnapshot snapshot)
        {
            var hasDuration = snapshot != null && snapshot.Duration > TimeSpan.Zero;
            _updating = true;
            try
            {
                SeekSlider.IsEnabled = hasDuration;
                SeekSlider.Maximum = hasDuration ? Math.Max(1, snapshot.Duration.TotalSeconds) : 1;
            }
            finally
            {
                _updating = false;
            }
        }

        private void UpdatePosition(AudioPlaybackSnapshot snapshot)
        {
            if (_dragging) return;

            var position = snapshot == null ? TimeSpan.Zero : snapshot.PositionNow();
            var duration = snapshot == null ? TimeSpan.Zero : snapshot.Duration;

            _updating = true;
            try
            {
                SeekSlider.Value = Math.Min(position.TotalSeconds, SeekSlider.Maximum);
            }
            finally
            {
                _updating = false;
            }

            TimeText.Text = FormatTime(position, duration);
        }

        private static string FormatTime(TimeSpan position, TimeSpan duration)
        {
            return duration > TimeSpan.Zero
                   ? LocalizedStrings.Format("SocialAudioTimeFormat", Clock(position), Clock(duration))
                   : Clock(position);
        }

        private static string Clock(TimeSpan time)
        {
            return time.TotalHours >= 1
                   ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                   : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private void Timer_Tick(object sender, object e)
        {
            try
            {
                var snapshot = AudioPlayerService.Current;
                if (_shown == null || !snapshot.IsFor(_shown) || snapshot.State != AudioPlaybackState.Playing)
                {
                    _timer.Stop();
                    return;
                }

                UpdatePosition(snapshot);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialAudioBar: could not update the position", ex);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMini)
            {
                AudioPlayerService.Toggle();
                return;
            }

            if (_file != null) AudioPlayerService.PlayOrToggle(_file);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayerService.Stop();
        }

        private void HiddenButton_Click(object sender, RoutedEventArgs e)
        {
            RevealRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = _shown;
                if (file != null) await WebLauncher.LaunchAsync(file.Url);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialAudioBar: could not open the audio", ex);
            }
        }

        private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updating || _shown == null) return;

            var snapshot = AudioPlayerService.Current;
            if (!snapshot.IsFor(_shown)) return;

            var target = TimeSpan.FromSeconds(e.NewValue);
            if (_dragging)
            {
                _pendingSeek = e.NewValue;
                TimeText.Text = FormatTime(target, snapshot.Duration);
                return;
            }

            AudioPlayerService.Seek(target);
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updating) return;

            AudioPlayerService.SetVolume(e.NewValue / 100);
            if (e.NewValue > 0 && MuteToggle.IsChecked.GetValueOrDefault()) AudioPlayerService.SetMuted(false);
        }

        private void MuteToggle_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayerService.SetMuted(MuteToggle.IsChecked.GetValueOrDefault());
        }

        private void Slider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (ReferenceEquals(sender, SeekSlider)) _dragging = true;
            else _volumeDragging = true;
        }

        private void Slider_PointerEnded(object sender, PointerRoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, SeekSlider))
            {
                _volumeDragging = false;
                return;
            }

            if (!_dragging) return;

            _dragging = false;
            var pending = _pendingSeek;
            _pendingSeek = null;
            if (pending.HasValue) AudioPlayerService.Seek(TimeSpan.FromSeconds(pending.Value));
        }
    }
}
