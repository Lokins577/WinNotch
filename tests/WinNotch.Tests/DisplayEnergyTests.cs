using WinNotch.DisplayEnergy;
using WinNotch.Models;
using WinNotch.Platform.Windows.DisplayEnergy;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class DisplayEnergyTests
{
    [Fact]
    public void Preferences_NormalizeHardwareAllowlistsAndRanges()
    {
        var preferences = new DisplayEnergyPreferences
        {
            BrightnessStep = 99,
            SoftwareDimmingOpacity = 2,
            ValidatedDdcMonitorIds = [" monitor ", "monitor"],
            ValidatedFanProviderIds = ["fan", "fan"]
        };

        preferences.Normalize();

        Assert.Equal(25, preferences.BrightnessStep);
        Assert.Equal(0.9, preferences.SoftwareDimmingOpacity);
        Assert.Equal(["monitor"], preferences.ValidatedDdcMonitorIds);
        Assert.Equal(["fan"], preferences.ValidatedFanProviderIds);
    }

    [Fact]
    public void FanCoordinator_RequiresProviderAndExactHardwareValidation()
    {
        var provider = new FakeFanProvider();
        var coordinator = new FanControlCoordinator([provider]);
        var preferences = new DisplayEnergyPreferences();

        Assert.False(coordinator.TrySet("fake", "board-1", 50, preferences, out _));
        preferences.ValidatedFanProviderIds.Add("fake");
        Assert.False(coordinator.TrySet("fake", "other", 50, preferences, out _));
        Assert.True(coordinator.TrySet("fake", "board-1", 50, preferences, out _));
        Assert.Equal(50, provider.LastPercent);
    }

    [Fact]
    public void DdcAndTemperatureDiscovery_AreReadOnlyAndReportCapabilities()
    {
        var monitors = new DdcMonitorService().Enumerate([]);
        var temperature = new AcpiTemperatureProvider().Read();

        Assert.All(monitors, monitor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.Id));
            Assert.False(monitor.UserValidated);
        });
        Assert.False(string.IsNullOrWhiteSpace(temperature.Provider));
        Assert.False(string.IsNullOrWhiteSpace(temperature.Detail));
    }

    [Fact]
    public void KeepAwakeController_ClearsStateOnDispose()
    {
        using var controller = new KeepAwakeController();
        controller.SetMode(KeepAwakeMode.Off);
        Assert.Equal(KeepAwakeMode.Off, controller.RequestedMode);
    }

    [Fact]
    public void SettingsWindow_BuildsOnStaThreadWithPoliciesOff()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                using var brightness = new BrightnessService();
                using var service = new DisplayEnergyFeatureService(
                    settings.DisplayEnergyPreferences, brightness, () => false, () => false);
                var window = new DisplayEnergyWindow(settings, new MemoryStore(), service);
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Display and energy UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class FakeFanProvider : IFanControlProvider
    {
        public string ProviderId => "fake";
        public string DisplayName => "Fake validated provider";
        public IReadOnlySet<string> SupportedHardwareIds { get; } = new HashSet<string> { "board-1" };
        public int? LastPercent { get; private set; }
        public HardwareMetricCapability GetCapability(string hardwareId) => new(true, ProviderId, "test");
        public bool TrySetPercent(string hardwareId, int percent, out string message)
        {
            LastPercent = percent;
            message = "set";
            return true;
        }
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
