using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private WindowManagementFeatureService? _subscribedWindowManager;
    private WindowSwitcherWindow? _windowSwitcher;

    private void AttachWindowManagement()
    {
        if (!_isPrimary || _services.WindowManager == null
            || ReferenceEquals(_subscribedWindowManager, _services.WindowManager)) return;
        if (_subscribedWindowManager != null)
            _subscribedWindowManager.OpenRequested -= OnWindowSwitcherRequested;
        _subscribedWindowManager = _services.WindowManager;
        _subscribedWindowManager.OpenRequested += OnWindowSwitcherRequested;
    }

    private void DetachWindowManagement()
    {
        if (_subscribedWindowManager != null)
            _subscribedWindowManager.OpenRequested -= OnWindowSwitcherRequested;
        _subscribedWindowManager = null;
        _windowSwitcher?.Close();
        _windowSwitcher = null;
    }

    private void OnWindowSwitcherRequested() => Dispatcher.Invoke(OpenWindowSwitcher);

    private void OpenWindowSwitcher()
    {
        if (_services.WindowManager == null) return;
        if (_windowSwitcher is { IsVisible: true })
        {
            _windowSwitcher.Activate();
            return;
        }

        _windowSwitcher = new WindowSwitcherWindow(_services.WindowManager.Backend)
        {
            Owner = this
        };
        _windowSwitcher.Closed += (_, _) => _windowSwitcher = null;
        _windowSwitcher.Show();
        _windowSwitcher.Activate();
    }
}
