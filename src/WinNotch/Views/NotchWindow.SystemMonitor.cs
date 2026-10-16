using WinNotch.Services;
using WinNotch.SystemMonitoring;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private SystemMonitorFeatureService? _subscribedSystemMonitor;
    private SystemMonitorWindow? _systemMonitorWindow;

    public event Action<SystemMetricAlert>? SystemMonitorAlertRaised;

    private void AttachSystemMonitor()
    {
        if (!_isPrimary || _services.SystemMonitor == null
            || ReferenceEquals(_subscribedSystemMonitor, _services.SystemMonitor)) return;
        if (_subscribedSystemMonitor != null)
        {
            _subscribedSystemMonitor.OpenRequested -= OnSystemMonitorRequested;
            _subscribedSystemMonitor.AlertRaised -= OnSystemMonitorAlertRaised;
        }
        _subscribedSystemMonitor = _services.SystemMonitor;
        _subscribedSystemMonitor.OpenRequested += OnSystemMonitorRequested;
        _subscribedSystemMonitor.AlertRaised += OnSystemMonitorAlertRaised;
    }

    private void DetachSystemMonitor()
    {
        if (_subscribedSystemMonitor != null)
        {
            _subscribedSystemMonitor.OpenRequested -= OnSystemMonitorRequested;
            _subscribedSystemMonitor.AlertRaised -= OnSystemMonitorAlertRaised;
        }
        _subscribedSystemMonitor = null;
        _systemMonitorWindow?.Close();
        _systemMonitorWindow = null;
    }

    private void OnSystemMonitorRequested() => Dispatcher.Invoke(OpenSystemMonitor);
    private void OnSystemMonitorAlertRaised(SystemMetricAlert alert)
        => SystemMonitorAlertRaised?.Invoke(alert);

    private void OpenSystemMonitor()
    {
        if (_services.SystemMonitor == null) return;
        if (_systemMonitorWindow is { IsVisible: true })
        {
            _systemMonitorWindow.Activate();
            return;
        }
        _systemMonitorWindow = new SystemMonitorWindow(
            _services.SystemMonitor, _settings, _settingsStore)
        {
            Owner = this
        };
        _systemMonitorWindow.Closed += (_, _) => _systemMonitorWindow = null;
        _systemMonitorWindow.Show();
        _systemMonitorWindow.Activate();
    }
}
