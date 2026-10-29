using System;
using System.Diagnostics;

namespace Fort.ind_UWP
{
    public enum AudioPlaybackState
    {
        None,
        Opening,
        Playing,
        Buffering,
        Paused,
        Ended,
        Failed
    }

    public sealed class AudioPlaybackSnapshot
    {
        private readonly long _stamp;

        private readonly TimeSpan _position;

        public AudioPlaybackSnapshot(SocialDriveFile file, AudioPlaybackState state, TimeSpan position, TimeSpan duration, double volume, bool isMuted)
        {
            File = file;
            State = state;
            Duration = duration;
            Volume = volume;
            IsMuted = isMuted;
            _position = position;
            _stamp = Stopwatch.GetTimestamp();
        }

        public SocialDriveFile File { get; private set; }

        public AudioPlaybackState State { get; private set; }

        public TimeSpan Duration { get; private set; }

        public double Volume { get; private set; }

        public bool IsMuted { get; private set; }

        public bool IsWantedPlaying
        {
            get { return State == AudioPlaybackState.Playing || State == AudioPlaybackState.Opening || State == AudioPlaybackState.Buffering; }
        }

        public bool IsFor(SocialDriveFile file)
        {
            return File != null && file != null && string.Equals(File.Id, file.Id, StringComparison.Ordinal);
        }

        public TimeSpan PositionNow()
        {
            if (State != AudioPlaybackState.Playing) return _position;

            var elapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - _stamp) / (double)Stopwatch.Frequency);
            var now = _position + elapsed;
            return Duration > TimeSpan.Zero && now > Duration ? Duration : now;
        }
    }
}
