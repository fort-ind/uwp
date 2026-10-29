using System;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage
    {
        private Action<AudioPlaybackSnapshot> _audioHandler;

        private bool _audioHandlerAttached;

        private void AttachAudioHandler()
        {
            if (_audioHandlerAttached) return;

            _audioHandler = OnAudioChanged;
            AudioPlayerService.Subscribe(_audioHandler);
            _audioHandlerAttached = true;
            OnAudioChanged(AudioPlayerService.Current);
        }

        private void DetachAudioHandler()
        {
            if (!_audioHandlerAttached) return;

            AudioPlayerService.Unsubscribe(_audioHandler);
            _audioHandlerAttached = false;
        }

        private void OnAudioChanged(AudioPlaybackSnapshot snapshot)
        {
            try
            {
                if (snapshot.State != AudioPlaybackState.None)
                {
                    if (AudioPlayerHost == null) FindName("AudioPlayerHost");
                    AudioPlayerHost.Visibility = Visibility.Visible;
                }
                else if (AudioPlayerHost != null)
                {
                    AudioPlayerHost.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("MainPage: could not show the audio player", ex);
            }
        }
    }
}
