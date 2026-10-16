using System.Windows.Threading;
using WinNotch.DisplayEnergy;
using WinNotch.Features;
using WinNotch.Platform.Windows.DisplayEnergy;
using WinNotch.Helpers;

namespace WinNotch.Services;

public sealed class DisplayEnergyFeatureService : IFeatureModule, IDisposable
{
    private readonly DisplayEnergyPreferences _preferences;
    private readonly BrightnessService _internalBrightness;
    private readonly Func<bool> _isFullscreen;
    private readonly Func<bool> _isRecording;
    private readonly KeepAwakeController _keepAwake = new();
    private readonly BluetoothSleepService _bluetooth = new();
    private readonly SoftwareDimmingService _dimming = new();
    private readonly DdcMonitorService _ddc = new();
    private readonly AcpiTemperatureProvider _temperature = new();
    private readonly DisplayEnergyHotkey _hotkey = new();
    private DispatcherTimer? _timer;

    public DisplayEnergyFeatureService(
        DisplayEnergyPreferences preferences,
        BrightnessService internalBrightness,
        Func<bool> isFullscreen,
        Func<bool> isRecording)
    {
        _preferences = preferences;
        _internalBrightness = internalBrightness;
        _isFullscreen = isFullscreen;
        _isRecording = isRecording;
    }

    public FeatureId Id => BuiltInFeatures.DisplayAndEnergy.Id;
    public string BluetoothStatus => _bluetooth.Status;
    public event Action? OpenRequested;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _preferences.Normalize();
        _hotkey.Pressed += OnHotkey;
        _hotkey.Register();
        await _bluetooth.StartAsync(_preferences.BluetoothPolicy);
        _dimming.Apply(_preferences.SoftwareDimmingOpacity);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshKeepAwake();
        _timer.Start();
        RefreshKeepAwake();
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Stop();
        _timer = null;
        _keepAwake.SetMode(KeepAwakeMode.Off);
        _dimming.Apply(0);
        _bluetooth.SetPolicy(BluetoothSleepPolicy.None);
        _hotkey.Pressed -= OnHotkey;
        _hotkey.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task ApplyPreferencesAsync()
    {
        _preferences.Normalize();
        _dimming.Apply(_preferences.SoftwareDimmingOpacity);
        await _bluetooth.StartAsync(_preferences.BluetoothPolicy);
        RefreshKeepAwake();
    }

    public IReadOnlyList<ExternalMonitorInfo> GetExternalMonitors()
        => _ddc.Enumerate(_preferences.ValidatedDdcMonitorIds);

    public bool TrySetExternalBrightness(string monitorId, int percent, out string message)
        => _ddc.TrySetBrightness(monitorId, percent, _preferences, out message);

    public void StepBrightness(int direction)
    {
        int delta = Math.Sign(direction) * _preferences.BrightnessStep;
        if (_internalBrightness.IsSupported)
            _internalBrightness.SetBrightness(_internalBrightness.Brightness + delta);
        foreach (var monitor in GetExternalMonitors().Where(value => value.BrightnessSupported && value.UserValidated))
        {
            int currentPercent = monitor.MaximumBrightness == monitor.MinimumBrightness
                ? 0
                : (int)Math.Round((monitor.CurrentBrightness - monitor.MinimumBrightness) * 100d
                                  / (monitor.MaximumBrightness - monitor.MinimumBrightness));
            _ddc.TrySetBrightness(monitor.Id, currentPercent + delta, _preferences, out _);
        }
    }

    public HardwareMetricCapability ReadTemperature() => _temperature.Read();

    public HardwareMetricCapability FanCapability => new(
        false,
        "Validated provider contract",
        "No built-in fan writer is enabled. Install or implement a provider with exact hardware IDs, then explicitly validate it.");

    private void OnHotkey() => OpenRequested?.Invoke();

    private void RefreshKeepAwake()
    {
        KeepAwakeMode requested = _preferences.KeepAwake;
        if ((_preferences.KeepAwakeWhileFullscreen && _isFullscreen())
            || (_preferences.KeepAwakeWhileRecording && _isRecording()))
            requested = KeepAwakeMode.SystemAndDisplay;
        _keepAwake.SetMode(requested);
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        _bluetooth.Dispose();
        _dimming.Dispose();
        _keepAwake.Dispose();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
