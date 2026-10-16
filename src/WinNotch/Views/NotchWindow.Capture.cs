using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private CaptureFeatureService? _subscribedCapture;
    private CaptureToolWindow? _captureToolWindow;

    private void AttachCaptureTool()
    {
        if (!_isPrimary || _services.Capture == null
            || ReferenceEquals(_subscribedCapture, _services.Capture)) return;
        if (_subscribedCapture != null)
            _subscribedCapture.OpenRequested -= OnCaptureRequested;
        _subscribedCapture = _services.Capture;
        _subscribedCapture.OpenRequested += OnCaptureRequested;
    }

    private void DetachCaptureTool()
    {
        if (_subscribedCapture != null)
            _subscribedCapture.OpenRequested -= OnCaptureRequested;
        _subscribedCapture = null;
        _captureToolWindow?.Close();
        _captureToolWindow = null;
    }

    private void OnCaptureRequested() => Dispatcher.Invoke(OpenCaptureTool);

    private void OpenCaptureTool()
    {
        if (_services.Capture == null) return;
        if (_captureToolWindow is { IsVisible: true })
        {
            _captureToolWindow.Activate();
            return;
        }
        _captureToolWindow = new CaptureToolWindow(_services.Capture, _settings, _settingsStore)
        {
            Owner = this
        };
        _captureToolWindow.Closed += (_, _) => _captureToolWindow = null;
        _captureToolWindow.Show();
        _captureToolWindow.Activate();
    }
}
