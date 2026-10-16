using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private AppManagementFeatureService? _subscribedAppManagement;
    private AppManagementWindow? _appManagementWindow;

    private void AttachAppManagement()
    {
        if (!_isPrimary || _services.AppManagement == null
            || ReferenceEquals(_subscribedAppManagement, _services.AppManagement)) return;
        if (_subscribedAppManagement != null)
            _subscribedAppManagement.OpenRequested -= OnAppManagementRequested;
        _subscribedAppManagement = _services.AppManagement;
        _subscribedAppManagement.OpenRequested += OnAppManagementRequested;
    }

    private void DetachAppManagement()
    {
        if (_subscribedAppManagement != null)
            _subscribedAppManagement.OpenRequested -= OnAppManagementRequested;
        _subscribedAppManagement = null;
        _appManagementWindow?.Close();
        _appManagementWindow = null;
    }

    private void OnAppManagementRequested() => Dispatcher.Invoke(OpenAppManagement);

    private void OpenAppManagement()
    {
        if (_services.AppManagement == null) return;
        if (_appManagementWindow is { IsVisible: true })
        {
            _appManagementWindow.Activate();
            return;
        }
        _appManagementWindow = new AppManagementWindow(
            _services.AppManagement, _settings, _settingsStore) { Owner = this };
        _appManagementWindow.Closed += (_, _) => _appManagementWindow = null;
        _appManagementWindow.Show();
        _appManagementWindow.Activate();
    }
}
