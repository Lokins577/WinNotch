using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private MediaToolsFeatureService? _subscribedMediaTools;
    private RecordingToolWindow? _recordingToolWindow;

    private void AttachMediaTools()
    {
        if (!_isPrimary || _services.MediaTools == null
            || ReferenceEquals(_subscribedMediaTools, _services.MediaTools)) return;
        if (_subscribedMediaTools != null)
            _subscribedMediaTools.OpenRequested -= OnMediaToolsRequested;
        _subscribedMediaTools = _services.MediaTools;
        _subscribedMediaTools.OpenRequested += OnMediaToolsRequested;
    }

    private void DetachMediaTools()
    {
        if (_subscribedMediaTools != null)
            _subscribedMediaTools.OpenRequested -= OnMediaToolsRequested;
        _subscribedMediaTools = null;
        _recordingToolWindow?.Close();
        _recordingToolWindow = null;
    }

    private void OnMediaToolsRequested() => Dispatcher.Invoke(OpenMediaTools);

    private void OpenMediaTools()
    {
        if (_services.MediaTools == null) return;
        if (_recordingToolWindow is { IsVisible: true })
        {
            _recordingToolWindow.Activate();
            return;
        }
        _recordingToolWindow = new RecordingToolWindow(
            _services.MediaTools, _settings, _settingsStore) { Owner = this };
        _recordingToolWindow.Closed += (_, _) => _recordingToolWindow = null;
        _recordingToolWindow.Show();
        _recordingToolWindow.Activate();
    }
}
