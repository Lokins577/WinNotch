using System.Text.Json.Serialization;

namespace WinNotch.DisplayEnergy;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeepAwakeMode
{
    Off,
    System,
    SystemAndDisplay
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BluetoothSleepPolicy
{
    None,
    OffOnSuspend,
    OffOnSuspendRestoreOnResume
}

public sealed class DisplayEnergyPreferences
{
    public KeepAwakeMode KeepAwake { get; set; }
    public bool KeepAwakeWhileFullscreen { get; set; }
    public bool KeepAwakeWhileRecording { get; set; } = true;
    public int BrightnessStep { get; set; } = 10;
    public double SoftwareDimmingOpacity { get; set; }
    public BluetoothSleepPolicy BluetoothPolicy { get; set; }
    public List<string> ValidatedDdcMonitorIds { get; set; } = [];
    public List<string> ValidatedFanProviderIds { get; set; } = [];

    public void Normalize()
    {
        BrightnessStep = Math.Clamp(BrightnessStep, 1, 25);
        SoftwareDimmingOpacity = Math.Clamp(SoftwareDimmingOpacity, 0, 0.9);
        ValidatedDdcMonitorIds ??= [];
        ValidatedDdcMonitorIds = ValidatedDdcMonitorIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToList();
        ValidatedFanProviderIds ??= [];
        ValidatedFanProviderIds = ValidatedFanProviderIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToList();
    }
}

public sealed record ExternalMonitorInfo(
    string Id,
    string Description,
    int MinimumBrightness,
    int CurrentBrightness,
    int MaximumBrightness,
    bool BrightnessSupported,
    bool UserValidated);

public sealed record HardwareMetricCapability(
    bool Supported,
    string Provider,
    string Detail,
    double? Value = null,
    string? Unit = null);

public interface IFanControlProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    IReadOnlySet<string> SupportedHardwareIds { get; }
    HardwareMetricCapability GetCapability(string hardwareId);
    bool TrySetPercent(string hardwareId, int percent, out string message);
}

public sealed class FanControlCoordinator
{
    private readonly IReadOnlyList<IFanControlProvider> _providers;
    public FanControlCoordinator(IEnumerable<IFanControlProvider> providers)
        => _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));

    public bool TrySet(
        string providerId,
        string hardwareId,
        int percent,
        DisplayEnergyPreferences preferences,
        out string message)
    {
        if (!preferences.ValidatedFanProviderIds.Contains(providerId, StringComparer.Ordinal))
        {
            message = "The fan provider has not been explicitly validated by the user.";
            return false;
        }
        var provider = _providers.FirstOrDefault(value => value.ProviderId == providerId);
        if (provider == null || !provider.SupportedHardwareIds.Contains(hardwareId))
        {
            message = "The provider does not declare support for this exact hardware ID.";
            return false;
        }
        return provider.TrySetPercent(hardwareId, Math.Clamp(percent, 0, 100), out message);
    }
}
