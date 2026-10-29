using System;
using System.Linq;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public static class AudioPlayerService
    {
        private static readonly object s_gate = new object();

        private static double s_volume = ReadVolume();

        private static bool s_muted;

        private static volatile AudioPlaybackSnapshot s_snapshot = new AudioPlaybackSnapshot(null, AudioPlaybackState.None, TimeSpan.Zero, TimeSpan.Zero, s_volume, false);

        private static Subscription[] s_subscribers = new Subscription[0];

        private static CoreDispatcher s_dispatcher;

        private static MediaPlayer s_player;

        private static MediaSource s_source;

        private static SocialDriveFile s_file;

        private static DispatcherTimer s_syncTimer;

        private static bool s_failed;

        private static bool s_ended;

        public static AudioPlaybackSnapshot Current
        {
            get { return s_snapshot; }
        }

        public static void Subscribe(Action<AudioPlaybackSnapshot> handler)
        {
            if (handler == null) return;

            var window = CoreWindow.GetForCurrentThread();
            if (window == null) return;

            var subscription = new Subscription(window.Dispatcher, ApplicationView.GetForCurrentView().Id, handler);
            lock (s_gate)
            {
                s_subscribers = s_subscribers.Concat(new[] { subscription }).ToArray();
            }
        }

        public static void Unsubscribe(Action<AudioPlaybackSnapshot> handler)
        {
            lock (s_gate)
            {
                s_subscribers = s_subscribers.Where(subscription => subscription.Handler != handler).ToArray();
            }
        }

        public static void ForgetView(int viewId)
        {
            lock (s_gate)
            {
                s_subscribers = s_subscribers.Where(subscription => subscription.ViewId != viewId).ToArray();
            }
        }

        public static void Play(SocialDriveFile file)
        {
            if (file == null) return;

            SocialInlineVideo.StopCurrentView();

            lock (s_gate)
            {
                if (s_dispatcher == null) s_dispatcher = WindowManagerService.MainDispatcher ?? CoreWindow.GetForCurrentThread()?.Dispatcher;
            }

            Post(() => StartOnMain(file));
        }

        public static void PlayOrToggle(SocialDriveFile file)
        {
            if (s_snapshot.IsFor(file)) Toggle();
            else Play(file);
        }

        public static void Toggle()
        {
            Post(ToggleOnMain);
        }

        public static void Pause()
        {
            Post(PauseOnMain);
        }

        public static void Stop()
        {
            Post(StopOnMain);
        }

        public static void Seek(TimeSpan position)
        {
            Post(() => SeekOnMain(position));
        }

        public static void SetVolume(double volume)
        {
            Post(() => SetVolumeOnMain(volume));
        }

        public static void SetMuted(bool muted)
        {
            Post(() => SetMutedOnMain(muted));
        }

        public static void OnEnteredBackground()
        {
            Post(PauseOnMain);
        }

        private static double ReadVolume()
        {
            var volume = AppSettings.ReadDouble(AppConstants.SettingAudioVolume, 1.0);
            return Math.Max(0, Math.Min(1, volume));
        }

        private static void Post(Action work)
        {
            CoreDispatcher dispatcher;
            lock (s_gate)
            {
                dispatcher = s_dispatcher;
            }

            if (dispatcher == null) return;

            if (dispatcher.HasThreadAccess)
            {
                Run(work);
                return;
            }

            var ignored = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Run(work));
        }

        private static void Run(Action work)
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                AppLog.Error("AudioPlayerService: a playback command failed", ex);
            }
        }

        private static void PostFor(MediaPlayer player, Action work)
        {
            Post(() =>
            {
                if (ReferenceEquals(player, s_player)) work();
            });
        }

        private static void StartOnMain(SocialDriveFile file)
        {
            ReleasePlayer();
            s_file = file;
            s_failed = false;
            s_ended = false;

            try
            {
                var uri = WebLauncher.TryCreateFetchUri(file.Url);
                if (uri == null)
                {
                    s_failed = true;
                    Publish();
                    return;
                }

                s_source = MediaSource.CreateFromUri(uri);
                var item = new MediaPlaybackItem(s_source);
                var properties = item.GetDisplayProperties();
                properties.Type = MediaPlaybackType.Music;
                properties.MusicProperties.Title = file.Name ?? "";
                item.ApplyDisplayProperties(properties);

                var player = new MediaPlayer { AutoPlay = true, Volume = s_volume, IsMuted = s_muted };
                player.MediaOpened += Player_MediaChanged;
                player.MediaEnded += Player_MediaEnded;
                player.MediaFailed += Player_MediaFailed;
                player.PlaybackSession.PlaybackStateChanged += Session_Changed;
                player.PlaybackSession.SeekCompleted += Session_Changed;
                player.PlaybackSession.NaturalDurationChanged += Session_Changed;
                s_player = player;

                if (s_syncTimer == null)
                {
                    s_syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    s_syncTimer.Tick += SyncTimer_Tick;
                }
                s_syncTimer.Start();

                player.Source = item;
                Publish();
            }
            catch (Exception ex)
            {
                AppLog.Error("AudioPlayerService: could not start playback", ex);
                ReleasePlayer();
                s_failed = true;
                Publish();
            }
        }

        private static void ToggleOnMain()
        {
            if (s_file == null) return;

            if (s_failed || s_player == null)
            {
                StartOnMain(s_file);
                return;
            }

            if (s_ended)
            {
                s_ended = false;
                s_player.PlaybackSession.Position = TimeSpan.Zero;
                s_player.Play();
                Publish();
                return;
            }

            if (IsWantedPlaying(s_player.PlaybackSession.PlaybackState)) s_player.Pause();
            else s_player.Play();
        }

        private static void PauseOnMain()
        {
            var player = s_player;
            if (player != null && IsWantedPlaying(player.PlaybackSession.PlaybackState)) player.Pause();
        }

        private static void StopOnMain()
        {
            ReleasePlayer();
            s_file = null;
            s_failed = false;
            s_ended = false;
            Publish();
        }

        private static void SeekOnMain(TimeSpan position)
        {
            var player = s_player;
            if (player == null) return;

            var duration = SaneDuration(player.PlaybackSession.NaturalDuration);
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;

            s_ended = false;
            player.PlaybackSession.Position = position;
            Publish();
        }

        private static void SetVolumeOnMain(double volume)
        {
            s_volume = Math.Max(0, Math.Min(1, volume));
            AppSettings.Write(AppConstants.SettingAudioVolume, s_volume);
            if (s_player != null) s_player.Volume = s_volume;
            Publish();
        }

        private static void SetMutedOnMain(bool muted)
        {
            s_muted = muted;
            if (s_player != null) s_player.IsMuted = muted;
            Publish();
        }

        private static void ReleasePlayer()
        {
            if (s_syncTimer != null) s_syncTimer.Stop();

            var player = s_player;
            var source = s_source;
            s_player = null;
            s_source = null;

            try
            {
                if (player != null)
                {
                    player.MediaOpened -= Player_MediaChanged;
                    player.MediaEnded -= Player_MediaEnded;
                    player.MediaFailed -= Player_MediaFailed;
                    player.PlaybackSession.PlaybackStateChanged -= Session_Changed;
                    player.PlaybackSession.SeekCompleted -= Session_Changed;
                    player.PlaybackSession.NaturalDurationChanged -= Session_Changed;
                    player.Pause();
                }

                if (source != null) source.Dispose();
                if (player != null) player.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("AudioPlayerService: could not release the player", ex);
            }
        }

        private static void Player_MediaChanged(MediaPlayer sender, object args)
        {
            PostFor(sender, Publish);
        }

        private static void Player_MediaEnded(MediaPlayer sender, object args)
        {
            PostFor(sender, () =>
            {
                s_ended = true;
                Publish();
            });
        }

        private static void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            AppLog.Warning($"AudioPlayerService: playback failed - {args.Error}: {args.ErrorMessage}");
            PostFor(sender, () =>
            {
                ReleasePlayer();
                s_failed = true;
                Publish();
            });
        }

        private static void Session_Changed(MediaPlaybackSession sender, object args)
        {
            try
            {
                var player = sender.MediaPlayer;
                PostFor(player, () =>
                {
                    if (s_ended && s_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) s_ended = false;
                    Publish();
                });
            }
            catch (Exception ex)
            {
                AppLog.Warning("AudioPlayerService: a playback session event arrived after the player was released - " + ex.Message);
            }
        }

        private static void SyncTimer_Tick(object sender, object e)
        {
            if (s_player != null && s_snapshot.State == AudioPlaybackState.Playing) s_snapshot = BuildSnapshot();
        }

        private static void Publish()
        {
            var snapshot = BuildSnapshot();
            s_snapshot = snapshot;

            foreach (var subscription in s_subscribers)
            {
                Deliver(subscription, snapshot);
            }
        }

        private static void Deliver(Subscription subscription, AudioPlaybackSnapshot snapshot)
        {
            try
            {
                if (subscription.Dispatcher.HasThreadAccess)
                {
                    subscription.Handler(snapshot);
                    return;
                }

                var ignored = subscription.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Invoke(subscription, snapshot));
            }
            catch (Exception ex)
            {
                AppLog.Warning("AudioPlayerService: a window did not take the update - " + ex.Message);
            }
        }

        private static void Invoke(Subscription subscription, AudioPlaybackSnapshot snapshot)
        {
            try
            {
                if (Array.IndexOf(s_subscribers, subscription) >= 0) subscription.Handler(snapshot);
            }
            catch (Exception ex)
            {
                AppLog.Error("AudioPlayerService: a subscriber failed", ex);
            }
        }

        private static AudioPlaybackSnapshot BuildSnapshot()
        {
            var file = s_file;
            if (file == null) return new AudioPlaybackSnapshot(null, AudioPlaybackState.None, TimeSpan.Zero, TimeSpan.Zero, s_volume, s_muted);

            var player = s_player;
            if (s_failed || player == null) return new AudioPlaybackSnapshot(file, AudioPlaybackState.Failed, TimeSpan.Zero, TimeSpan.Zero, s_volume, s_muted);

            var session = player.PlaybackSession;
            var duration = SaneDuration(session.NaturalDuration);
            if (s_ended) return new AudioPlaybackSnapshot(file, AudioPlaybackState.Ended, duration, duration, s_volume, s_muted);

            return new AudioPlaybackSnapshot(file, Map(session.PlaybackState), session.Position, duration, s_volume, s_muted);
        }

        private static AudioPlaybackState Map(MediaPlaybackState state)
        {
            switch (state)
            {
                case MediaPlaybackState.Playing: return AudioPlaybackState.Playing;
                case MediaPlaybackState.Buffering: return AudioPlaybackState.Buffering;
                case MediaPlaybackState.Paused: return AudioPlaybackState.Paused;
                default: return AudioPlaybackState.Opening;
            }
        }

        private static bool IsWantedPlaying(MediaPlaybackState state)
        {
            return state == MediaPlaybackState.Playing || state == MediaPlaybackState.Opening || state == MediaPlaybackState.Buffering;
        }

        private static TimeSpan SaneDuration(TimeSpan duration)
        {
            return duration > TimeSpan.Zero && duration < TimeSpan.FromHours(100) ? duration : TimeSpan.Zero;
        }

        private sealed class Subscription
        {
            public Subscription(CoreDispatcher dispatcher, int viewId, Action<AudioPlaybackSnapshot> handler)
            {
                Dispatcher = dispatcher;
                ViewId = viewId;
                Handler = handler;
            }

            public CoreDispatcher Dispatcher { get; private set; }

            public int ViewId { get; private set; }

            public Action<AudioPlaybackSnapshot> Handler { get; private set; }
        }
    }
}
