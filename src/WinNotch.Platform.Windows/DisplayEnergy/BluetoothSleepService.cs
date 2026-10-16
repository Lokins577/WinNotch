using Microsoft.Win32;
using Windows.Devices.Radios;
using WinNotch.DisplayEnergy;

namespace WinNotch.Platform.Windows.DisplayEnergy;

public sealed class BluetoothSleepService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BluetoothSleepPolicy _policy;
    private bool _restoreOnResume;
    private bool _started;
    private bool _accessAllowed;

    public string Status { get; private set; } = "Bluetooth sleep policy is off.";

    public async Task StartAsync(BluetoothSleepPolicy policy)
    {
        _policy = policy;
        if (!_started)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _started = true;
        }
        if (policy != BluetoothSleepPolicy.None)
        {
            var access = await Radio.RequestAccessAsync();
            _accessAllowed = access == RadioAccessStatus.Allowed;
            Status = access == RadioAccessStatus.Allowed
                ? "Bluetooth radio access allowed."
                : $"Bluetooth radio access denied: {access}.";
        }
    }

    public void SetPolicy(BluetoothSleepPolicy policy)
    {
        _policy = policy;
        if (policy == BluetoothSleepPolicy.None) _restoreOnResume = false;
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_policy == BluetoothSleepPolicy.None || !_accessAllowed) return;
        if (e.Mode == PowerModes.Suspend) await SetBluetoothForSuspendAsync();
        else if (e.Mode == PowerModes.Resume && _restoreOnResume) await RestoreBluetoothAsync();
    }

    private async Task SetBluetoothForSuspendAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var bluetooth = radios.Where(value => value.Kind == RadioKind.Bluetooth).ToArray();
            _restoreOnResume = _policy == BluetoothSleepPolicy.OffOnSuspendRestoreOnResume
                               && bluetooth.Any(value => value.State == RadioState.On);
            int denied = 0;
            foreach (var radio in bluetooth.Where(value => value.State == RadioState.On))
                if (await radio.SetStateAsync(RadioState.Off) != RadioAccessStatus.Allowed) denied++;
            Status = bluetooth.Length == 0 ? "No Bluetooth radio is exposed." : "Bluetooth off request sent for suspend.";
            if (denied > 0) Status += $" {denied} radio request(s) were denied.";
        }
        catch (Exception exception) { Status = exception.Message; }
        finally { _gate.Release(); }
    }

    private async Task RestoreBluetoothAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var radios = await Radio.GetRadiosAsync();
            int denied = 0;
            foreach (var radio in radios.Where(value => value.Kind == RadioKind.Bluetooth && value.State == RadioState.Off))
                if (await radio.SetStateAsync(RadioState.On) != RadioAccessStatus.Allowed) denied++;
            _restoreOnResume = false;
            Status = "Bluetooth restore request sent after resume.";
            if (denied > 0) Status += $" {denied} radio request(s) were denied.";
        }
        catch (Exception exception) { Status = exception.Message; }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_started) SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
