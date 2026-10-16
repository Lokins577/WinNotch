using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private AudioMixerFeatureService? _subscribedAudioMixer;
    private AudioMixerWindow? _audioMixerWindow;

    private void AttachAudioMixer()
    {
        if (!_isPrimary || _services.AudioMixer == null
            || ReferenceEquals(_subscribedAudioMixer, _services.AudioMixer)) return;
        if (_subscribedAudioMixer != null)
            _subscribedAudioMixer.OpenRequested -= OnAudioMixerRequested;
        _subscribedAudioMixer = _services.AudioMixer;
        _subscribedAudioMixer.OpenRequested += OnAudioMixerRequested;
    }

    private void DetachAudioMixer()
    {
        if (_subscribedAudioMixer != null)
            _subscribedAudioMixer.OpenRequested -= OnAudioMixerRequested;
        _subscribedAudioMixer = null;
        _audioMixerWindow?.Close();
        _audioMixerWindow = null;
    }

    private void OnAudioMixerRequested() => Dispatcher.Invoke(OpenAudioMixer);

    private void OpenAudioMixer()
    {
        if (_services.AudioMixer == null) return;
        if (_audioMixerWindow is { IsVisible: true })
        {
            _audioMixerWindow.Activate();
            return;
        }
        _audioMixerWindow = new AudioMixerWindow(
            _services.AudioMixer, _settings, _settingsStore, _services.AudioCapture)
        {
            Owner = this
        };
        _audioMixerWindow.Closed += (_, _) => _audioMixerWindow = null;
        _audioMixerWindow.Show();
        _audioMixerWindow.Activate();
    }
}
