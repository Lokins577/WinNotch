using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private DisplayEnergyFeatureService? _subscribedDisplayEnergy;
    private DisplayEnergyWindow? _displayEnergyWindow;

    private void AttachDisplayEnergy()
    {
        if (!_isPrimary || _services.DisplayEnergy == null
            || ReferenceEquals(_subscribedDisplayEnergy, _services.DisplayEnergy)) return;
        if (_subscribedDisplayEnergy != null)
            _subscribedDisplayEnergy.OpenRequested -= OnDisplayEnergyRequested;
        _subscribedDisplayEnergy = _services.DisplayEnergy;
        _subscribedDisplayEnergy.OpenRequested += OnDisplayEnergyRequested;
    }

    private void DetachDisplayEnergy()
    {
        if (_subscribedDisplayEnergy != null)
            _subscribedDisplayEnergy.OpenRequested -= OnDisplayEnergyRequested;
        _subscribedDisplayEnergy = null;
        _displayEnergyWindow?.Close();
        _displayEnergyWindow = null;
    }

    private void OnDisplayEnergyRequested() => Dispatcher.BeginInvoke(OpenDisplayEnergy);

    private void OpenDisplayEnergy()
    {
        if (_services.DisplayEnergy == null) return;
        if (_displayEnergyWindow is { IsVisible: true })
        {
            _displayEnergyWindow.Activate();
            return;
        }
        _displayEnergyWindow = new DisplayEnergyWindow(
            _settings, _settingsStore, _services.DisplayEnergy) { Owner = this };
        _displayEnergyWindow.Closed += (_, _) => _displayEnergyWindow = null;
        _displayEnergyWindow.Show();
        _displayEnergyWindow.Activate();
    }
}
