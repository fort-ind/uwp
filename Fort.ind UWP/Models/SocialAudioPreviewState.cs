namespace Fort.ind_UWP
{
    public sealed class SocialAudioPreviewState : BindableBase
    {
        private AudioPlaybackState _state;

        public AudioPlaybackState State
        {
            get { return _state; }
            private set
            {
                if (!SetProperty(ref _state, value)) return;
                OnPropertyChanged("Glyph");
                OnPropertyChanged("Label");
            }
        }

        public string Glyph
        {
            get
            {
                switch (_state)
                {
                    case AudioPlaybackState.Playing:
                    case AudioPlaybackState.Opening:
                    case AudioPlaybackState.Buffering:
                        return "";
                    case AudioPlaybackState.Failed:
                        return "";
                    default:
                        return "";
                }
            }
        }

        public string Label
        {
            get
            {
                switch (_state)
                {
                    case AudioPlaybackState.Playing:
                    case AudioPlaybackState.Opening:
                    case AudioPlaybackState.Buffering:
                        return LocalizedStrings.Get("SocialAudioPause");
                    case AudioPlaybackState.Failed:
                        return LocalizedStrings.Get("SocialAudioFailed");
                    default:
                        return LocalizedStrings.Get("SocialAudioPlay");
                }
            }
        }

        public void Update(SocialDriveFile file, AudioPlaybackSnapshot snapshot)
        {
            State = snapshot != null && snapshot.IsFor(file) ? snapshot.State : AudioPlaybackState.None;
        }
    }
}
